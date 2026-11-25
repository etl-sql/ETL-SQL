using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Live cross-process ST56 verification: runs the ACTUAL compiled VS Code hosted client
/// (Node 22) and the REAL VS Code Electron Extension Host against a live loopback Kestrel Portal
/// and real Federated Orchestrator (ST55 §4.1).
/// </summary>
[Trait("Category", "Integration")]
public sealed class St56LiveEndToEndTests(ITestOutputHelper output)
{
    private const string FinanceCheckScript =
        "CREATE CONNECTION sample_data AS MOCKDB();\n"
        + "SELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private const string FinanceCheckV2Script =
        "CREATE CONNECTION sample_data AS MOCKDB();\n"
        + "SELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n"
        + "SELECT COUNT(*) AS TotalUsers FROM #users;\n";

    private const string SlowWaitScript = "WAITFOR DELAY '00:01:00';\n";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [St56LiveFact]
    public async Task LiveVsCodeClient_EightStepAcceptance_PassesAgainstKestrelPortalAndFederatedOrchestrator()
    {
        var repoRoot = FindRepoRoot();
        var node22 = Environment.GetEnvironmentVariable("ETLSQL_ST56_NODE") ?? "node";
        var runnerJs = Path.Combine(repoRoot, "src", "etl-sql-vscode", "out", "test", "st56LiveAcceptanceRunner.js");
        await RequireNode22Async(node22, repoRoot);
        Assert.True(File.Exists(runnerJs), $"Compiled live runner not found at {runnerJs}. Run npm run compile first.");

        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new LiveKestrelPortalFactory(orchestrator);
        portal.Start();

        var seeded = await SeedAcceptanceTopologyAsync(portal, orchestrator);

        await using var proxy = new LoopbackFaultProxy(portal.ServerAddress, portal, orchestrator);
        proxy.Start();

        var env = new Dictionary<string, string>
        {
            ["ETLSQL_LIVE_PORTAL_URL"] = proxy.ProxyBaseUrl,
            ["ETLSQL_LIVE_CONTROL_URL"] = proxy.ControlBaseUrl,
            ["ETLSQL_LIVE_FINANCE_PIPELINE_ID"] = seeded.FinancePipelineId.ToString(),
            ["ETLSQL_LIVE_SLOW_PIPELINE_ID"] = seeded.SlowPipelineId.ToString()
        };

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            node22,
            $"\"{runnerJs}\"",
            Path.Combine(repoRoot, "src", "etl-sql-vscode"),
            env,
            TimeSpan.FromMinutes(2));

