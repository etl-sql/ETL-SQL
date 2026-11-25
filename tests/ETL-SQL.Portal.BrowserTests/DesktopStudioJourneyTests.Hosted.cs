using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Hosted;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using ETL_SQL.Portal.Tests;
using ETL_SQL.WorkstationEditor;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;

namespace ETL_SQL.Portal.BrowserTests;

public sealed partial class DesktopStudioJourneyTests
{
    private const string HostedFinanceCheckScript =
        "CREATE CONNECTION sample_data AS MOCKDB();\n"
        + "SELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private const string HostedSlowWaitScript = "WAITFOR DELAY '00:00:40';\n";
    private static readonly JsonSerializerOptions HostedJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task DesktopStudio_HostedExecutionJourney_OfflineSignInRunDeniedStopRecoveryAndOwnership()
    {
        using var workspace = new StudioTempWorkspace();
        using var registryTemp = new StudioTempWorkspace();
        using var sessionRegistryTemp = new StudioTempWorkspace();

        var financeFile = Path.Combine(workspace.Root, "FinanceCheck.etlsql");
        var slowFile = Path.Combine(workspace.Root, "SlowWait.etlsql");
        var otherFile = Path.Combine(workspace.Root, "OtherLocal.etlsql");
        await File.WriteAllTextAsync(financeFile, HostedFinanceCheckScript);
        await File.WriteAllTextAsync(slowFile, HostedSlowWaitScript);
        await File.WriteAllTextAsync(otherFile, "SELECT 99 AS LocalOnlyValue;");

        var registryPath = Path.Combine(registryTemp.Root, "hosted-servers.json");
        var serverRegistry = new HostedServerRegistryStore(registryPath);

        // Register a pre-existing server pointing at an unreachable loopback port first
        // to prove Studio defaults to Local (Offline) and runs locally with Portal stopped.
        var closedPort = GetFreeLoopbackPort();
        serverRegistry.AddOrUpdate("offline-portal", "Stopped Portal", $"http://127.0.0.1:{closedPort}", "Dev");

        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new HostedLiveKestrelPortalFactory(orchestrator);
        portal.Start();

        var seeded = await SeedHostedAcceptanceTopologyAsync(portal);
        await using var proxy = new HostedLoopbackFaultProxy(portal.ServerAddress, portal);
        proxy.Start();

        serverRegistry.AddOrUpdate("dev", "Loopback Portal", proxy.ProxyBaseUrl, "Dev");

        var capturedSecrets = new HashSet<string>(StringComparer.Ordinal);
        var clock = new HostedAdvanceableClock(DateTimeOffset.UtcNow);
        var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
        {
            await DrivePortalOidcConsentAsync(authorizeUri, code => capturedSecrets.Add(code), ct);
            return true;
        });

        var options = new WorkstationEditorOptions(
            workspace.Root,
            financeFile,
            0,
            false,
            "st58-studio-token",
            StudioMode: true,
            InstanceId: Guid.NewGuid().ToString("D"),
            HostedServerRegistryPath: registryPath);

        await using var host = WorkstationEditorApp.Create([], options, services =>
        {
            services.AddSingleton(authenticator);
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(provider => new HostedRunCoordinator(
                provider.GetRequiredService<HostedClientSessionService>(),
                provider.GetRequiredService<HostedPipelineClient>(),
                provider.GetRequiredService<TimeProvider>(),
                initialPollDelay: TimeSpan.FromMilliseconds(150),
                maxPollDelay: TimeSpan.FromMilliseconds(400)));
        });
        await host.StartAsync();

        var sessionService = host.Services.GetRequiredService<HostedClientSessionService>();
        void CaptureActiveSecrets()
        {
            foreach (var secret in sessionService.GetActiveSecrets())
                capturedSecrets.Add(secret);
        }

        // Write a local Studio session registry entry (the same way the desktop launcher does)
        // so we can assert zero Portal tokens ever enter StudioSessionRegistry.
        var localSessionRegistry = new StudioSessionRegistry(sessionRegistryTemp.Root);
        var hostPort = new Uri(WorkstationEditorApp.GetListeningUrl(host)).Port;
        await localSessionRegistry.WriteAsync(new StudioSessionRecord(
            options.InstanceId!,
            workspace.Root,
            Environment.ProcessId,
            hostPort,
            DateTimeOffset.UtcNow,
            new StudioAuthenticationMetadata("X-ETLSQL-EDITOR-TOKEN", options.SessionToken)));

        await using var browser = await fixture.NewSessionAsync();
        var page = browser.Page;
        await page.GotoWithDiagnosticsAsync(StudioUrl(host, options.SessionToken));
        await WaitForStudioAsync(browser);

        // 1. Offline startup defaults to Local (Offline) and local execution works without Portal.
        var targetBtn = page.Locator("[data-hosted-target-btn]");
        await targetBtn.WaitForAsync();
        Assert.Equal("local", await targetBtn.GetAttributeAsync("data-hosted-mode"));
        Assert.Contains("Local (Offline)", await targetBtn.InnerTextAsync(), StringComparison.Ordinal);

        await page.Locator("[data-action='run']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => e.type === 'done' && e.exitCode === 0)");

        // 2. Explicit Hosted target picker & sign-in as alice -> displays Portal, tenant, and user before submission.
        portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
        await targetBtn.ClickAsync();
        await page.Locator("[data-hosted-target-modal]").WaitForAsync();
        await page.Locator("[data-hosted-connect='dev']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.getAttribute('data-hosted-signed-in') === 'true'");
        CaptureActiveSecrets();

        var signedInLabel = await targetBtn.InnerTextAsync();
        Assert.Contains("Loopback Portal", signedInLabel, StringComparison.Ordinal);
        Assert.Contains("portal-host", signedInLabel, StringComparison.Ordinal);
        Assert.Contains("alice", signedInLabel, StringComparison.Ordinal);

        // Verify client ID and execute-only scopes (studio.read studio.run, no studio.save).
        Assert.Equal(HostedClientIds.DesktopStudio, sessionService.ClientId);
        Assert.Contains("studio.read", sessionService.Session!.Scopes);
        Assert.Contains("studio.run", sessionService.Session!.Scopes);
        Assert.DoesNotContain("studio.save", sessionService.Session!.Scopes);

        // 3. Unsaved edits offer Run Last Saved Revision or Cancel and preserve the local buffer.
        const string unsavedMarker = "-- st58 unsaved local edit";
        await page.EvaluateAsync(
            "marker => window.__STUDIO__.state.editorInstance.setValue(window.__STUDIO__.state.editorInstance.getValue() + `\\n${marker}\\n`)",
            unsavedMarker);
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === true");