        output.WriteLine(stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            output.WriteLine("STDERR:\n" + stderr);

        Assert.True(exitCode == 0, $"Live VS Code acceptance runner failed (exit {exitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.Contains("ST56_LIVE_TRACE_BEGIN", stdout);
        Assert.Contains("ST56_LIVE_TRACE_END", stdout);

        // Verify Portal audit rows from the live run
        using var scope = portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var alice = await db.Users.SingleAsync(u => u.UserName == "alice");
        Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "CLIENT_LOGIN"));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "PIPELINE_RUN_SUBMITTED"));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "PIPELINE_RUN_CANCEL_REQUESTED"));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.UserId == alice.Id && a.Action == "LOGOUT"));
    }

    [St56LiveFact(windowsOnly: true)]
    public async Task LiveElectronExtensionHost_Smoke_PassesAgainstKestrelPortalAndFederatedOrchestrator()
    {
        var repoRoot = FindRepoRoot();
        var node22 = Environment.GetEnvironmentVariable("ETLSQL_ST56_NODE") ?? "node";
        var runTestJs = Path.Combine(repoRoot, "src", "etl-sql-vscode", "out", "test", "runTest.js");
        var vscodeExe = Environment.GetEnvironmentVariable("ETLSQL_VSCODE_EXECUTABLE")
            ?? FindCachedVsCodeExecutable(Path.Combine(repoRoot, "src", "etl-sql-vscode", ".vscode-test"));
        await RequireNode22Async(node22, repoRoot);
        Assert.True(File.Exists(runTestJs), $"Compiled Electron test launcher not found at {runTestJs}");
        Assert.True(vscodeExe is not null && File.Exists(vscodeExe), "Cached VS Code Electron executable not found under .vscode-test");

        using var orchestrator = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var portal = new LiveKestrelPortalFactory(orchestrator);
        portal.Start();

        var seeded = await SeedAcceptanceTopologyAsync(portal, orchestrator, extraScheduledJobCount: 51);
        await using var proxy = new LoopbackFaultProxy(portal.ServerAddress, portal, orchestrator);
        proxy.Start();

        var tempUserData = Path.Combine(Path.GetTempPath(), $"etlsql_vscode_smoke_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempUserData);
        var userSettingsPath = Path.Combine(tempUserData, "User", "settings.json");

        try
        {
            var env = new Dictionary<string, string>
            {
                ["ETLSQL_VSCODE_EXECUTABLE"] = vscodeExe!,
                ["ETLSQL_VSCODE_USER_DATA_DIR"] = tempUserData,
                ["ETLSQL_VSCODE_TEST_GREP"] = "ST56 Hosted Execution Electron Smoke",
                ["ETLSQL_ST56_SMOKE_PORTAL_URL"] = portal.ServerAddress,
                ["ETLSQL_ST56_SMOKE_CONTROL_URL"] = proxy.ControlBaseUrl,
                ["ETLSQL_ST56_SMOKE_PIPELINE_ID"] = seeded.FinancePipelineId.ToString(),
                ["ETLSQL_ST56_SMOKE_SLOW_PIPELINE_ID"] = seeded.SlowPipelineId.ToString(),
                ["ETLSQL_ST56_SMOKE_USER_SETTINGS"] = userSettingsPath
            };

            var (exitCode, stdout, stderr) = await RunProcessAsync(
                node22,
                $"\"{runTestJs}\"",
                Path.Combine(repoRoot, "src", "etl-sql-vscode"),
                env,
                TimeSpan.FromMinutes(3));

            output.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
                output.WriteLine("STDERR:\n" + stderr);

            Assert.True(exitCode == 0, $"Electron Extension-Host smoke failed (exit {exitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            using var scope = portal.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var alice = await db.Users.SingleAsync(u => u.UserName == "alice");
            var bob = await db.Users.SingleAsync(u => u.UserName == "bob");

            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.UserId == alice.Id
                && a.Action == "PIPELINE_RUN_SUBMITTED"
                && a.ResourceId == seeded.FinancePipelineId.ToString()
                && a.Detail != null && a.Detail.Contains("version=2;")));
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.UserId == alice.Id
                && (a.Action == "HOSTED_JOB_RUN_CANCEL_REQUESTED" || a.Action == "PIPELINE_RUN_CANCEL_REQUESTED")));
            Assert.False(await db.AuditLogs.AnyAsync(a =>
                a.UserId == bob.Id
                && (a.Action == "PIPELINE_RUN_SUBMITTED"
                    || a.Action == "HOSTED_JOB_TRIGGERED"
                    || a.Action == "PIPELINE_RUN_CANCEL_REQUESTED"
                    || a.Action == "HOSTED_JOB_RUN_CANCEL_REQUESTED")));
        }
        finally
        {
            try { Directory.Delete(tempUserData, recursive: true); } catch { }
        }
    }

    private static async Task<(int FinancePipelineId, int SlowPipelineId)> SeedAcceptanceTopologyAsync(
        LiveKestrelPortalFactory portal,
        OrchestratorWebFactory orchestrator,
        int extraScheduledJobCount = 0)
    {
        int folderId;
        int restrictedFolderId;
        using (var scope = portal.Services.CreateScope())
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
            var restricted = new Folder { Name = "FinancePrivate", Path = "/FinancePrivate", OwnerId = admin.Id };
            db.Folders.AddRange(folder, restricted);
            await db.SaveChangesAsync();
            folderId = folder.Id;
            restrictedFolderId = restricted.Id;

            // DataReaders may look up runnable revisions, but do not receive Author or Orchestrator Execute.
            db.FolderAcls.AddRange(
                new FolderAcl { FolderId = folderId, GroupId = authorsGroup.Id, Permission = FolderPermission.Author },
                new FolderAcl { FolderId = folderId, GroupId = readersGroup.Id, Permission = FolderPermission.Execute });

            // Pre-provision alice (author/executor) and bob (read-only on Orchestrator jobs)
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
            var bob = new PortalUser
            {
                TenantId = "portal-host",
                UserName = "bob",
                NormalizedUserName = "BOB",
                Email = "bob@alpha.test",
                NormalizedEmail = "BOB@ALPHA.TEST",
                IsActive = true,
                MustChangePassword = false,
                Provider = "OIDC",
                ExternalSubject = "oid-bob",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            db.Users.AddRange(alice, bob);
            await db.SaveChangesAsync();
            db.UserGroups.AddRange(
                new UserGroup { UserId = alice.Id, GroupId = authorsGroup.Id },
                new UserGroup { UserId = bob.Id, GroupId = readersGroup.Id });
            await db.SaveChangesAsync();
        }

        using var http = new HttpClient { BaseAddress = new Uri(portal.ServerAddress) };
        var loginResp = await http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var adminToken = (await loginResp.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();

        var (financeId, financeVersion) = await CreatePipelineAsync(http, adminToken, folderId, "FinanceCheck.etlsql", FinanceCheckScript);
        var (slowId, slowVersion) = await CreatePipelineAsync(http, adminToken, folderId, "SlowWait.etlsql", SlowWaitScript);
        await CreatePipelineAsync(http, adminToken, restrictedFolderId, "FinancePrivate.etlsql", FinanceCheckScript);

        string alicePrincipalKey;
        string bobPrincipalKey;
        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            alicePrincipalKey = (await db.Users.SingleAsync(u => u.UserName == "alice")).PrincipalKey!;
            bobPrincipalKey = (await db.Users.SingleAsync(u => u.UserName == "bob")).PrincipalKey!;
        }

        await ProvisionAndGrantAsync(http, adminToken, financeId, financeVersion, alicePrincipalKey, cancelImmediately: false);
        await ProvisionAndGrantAsync(http, adminToken, slowId, slowVersion, alicePrincipalKey, cancelImmediately: true);
        await GrantJobPermissionAsync(http, adminToken, $"PIPELINE:{financeId}", bobPrincipalKey, "READ");
        await GrantJobPermissionAsync(http, adminToken, $"PIPELINE:{slowId}", bobPrincipalKey, "READ");

        if (extraScheduledJobCount > 0)
        {
            using var orchScope = orchestrator.Services.CreateScope();
            var historyStore = orchScope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var authStore = orchScope.ServiceProvider.GetRequiredService<IOrchestratorAuthorizationStore>();
            for (var i = 1; i <= extraScheduledJobCount; i++)
            {
                var jobName = $"BatchJob-{i:00}";
                await historyStore.SaveJobAsync(new JobDefinition(
                    Name: jobName,
                    Script: "SELECT 1;",
                    Interval: 1,
                    Unit: "Days",
                    AtTime: null,
                    LastRun: null,
                    NextRun: DateTime.UtcNow.AddHours(i),
                    IsEnabled: true,
                    DisplayName: $"Batch Job {i:00}",
                    TenantId: "portal-host",
                    CreatedBy: alicePrincipalKey));
                var objectId = await authStore.ResolveObjectIdAsync("portal-host", OrchestratorObjectKind.Job, jobName);
                Assert.NotNull(objectId);
                await authStore.SaveObjectGrantAsync(new OrchestratorObjectGrant(
                    objectId!, OrchestratorObjectKind.Job, OrchestratorPrincipalKind.User,
                    alicePrincipalKey, OrchestratorObjectPermission.Execute, "admin"));
            }
        }

        return (financeId, slowId);
    }

    private static async Task GrantJobPermissionAsync(
        HttpClient http, string token, string jobName, string principalKey, string permission)
    {
        using var grantReq = new HttpRequestMessage(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString(jobName)}/USER/{principalKey}")
        {
            Content = JsonContent.Create(new { permission })
        };
        grantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var grantResp = await http.SendAsync(grantReq);
        Assert.Equal(HttpStatusCode.OK, grantResp.StatusCode);
    }

    private static async Task<(int Id, long Version)> CreatePipelineAsync(
        HttpClient http, string token, int folderId, string name, string script)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/studio/reports")
        {
            Content = JsonContent.Create(new { folderId, name, scriptText = script, kind = "Pipeline" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        return (body["id"]!.GetValue<int>(), body["version"]!.GetValue<long>());
    }

    private static async Task ProvisionAndGrantAsync(
        HttpClient http, string token, int pipelineId, long version, string principalKey, bool cancelImmediately)
    {
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs")
        {
            Content = JsonContent.Create(new { version })
        };
        submitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        submitReq.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var submitResp = await http.SendAsync(submitReq);
        Assert.Equal(HttpStatusCode.Accepted, submitResp.StatusCode);
        var submitted = (await submitResp.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;

        if (cancelImmediately)
        {
            using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs/{submitted.RunId}/cancel");
            cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await http.SendAsync(cancelReq);
        }

        for (var i = 0; i < 60; i++)
        {
            using var pollReq = new HttpRequestMessage(HttpMethod.Get, $"/api/studio/pipelines/{pipelineId}/runs/{submitted.RunId}");
            pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var pollResp = await http.SendAsync(pollReq);
            var run = (await pollResp.Content.ReadFromJsonAsync<PipelineRunDto>(Json))!;
            if (run.IsTerminal) break;
            await Task.Delay(150);
        }

        using var grantReq = new HttpRequestMessage(HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString($"PIPELINE:{pipelineId}")}/USER/{principalKey}")
        {
            Content = JsonContent.Create(new { permission = "EXECUTE" })
        };
        grantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var grantResp = await http.SendAsync(grantReq);
        Assert.Equal(HttpStatusCode.OK, grantResp.StatusCode);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName, string arguments, string workingDirectory, Dictionary<string, string> envVars, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var (k, v) in envVars)
            psi.Environment[k] = v;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process timed out after {timeout}: {fileName} {arguments}");
        }

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task RequireNode22Async(string node, string repoRoot)
    {
        var (exitCode, stdout, _) = await RunProcessAsync(node, "--version", repoRoot, [], TimeSpan.FromSeconds(10));
        Assert.True(exitCode == 0 && stdout.Trim().StartsWith("v22.", StringComparison.Ordinal),
            "The live ST56 fixture requires Node 22 on PATH or in ETLSQL_ST56_NODE.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (ETL-SQL.slnx).");
    }

    private static string? FindCachedVsCodeExecutable(string vscodeTestDir)
    {
        if (!Directory.Exists(vscodeTestDir)) return null;
        return Directory.GetDirectories(vscodeTestDir, "vscode-win32-x64-archive-*")
            .OrderByDescending(d => d, StringComparer.Ordinal)
            .Select(d => Path.Combine(d, "Code.exe"))
            .FirstOrDefault(File.Exists);
    }

    private sealed class LiveKestrelPortalFactory(OrchestratorWebFactory orchestrator) : PortalWebFactory
    {
        public string ServerAddress { get; private set; } = string.Empty;
        public MutableStubOidc StubOidc { get; } = new();

        public void Start()
        {
            _ = orchestrator.Server;
            UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            using var client = CreateClient();
            ServerAddress = client.BaseAddress!.AbsoluteUri.TrimEnd('/');
        }

        public async Task StopKestrelAsync()
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Services.GetRequiredService<IServer>().StopAsync(stopTimeout.Token);
        }

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            settings["Portal:Identity:Oidc:Enabled"] = "true";
            settings["Portal:Identity:Oidc:Authority"] = "https://idp.acceptance.test";
            settings["Portal:Identity:Oidc:ClientId"] = "portal-acceptance";
        }

        protected override void CustomizePortalConfig(PortalConfig config)
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

    private sealed class MutableStubOidc : IOidcAuthenticationService
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

    /// <summary>
    /// Test-only loopback proxy + control endpoint for injecting dropped-202 responses and
    /// mid-recovery session revocations without adding any fault-injection code to Portal or VS Code.
    /// </summary>
    private sealed class LoopbackFaultProxy : IAsyncDisposable
    {
        private readonly string _upstreamOrigin;
        private readonly LiveKestrelPortalFactory _portal;
        private readonly OrchestratorWebFactory _orchestrator;
        private readonly HttpListener _proxyListener = new();
        private readonly HttpListener _controlListener = new();
        private readonly HttpClient _upstream = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        private readonly CancellationTokenSource _cts = new();

        private volatile bool _dropNext202;
        private volatile bool _revokeSessionAfterDrop;

        public string ProxyBaseUrl { get; }
        public string ControlBaseUrl { get; }

        public LoopbackFaultProxy(string upstreamOrigin, LiveKestrelPortalFactory portal, OrchestratorWebFactory orchestrator)
        {
            _upstreamOrigin = upstreamOrigin.TrimEnd('/');
            _portal = portal;
            _orchestrator = orchestrator;
            ProxyBaseUrl = $"http://127.0.0.1:{GetFreePort()}";
            ControlBaseUrl = $"http://127.0.0.1:{GetFreePort()}";
            _proxyListener.Prefixes.Add(ProxyBaseUrl + "/");
            _controlListener.Prefixes.Add(ControlBaseUrl + "/");
        }

        public void Start()
        {
            _proxyListener.Start();
            _controlListener.Start();
            _ = Task.Run(() => ServeLoopAsync(_proxyListener, HandleProxyAsync));
            _ = Task.Run(() => ServeLoopAsync(_controlListener, HandleControlAsync));
        }

        private async Task ServeLoopAsync(HttpListener listener, Func<HttpListenerContext, Task> handler)
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => handler(ctx));
            }
        }

        private async Task HandleProxyAsync(HttpListenerContext ctx)
        {
            try
            {
                var targetUrl = _upstreamOrigin + ctx.Request.RawUrl;
                using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.HttpMethod), targetUrl);
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
                    // Rewrite Origin header to match upstream Kestrel origin if needed
                    if (name.Equals("Origin", StringComparison.OrdinalIgnoreCase)
                        && value.Equals(ProxyBaseUrl, StringComparison.OrdinalIgnoreCase))
                        value = _upstreamOrigin;
                    req.Headers.TryAddWithoutValidation(name, value);
                }

                using var resp = await _upstream.SendAsync(req);

                if (_dropNext202
                    && ctx.Request.HttpMethod == "POST"
                    && (ctx.Request.Url?.AbsolutePath.EndsWith("/runs", StringComparison.OrdinalIgnoreCase) == true)
                    && resp.StatusCode == HttpStatusCode.Accepted)
                {
                    _dropNext202 = false;
                    if (_revokeSessionAfterDrop)
                    {
                        _revokeSessionAfterDrop = false;
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
                    {
                        var rewritten = header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase)
                            ? val.Replace(_upstreamOrigin, ProxyBaseUrl, StringComparison.OrdinalIgnoreCase)
                            : val;
                        ctx.Response.Headers.Add(header.Key, rewritten);
                    }
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

        private async Task HandleControlAsync(HttpListenerContext ctx)
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
                var payload = string.IsNullOrWhiteSpace(raw) ? new JsonObject() : JsonNode.Parse(raw)!.AsObject();
                var path = ctx.Request.Url?.AbsolutePath ?? "/";

                object result;
                if (path == "/set-oidc-user")
                {
                    var user = payload["user"]!.GetValue<string>();
                    _portal.StubOidc.Identity = user == "bob"
                        ? new OidcIdentity("oid-bob", "bob", "bob@alpha.test", ["DataReaders"])
                        : new OidcIdentity("oid-alice", "alice", "alice@alpha.test", ["DataAuthors"]);
                    result = new { ok = true, user };
                }
                else if (path == "/arm-drop")
                {
                    _dropNext202 = true;
                    _revokeSessionAfterDrop = payload["revokeSessionAfterDrop"]?.GetValue<bool>() == true;
                    result = new { ok = true };
                }
                else if (path == "/history-count")
                {
                    var pipelineId = payload["pipelineId"]!.GetValue<int>();
                    using var scope = _orchestrator.Services.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
                    var job = await store.GetJobAsync("portal-host", $"PIPELINE:{pipelineId}");
                    var count = job is null ? 0 : (await store.GetHistoryAsync(job.Id, 200)).Count();
                    result = new { count };
                }
                else if (path == "/stop-portal")
                {
                    await _portal.StopKestrelAsync();
                    result = new { ok = true };
                }
                else if (path == "/bump-finance-version")
                {
                    var pipelineId = payload["pipelineId"]!.GetValue<int>();
                    using var adminHttp = new HttpClient { BaseAddress = new Uri(_upstreamOrigin) };
                    var loginResp = await adminHttp.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
                    if (!loginResp.IsSuccessStatusCode)
                    {
                        ctx.Response.StatusCode = (int)loginResp.StatusCode;
                        ctx.Response.Close();
                        return;
                    }
                    var adminToken = (await loginResp.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
                    using var saveReq = new HttpRequestMessage(HttpMethod.Post, "/api/designer/save")
                    {
                        Content = JsonContent.Create(new { reportId = pipelineId, scriptText = FinanceCheckV2Script })
                    };
                    saveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                    saveReq.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
                    using var saveResp = await adminHttp.SendAsync(saveReq);
                    if (!saveResp.IsSuccessStatusCode)
                    {
                        ctx.Response.StatusCode = (int)saveResp.StatusCode;
                        ctx.Response.Close();
                        return;
                    }
                    var saved = (await saveResp.Content.ReadFromJsonAsync<JsonObject>(Json))!;
                    result = new { ok = true, version = saved["version"]!.GetValue<long>() };
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                var json = JsonSerializer.SerializeToUtf8Bytes(result, Json);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = json.Length;
                await ctx.Response.OutputStream.WriteAsync(json);
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { }
            }
        }

        private async Task RevokeAliceNativeSessionsAsync()
        {
            using var scope = _portal.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var alice = await db.Users.SingleAsync(u => u.UserName == "alice");
            var now = DateTime.UtcNow;
            await db.NativeClientSessions
                .Where(s => s.UserId == alice.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, "test_revoked_during_recovery"));
            await db.NativeClientRefreshTokens
                .Where(t => t.UserId == alice.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.RevokedAt, now));
        }

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _proxyListener.Stop(); _proxyListener.Close(); } catch { }
            try { _controlListener.Stop(); _controlListener.Close(); } catch { }
            _upstream.Dispose();
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class St56LiveFactAttribute : FactAttribute
{
    public St56LiveFactAttribute(bool windowsOnly = false)
    {
        if (Environment.GetEnvironmentVariable("ETLSQL_ST56_LIVE") != "1")
            Skip = "Set ETLSQL_ST56_LIVE=1 after building the extension to run the live ST56 fixtures.";
        else if (windowsOnly && !OperatingSystem.IsWindows())
            Skip = "The Electron fixture currently uses a Windows VS Code test executable.";
    }
}