        var histBeforeAliceRun = await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-dirty-modal]").WaitForAsync();
        await page.Locator("[data-hosted-dirty-cancel]").ClickAsync();
        await page.Locator("[data-hosted-dirty-modal]").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        Assert.Equal(histBeforeAliceRun, await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId));
        Assert.Contains(unsavedMarker, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));

        // Now choose Run Last Saved Revision -> runs saved Portal revision, preserves dirty buffer.
        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-dirty-modal]").WaitForAsync();
        await page.Locator("[data-hosted-dirty-run]").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => e.type === 'done' && e.status === 'Complete')");
        Assert.Equal(histBeforeAliceRun + 1, await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        Assert.Contains(unsavedMarker, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));

        // 4. Execute-only user (bob) denied by Orchestrator (403 run_grant_denied), zero new runs.
        var histBeforeBob = await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
        portal.StubOidc.Identity = new OidcIdentity("oid-bob", "bob", "bob@alpha.test", ["DataReaders"]);
        await targetBtn.ClickAsync();
        await page.Locator("[data-hosted-target-modal]").WaitForAsync();
        await page.Locator("[data-hosted-connect='dev']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.textContent?.includes('bob')");
        CaptureActiveSecrets();

        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-dirty-modal]").WaitForAsync();
        await page.Locator("[data-hosted-dirty-run]").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => e.type === 'done' && e.status === 'Refused')");
        var bobTraceText = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.__STUDIO__.state.documents[0].studioContext.resultsTrace)");
        Assert.Contains("run_grant_denied", bobTraceText, StringComparison.Ordinal);
        Assert.Equal(histBeforeBob, await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId));

        // 5. Switch back to alice and test Stop on SlowWait.etlsql -> terminal CANCELLED.
        portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
        await targetBtn.ClickAsync();
        await page.Locator("[data-hosted-target-modal]").WaitForAsync();
        await page.Locator("[data-hosted-connect='dev']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.textContent?.includes('alice')");
        CaptureActiveSecrets();

        // Open SlowWait.etlsql from Explorer.
        await page.Locator("[data-activity='explorer']").ClickAsync();
        await page.Locator("[data-explorer-file='SlowWait.etlsql']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.some(d => d.path === 'SlowWait.etlsql' && d.id === window.__STUDIO__.state.activeDocId)");

        await page.Locator("[data-action='run']").ClickAsync();
        var stopBtn = page.Locator("[data-action='stop']");
        await stopBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId).studioContext.resultsTrace.some(e => (e.text || '').includes('Accepted run #'))");
        await stopBtn.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId).studioContext.resultsTrace.some(e => e.type === 'done' && e.status === 'Cancelled')",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // 6. Dropped 202 acceptance response -> recovers exact runId -> produces exactly ONE run.
        await page.Locator("[data-explorer-file='FinanceCheck.etlsql']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId)?.path === 'FinanceCheck.etlsql'");

        var histBeforeDrop1 = await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
        proxy.ArmDropNext202(revokeAliceSessionAfterDrop: false);
        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-dirty-modal]").WaitForAsync();
        await page.Locator("[data-hosted-dirty-run]").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => (e.text || '').includes('Recovered accepted run')) && window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => e.type === 'done' && e.status === 'Complete')",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        var drop1Trace = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.__STUDIO__.state.documents[0].studioContext.resultsTrace)");
        Assert.Contains("Recovered accepted run", drop1Trace, StringComparison.Ordinal);
        Assert.Equal(histBeforeDrop1 + 1, await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId));

        // 7. Session expiry and caller-scoped recovery (bob cannot see alice's pending request; alice recovers with original key -> 1 run).
        var histBeforeDrop2 = await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
        proxy.ArmDropNext202(revokeAliceSessionAfterDrop: true);
        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-dirty-modal]").WaitForAsync();
        await page.Locator("[data-hosted-dirty-run]").ClickAsync();

        // Session expired banner appears; dirty buffer preserved; did NOT fall back to local.
        await page.Locator("[data-hosted-reauth]").WaitForAsync(new() { Timeout = 20_000 });
        Assert.Equal("hosted", await targetBtn.GetAttributeAsync("data-hosted-mode"));
        Assert.Equal("false", await targetBtn.GetAttributeAsync("data-hosted-signed-in"));
        Assert.Contains(unsavedMarker, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));

        // Sign in as bob -> alice's pending submission must NOT be visible or recoverable.
        portal.StubOidc.Identity = new OidcIdentity("oid-bob", "bob", "bob@alpha.test", ["DataReaders"]);
        await targetBtn.ClickAsync();
        await page.Locator("[data-hosted-target-modal]").WaitForAsync();
        await page.Locator("[data-hosted-connect='dev']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.textContent?.includes('bob')");
        CaptureActiveSecrets();
        Assert.Equal(0, await page.Locator("[data-hosted-recover]").CountAsync());

        // Sign back in as alice -> pending submission reappears and recovers with original Idempotency-Key -> 1 run.
        portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
        await targetBtn.ClickAsync();
        await page.Locator("[data-hosted-target-modal]").WaitForAsync();
        await page.Locator("[data-hosted-connect='dev']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.textContent?.includes('alice')");
        CaptureActiveSecrets();

        var recoverBtn = page.Locator("[data-hosted-recover]");
        await recoverBtn.WaitForAsync();
        await recoverBtn.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => (e.text || '').includes('Recovered accepted run') ) && window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => e.type === 'done' && e.status === 'Complete')",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        Assert.Equal(histBeforeDrop2 + 1, await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId));

        // 8. Late-response ownership: start a delayed hosted run on FinanceCheck.etlsql, immediately switch to
        //    OtherLocal.etlsql AND switch target to Local (Offline). The late response must NEVER paint onto
        //    OtherLocal.etlsql or Local (Offline), and stays owned by FinanceCheck.etlsql's hosted target.
        await page.Locator("[data-explorer-file='OtherLocal.etlsql']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId)?.path === 'OtherLocal.etlsql'");
        await page.Locator("[data-explorer-file='FinanceCheck.etlsql']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId)?.path === 'FinanceCheck.etlsql'");

        var aliceTargetKey = await page.EvaluateAsync<string>(
            "() => document.querySelector('[data-hosted-target-btn]')?.textContent || ''");
        Assert.Contains("alice", aliceTargetKey, StringComparison.Ordinal);

        proxy.ArmDelayNextRunSubmit(TimeSpan.FromMilliseconds(1200));
        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-dirty-modal]").WaitForAsync();
        await page.Locator("[data-hosted-dirty-run]").ClickAsync();

        // While the hosted submission is delayed in flight, switch to OtherLocal.etlsql and then switch target to Local (Offline).
        await page.Locator("[data-explorer-file='OtherLocal.etlsql']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId)?.path === 'OtherLocal.etlsql'");

        await targetBtn.ClickAsync();
        await page.Locator("[data-hosted-target-modal]").WaitForAsync();
        await page.Locator("[data-hosted-select-local]").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.getAttribute('data-hosted-mode') === 'local'");

        // Wait past the delayed response window and verify OtherLocal.etlsql + Local (Offline) was NEVER polluted.
        await Task.Delay(2000);
        var otherDocTrace = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.__STUDIO__.state.documents.find(d => d.path === 'OtherLocal.etlsql').studioContext.resultsTrace)");
        Assert.DoesNotContain("FinanceCheck", otherDocTrace, StringComparison.Ordinal);
        Assert.DoesNotContain("PIPELINE:", otherDocTrace, StringComparison.Ordinal);
        Assert.DoesNotContain("Loopback Portal", otherDocTrace, StringComparison.Ordinal);

        var financeLocalTrace = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.__STUDIO__.state.documents.find(d => d.path === 'FinanceCheck.etlsql').studioContext.resultsTraceByTarget['local'] || [])");
        Assert.DoesNotContain("PIPELINE:", financeLocalTrace, StringComparison.Ordinal);

        // 9. Secret hygiene check: zero Portal tokens/codes in browser DOM, storage, registry files, or workspace files.
        Assert.True(capturedSecrets.Count >= 6, $"Expected captured Portal secrets to audit, got {capturedSecrets.Count}");
        var browserSurface = await page.EvaluateAsync<string>(
            """
            () => [
                document.documentElement.outerHTML,
                JSON.stringify(localStorage),
                JSON.stringify(sessionStorage),
                JSON.stringify(window.__STUDIO__?.state?.documents?.map(d => d.studioContext?.resultsTraceByTarget) || [])
            ].join('\n')
            """);
        var diskSurface = File.ReadAllText(registryPath)
            + "\n" + string.Join("\n", Directory.EnumerateFiles(sessionRegistryTemp.Root, "*", SearchOption.AllDirectories).Select(File.ReadAllText))
            + "\n" + string.Join("\n", Directory.EnumerateFiles(workspace.Root, "*", SearchOption.AllDirectories).Select(File.ReadAllText));

        foreach (var secret in capturedSecrets)
        {
            Assert.DoesNotContain(secret, browserSurface, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, diskSurface, StringComparison.Ordinal);
        }

        Assert.Empty(browser.PageErrors);
    }

    [Fact]
    public async Task DesktopStudio_HostedRecovery_WhenOriginalDocumentClosed_DoesNotAttachToOtherDocument()
    {
        using var workspace = new StudioTempWorkspace();
        using var registryTemp = new StudioTempWorkspace();

        var financeFile = Path.Combine(workspace.Root, "FinanceCheck.etlsql");
        var otherFile = Path.Combine(workspace.Root, "OtherLocal.etlsql");
        await File.WriteAllTextAsync(financeFile, HostedFinanceCheckScript);
        await File.WriteAllTextAsync(otherFile, "SELECT 99 AS LocalOnlyValue;");

        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new HostedLiveKestrelPortalFactory(orchestrator);
        portal.Start();
        var seeded = await SeedHostedAcceptanceTopologyAsync(portal);
        await using var proxy = new HostedLoopbackFaultProxy(portal.ServerAddress, portal);
        proxy.Start();

        var registryPath = Path.Combine(registryTemp.Root, "hosted-servers.json");
        new HostedServerRegistryStore(registryPath).AddOrUpdate("dev", "Loopback Portal", proxy.ProxyBaseUrl, "Dev");

        var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
        {
            await DrivePortalOidcConsentAsync(authorizeUri, _ => { }, ct);
            return true;
        });

        var options = new WorkstationEditorOptions(
            workspace.Root, financeFile, 0, false, "st58-closed-doc-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D"), HostedServerRegistryPath: registryPath);

        await using var host = WorkstationEditorApp.Create([], options, services =>
        {
            services.AddSingleton(authenticator);
            services.AddSingleton(provider => new HostedRunCoordinator(
                provider.GetRequiredService<HostedClientSessionService>(),
                provider.GetRequiredService<HostedPipelineClient>(),
                provider.GetRequiredService<TimeProvider>(),
                initialPollDelay: TimeSpan.FromMilliseconds(150),
                maxPollDelay: TimeSpan.FromMilliseconds(400)));
        });
        await host.StartAsync();

        await using var browser = await fixture.NewSessionAsync();
        var page = browser.Page;
        await page.GotoWithDiagnosticsAsync(StudioUrl(host, options.SessionToken));
        await WaitForStudioAsync(browser);

        // Sign in as alice.
        portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
        var targetBtn = page.Locator("[data-hosted-target-btn]");
        await targetBtn.ClickAsync();
        await page.Locator("[data-hosted-connect='dev']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.getAttribute('data-hosted-signed-in') === 'true'");

        // Arm dropped 202 + revoked session, then start hosted run on FinanceCheck.etlsql.
        var histBefore = await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId);
        proxy.ArmDropNext202(revokeAliceSessionAfterDrop: true);
        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-reauth]").WaitForAsync(new() { Timeout = 20_000 });

        // Open OtherLocal.etlsql and CLOSE FinanceCheck.etlsql BEFORE recovering.
        await page.Locator("[data-activity='explorer']").ClickAsync();
        await page.Locator("[data-explorer-file='OtherLocal.etlsql']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents.find(d => d.id === window.__STUDIO__.state.activeDocId)?.path === 'OtherLocal.etlsql'");

        await page.Locator("#etlsql-tab-doc-primary .etlsql-tab-close").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => !window.__STUDIO__.state.documents.some(d => d.path === 'FinanceCheck.etlsql')");

        // Sign back in as alice and recover.
        await page.Locator("[data-hosted-reauth]").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.getAttribute('data-hosted-signed-in') === 'true'");
        await page.Locator("[data-hosted-recover]").ClickAsync();

        // Recovery status must appear in the hosted banner (original document closed) and finish SUCCESS.
        var recoveryBanner = page.Locator("[data-hosted-recovery-status]");
        await recoveryBanner.WaitForAsync(new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "() => (document.querySelector('[data-hosted-recovery-status]')?.textContent || '').includes('SUCCESS')",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        Assert.Equal(histBefore + 1, await GetHostedHistoryCountAsync(orchestrator, seeded.FinancePipelineId));

        // OtherLocal.etlsql MUST NOT receive FinanceCheck's recovered trace.
        var otherTrace = await page.EvaluateAsync<string>(
            "() => JSON.stringify(window.__STUDIO__.state.documents.find(d => d.path === 'OtherLocal.etlsql').studioContext.resultsTrace)");
        Assert.DoesNotContain("FinanceCheck", otherTrace, StringComparison.Ordinal);
        Assert.DoesNotContain("PIPELINE:", otherTrace, StringComparison.Ordinal);
        Assert.DoesNotContain("Recovered accepted run", otherTrace, StringComparison.Ordinal);
        Assert.Empty(browser.PageErrors);
    }

    [Fact]
    public async Task DesktopStudio_HostedPipelinePicker_PagesBeyondFiftyResultsAndRunsSelection()
    {
        using var workspace = new StudioTempWorkspace();
        using var registryTemp = new StudioTempWorkspace();

        var localFile = Path.Combine(workspace.Root, "PickFromCatalog.etlsql");
        await File.WriteAllTextAsync(localFile, "SELECT 1 AS Placeholder;");

        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new HostedLiveKestrelPortalFactory(orchestrator);
        portal.Start();
        await SeedHostedAcceptanceTopologyAsync(portal);

        // Seed 50 more pipelines so the target pipeline sits beyond the first page of 50.
        int pageTwoPipelineId;
        using (var http = new HttpClient { BaseAddress = new Uri(portal.ServerAddress) })
        {
            var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var adminToken = (await loginResp.Content.ReadFromJsonAsync<JsonObject>(HostedJson))!["token"]!.GetValue<string>();

            int folderId;
            string alicePrincipalKey;
            using (var scope = portal.BrowserServices.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                folderId = (await db.Folders.SingleAsync(f => f.Path == "/Finance")).Id;
                alicePrincipalKey = (await db.Users.SingleAsync(u => u.UserName == "alice")).PrincipalKey!;
            }

            for (var i = 1; i <= 50; i++)
            {
                await CreateHostedPipelineAsync(http, adminToken, folderId, $"BulkPipeline_{i:D2}.etlsql", HostedFinanceCheckScript);
            }
            var (zebraId, zebraVersion) = await CreateHostedPipelineAsync(http, adminToken, folderId, "ZebraPageTwo.etlsql", HostedFinanceCheckScript);
            pageTwoPipelineId = zebraId;
            await ProvisionAndGrantHostedAsync(http, adminToken, zebraId, zebraVersion, alicePrincipalKey, cancelImmediately: false);
        }

        var registryPath = Path.Combine(registryTemp.Root, "hosted-servers.json");
        new HostedServerRegistryStore(registryPath).AddOrUpdate("dev", "Loopback Portal", portal.ServerAddress, "Dev");

        var authenticator = new LoopbackPkceAuthenticator(async (authorizeUri, ct) =>
        {
            await DrivePortalOidcConsentAsync(authorizeUri, _ => { }, ct);
            return true;
        });

        var options = new WorkstationEditorOptions(
            workspace.Root, localFile, 0, false, "st58-paging-token",
            StudioMode: true, InstanceId: Guid.NewGuid().ToString("D"), HostedServerRegistryPath: registryPath);

        await using var host = WorkstationEditorApp.Create([], options, services =>
        {
            services.AddSingleton(authenticator);
            services.AddSingleton(provider => new HostedRunCoordinator(
                provider.GetRequiredService<HostedClientSessionService>(),
                provider.GetRequiredService<HostedPipelineClient>(),
                provider.GetRequiredService<TimeProvider>(),
                initialPollDelay: TimeSpan.FromMilliseconds(150),
                maxPollDelay: TimeSpan.FromMilliseconds(400)));
        });
        await host.StartAsync();

        await using var browser = await fixture.NewSessionAsync();
        var page = browser.Page;
        await page.GotoWithDiagnosticsAsync(StudioUrl(host, options.SessionToken));
        await WaitForStudioAsync(browser);

        portal.StubOidc.Identity = new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
        await page.Locator("[data-hosted-target-btn]").ClickAsync();
        await page.Locator("[data-hosted-connect='dev']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-hosted-target-btn]')?.getAttribute('data-hosted-signed-in') === 'true'");

        var histBefore = await GetHostedHistoryCountAsync(orchestrator, pageTwoPipelineId);
        await page.Locator("[data-action='run']").ClickAsync();
        await page.Locator("[data-hosted-pipeline-modal]").WaitForAsync();

        // Page 1 must show 50 items and offer Load More (ZebraPageTwo is on page 2).
        Assert.Equal(50, await page.Locator("[data-hosted-pick-pipeline]").CountAsync());
        Assert.Equal(0, await page.Locator($"[data-hosted-pick-pipeline='{pageTwoPipelineId}']").CountAsync());

        var loadMoreBtn = page.Locator("[data-hosted-pipeline-load-more]");
        await loadMoreBtn.WaitForAsync();
        await loadMoreBtn.ClickAsync();

        // Page 2 appends ZebraPageTwo.etlsql; select and run it.
        var zebraChoice = page.Locator($"[data-hosted-pick-pipeline='{pageTwoPipelineId}']");
        await zebraChoice.WaitForAsync();
        await zebraChoice.ClickAsync();

        await page.WaitForFunctionAsync(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace.some(e => e.type === 'done' && e.status === 'Complete')",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        Assert.Equal(histBefore + 1, await GetHostedHistoryCountAsync(orchestrator, pageTwoPipelineId));
        Assert.Empty(browser.PageErrors);
    }

    private static int GetFreeLoopbackPort()
    {
        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private static async Task DrivePortalOidcConsentAsync(
        Uri authorizeUri,
        Action<string> onCapturedCode,
        CancellationToken ct)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = cookies
        };
        using var browser = new HttpClient(handler);

        var currentUri = authorizeUri;
        HttpResponseMessage resp;
        while (true)
        {
            resp = await browser.GetAsync(currentUri, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is not null)
            {
                var next = resp.Headers.Location.IsAbsoluteUri
                    ? resp.Headers.Location
                    : new Uri(currentUri, resp.Headers.Location);
                resp.Dispose();

                if (next.Host.Equals("idp.acceptance.test", StringComparison.OrdinalIgnoreCase))
                {
                    var origin = $"{authorizeUri.Scheme}://{authorizeUri.Authority}";
                    currentUri = new Uri($"{origin}/api/auth/oidc/callback?code=mock-oidc-code&state=acceptance-oidc-state");
                    continue;
                }

                currentUri = next;
                continue;
            }
            break;
        }

        using (resp)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var html = await resp.Content.ReadAsStringAsync(ct);
            var csrfMatch = Regex.Match(html, "name=\"csrf_token\"\\s+value=\"([^\"]+)\"");
            Assert.True(csrfMatch.Success, "Consent page did not contain csrf_token.");
            var csrf = WebUtility.HtmlDecode(csrfMatch.Groups[1].Value);
            var txMatch = Regex.Match(currentUri.Query, "[?&]tx=([^&]+)");
            Assert.True(txMatch.Success, $"Consent URL did not contain tx query param: {currentUri}");
            var tx = Uri.UnescapeDataString(txMatch.Groups[1].Value);
            var origin = $"{authorizeUri.Scheme}://{authorizeUri.Authority}";

            using var approveReq = new HttpRequestMessage(HttpMethod.Post, $"{origin}/api/auth/client/consent")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["tx"] = tx,
                    ["csrf_token"] = csrf,
                    ["decision"] = "approve"
                })
            };
            approveReq.Headers.TryAddWithoutValidation("Origin", origin);
            using var approveResp = await browser.SendAsync(approveReq, ct);
            Assert.True((int)approveResp.StatusCode is >= 300 and < 400 && approveResp.Headers.Location is not null,
                $"Expected redirect from consent approval, got {(int)approveResp.StatusCode}");

            var callbackUri = approveResp.Headers.Location!;
            var codeMatch = Regex.Match(callbackUri.Query, "[?&]code=([^&]+)");
            if (codeMatch.Success)
                onCapturedCode(Uri.UnescapeDataString(codeMatch.Groups[1].Value));

            using var cbResp = await browser.GetAsync(callbackUri, ct);
            Assert.Equal(HttpStatusCode.OK, cbResp.StatusCode);
        }
    }

    private static async Task<(int FinancePipelineId, int SlowPipelineId)> SeedHostedAcceptanceTopologyAsync(HostedLiveKestrelPortalFactory portal)
    {
        int folderId;
        using (var scope = portal.BrowserServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
            admin.MustChangePassword = false;

            var authorsGroup = new ETL_SQL.Portal.Data.Group { Name = "DataAuthors", Provider = "OIDC", AdGroup = "DataAuthors" };
            var readersGroup = new ETL_SQL.Portal.Data.Group { Name = "DataReaders", Provider = "OIDC", AdGroup = "DataReaders" };
            db.Groups.AddRange(authorsGroup, readersGroup);
            await db.SaveChangesAsync();

            db.GroupStudioCapabilities.AddRange(
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.StudioAccess },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptRead },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptRun },
                new GroupStudioCapability { GroupId = authorsGroup.Id, Capability = StudioCapabilities.ScriptSave },
                new GroupStudioCapability { GroupId = readersGroup.Id, Capability = StudioCapabilities.StudioAccess },
                new GroupStudioCapability { GroupId = readersGroup.Id, Capability = StudioCapabilities.ScriptRead },
                new GroupStudioCapability { GroupId = readersGroup.Id, Capability = StudioCapabilities.ScriptRun });

            var folder = new Folder { Name = "Finance", Path = "/Finance", OwnerId = admin.Id };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
            folderId = folder.Id;

            db.FolderAcls.AddRange(
                new FolderAcl { FolderId = folderId, GroupId = authorsGroup.Id, Permission = FolderPermission.Author },
                new FolderAcl { FolderId = folderId, GroupId = readersGroup.Id, Permission = FolderPermission.Execute });

            var alice = new PortalUser
            {
                TenantId = "portal-host",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@alpha.test",
                NormalizedEmail = "ALICE@ALPHA.TEST",
                IsActive = true,
                MustChangePassword = false,
                Provider = "OIDC",
                ExternalSubject = "oid-alice",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            db.Users.Add(alice);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = alice.Id, GroupId = authorsGroup.Id });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(portal.ServerAddress) };
        var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var adminToken = (await loginResp.Content.ReadFromJsonAsync<JsonObject>(HostedJson))!["token"]!.GetValue<string>();

        var (financeId, financeVersion) = await CreateHostedPipelineAsync(http, adminToken, folderId, "FinanceCheck.etlsql", HostedFinanceCheckScript);
        var (slowId, slowVersion) = await CreateHostedPipelineAsync(http, adminToken, folderId, "SlowWait.etlsql", HostedSlowWaitScript);

        string alicePrincipalKey;
        using (var scope = portal.BrowserServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            alicePrincipalKey = (await db.Users.SingleAsync(u => u.UserName == "alice")).PrincipalKey!;
        }

        await ProvisionAndGrantHostedAsync(http, adminToken, financeId, financeVersion, alicePrincipalKey, cancelImmediately: false);
        await ProvisionAndGrantHostedAsync(http, adminToken, slowId, slowVersion, alicePrincipalKey, cancelImmediately: true);

        return (financeId, slowId);
    }

    private static async Task<(int Id, long Version)> CreateHostedPipelineAsync(
        HttpClient http, string token, int folderId, string name, string script)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/studio/reports")
        {
            Content = JsonContent.Create(new { folderId, name, scriptText = script, kind = "Pipeline" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<JsonObject>(HostedJson))!;
        return (body["id"]!.GetValue<int>(), body["version"]!.GetValue<long>());
    }

    private static async Task ProvisionAndGrantHostedAsync(
        HttpClient http, string token, int pipelineId, long version, string principalKey, bool cancelImmediately)
    {
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs")
        {
            Content = JsonContent.Create(new { version })
        };
        submitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var submitResp = await http.SendAsync(submitReq);
        Assert.Equal(HttpStatusCode.Accepted, submitResp.StatusCode);
        var submitted = (await submitResp.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(HostedJson))!;

        if (cancelImmediately)
        {
            using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs/{submitted.RunId}/cancel");
            cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var _ = await http.SendAsync(cancelReq);
        }

        for (var i = 0; i < 60; i++)
        {
            using var pollReq = new HttpRequestMessage(HttpMethod.Get, $"/api/studio/pipelines/{pipelineId}/runs/{submitted.RunId}");
            pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var pollResp = await http.SendAsync(pollReq);
            if (pollResp.IsSuccessStatusCode)
            {
                var run = await pollResp.Content.ReadFromJsonAsync<PipelineRunDto>(HostedJson);
                if (run is not null && run.IsTerminal)
                    break;
            }
            await Task.Delay(100);
        }

        using var grantReq = new HttpRequestMessage(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{pipelineId}")}/USER/{principalKey}")
        {
            Content = JsonContent.Create(new { permission = "EXECUTE" })
        };
        grantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var grantResp = await http.SendAsync(grantReq);
        Assert.True(grantResp.IsSuccessStatusCode, $"Failed to grant EXECUTE on PIPELINE:{pipelineId}: {(int)grantResp.StatusCode}");
    }

    private static async Task<int> GetHostedHistoryCountAsync(OrchestratorWebFactory orchestrator, int pipelineId)
    {
        using var scope = orchestrator.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
        var job = await store.GetJobAsync("portal-host", $"PIPELINE:{pipelineId}");
        return job is null ? 0 : (await store.GetHistoryAsync(job.Id, 200)).Count();
    }

    private sealed class HostedAdvanceableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class HostedLiveKestrelPortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        public string ServerAddress { get; private set; } = string.Empty;
        public IServiceProvider BrowserServices => Services;
        public HostedMutableStubOidc StubOidc { get; } = new();

        public void Start()
        {
            _ = orchestrator.Server;
            UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var client = CreateClient();
            ServerAddress = client.BaseAddress!.AbsoluteUri.TrimEnd('/');
        }

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Portal:Identity:Oidc:Enabled"] = "true";
            settings["Portal:Identity:Oidc:Authority"] = "https://idp.acceptance.test";
            settings["Portal:Identity:Oidc:ClientId"] = "portal-acceptance";
        }

        protected override void CustomizePortalConfig(ETL_SQL.Portal.PortalConfig config)
        {
            config.Identity.Oidc.Enabled = true;
            config.Identity.Oidc.Authority = "https://idp.acceptance.test";
            config.Identity.Oidc.ClientId = "portal-acceptance";
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IOidcAuthenticationService>();
            services.AddSingleton<IOidcAuthenticationService>(StubOidc);
            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => orchestrator.Server.CreateHandler());
        }
    }

    private sealed class HostedMutableStubOidc : IOidcAuthenticationService
    {
        public bool Enabled => true;
        public OidcIdentity Identity { get; set; } = new("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);

        public Task<OidcAuthorizationRequest> BuildAuthorizationRequestAsync(string redirectUri, CancellationToken ct = default) =>
            Task.FromResult(new OidcAuthorizationRequest(
                "https://idp.acceptance.test/authorize?state=acceptance-oidc-state",
                "acceptance-oidc-state",
                "acceptance-nonce",
                "acceptance-verifier"));

        public Task<OidcIdentity> CompleteAsync(
            string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct = default) =>
            Task.FromResult(Identity);
    }

    private sealed class HostedLoopbackFaultProxy : IAsyncDisposable
    {
        private readonly string _upstreamOrigin;
        private readonly HostedLiveKestrelPortalFactory _portal;
        private readonly HttpListener _proxyListener = new();
        private readonly HttpClient _upstream = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        private readonly CancellationTokenSource _cts = new();

        private volatile bool _dropNext202;
        private volatile bool _revokeAliceSessionAfterDrop;
        private int _delayNextRunSubmitMs;

        public string ProxyBaseUrl { get; }

        public HostedLoopbackFaultProxy(string upstreamOrigin, HostedLiveKestrelPortalFactory portal)
        {
            _upstreamOrigin = upstreamOrigin.TrimEnd('/');
            _portal = portal;
            ProxyBaseUrl = $"http://127.0.0.1:{GetFreeLoopbackPort()}";
            _proxyListener.Prefixes.Add(ProxyBaseUrl + "/");
        }

        public void ArmDropNext202(bool revokeAliceSessionAfterDrop)
        {
            _dropNext202 = true;
            _revokeAliceSessionAfterDrop = revokeAliceSessionAfterDrop;
        }

        public void ArmDelayNextRunSubmit(TimeSpan delay)
        {
            Interlocked.Exchange(ref _delayNextRunSubmitMs, (int)delay.TotalMilliseconds);
        }

        public void Start()
        {
            _proxyListener.Start();
            _ = Task.Run(ServeLoopAsync);
        }

        private async Task ServeLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _proxyListener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => HandleProxyAsync(ctx));
            }
        }

        private async Task HandleProxyAsync(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod;
            try
            {
                if (method == "POST" && path.EndsWith("/runs", StringComparison.OrdinalIgnoreCase))
                {
                    var delayMs = Interlocked.Exchange(ref _delayNextRunSubmitMs, 0);
                    if (delayMs > 0)
                        await Task.Delay(delayMs, _cts.Token);
                }

                var targetUrl = _upstreamOrigin + ctx.Request.RawUrl;
                using var req = new HttpRequestMessage(new HttpMethod(method), targetUrl);
                if (ctx.Request.HasEntityBody)
                {
                    using var ms = new MemoryStream();
                    await ctx.Request.InputStream.CopyToAsync(ms);
                    req.Content = new ByteArrayContent(ms.ToArray());
                    if (!string.IsNullOrEmpty(ctx.Request.ContentType))
                        req.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
                }

                foreach (string? name in ctx.Request.Headers.AllKeys)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var value = ctx.Request.Headers[name]!;
                    if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)
                        && value.Equals(ProxyBaseUrl, StringComparison.OrdinalIgnoreCase))
                        value = _upstreamOrigin;
                    req.Headers.TryAddWithoutValidation(name, value);
                }

                using var resp = await _upstream.SendAsync(req);

                if (_dropNext202
                    && method == "POST"
                    && path.EndsWith("/runs", StringComparison.OrdinalIgnoreCase)
                    && resp.StatusCode == HttpStatusCode.Accepted)
                {
                    _dropNext202 = false;
                    if (_revokeAliceSessionAfterDrop)
                    {
                        _revokeAliceSessionAfterDrop = false;
                        await RevokeAliceNativeSessionsAsync();
                    }
                    ctx.Response.Abort();
                    return;
                }

                ctx.Response.StatusCode = (int)resp.StatusCode;
                foreach (var header in resp.Headers)
                {
                    if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var val in header.Value)
                        ctx.Response.Headers.Add(header.Key, val);
                }
                foreach (var header in resp.Content.Headers)
                {
                    if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        ctx.Response.ContentType = string.Join(", ", header.Value);
                    else
                        foreach (var val in header.Value)
                            ctx.Response.Headers.Add(header.Key, val);
                }

                var bodyBytes = await resp.Content.ReadAsByteArrayAsync();
                ctx.Response.ContentLength64 = bodyBytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bodyBytes);
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private async Task RevokeAliceNativeSessionsAsync()
        {
            using var scope = _portal.BrowserServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var alice = await db.Users.SingleAsync(u => u.UserName == "alice");
            var now = DateTime.UtcNow;
            await db.NativeClientSessions
                .Where(s => s.UserId == alice.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, "st58_revoked_during_recovery"));
            await db.NativeClientRefreshTokens
                .Where(t => t.UserId == alice.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now));
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _proxyListener.Stop(); } catch { }
            try { _proxyListener.Close(); } catch { }
            _upstream.Dispose();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
