using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Hosted;
using ETL_SQL.WorkstationEditor;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.WorkstationEditor;

public sealed class WorkstationHostedTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task HostedStatus_DefaultsToLocalOffline_EvenWhenServersAreRegistered()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            var registry = new HostedServerRegistryStore(registryPath);
            registry.AddOrUpdate("prod", "Prod Portal", "https://portal.corp.example", "Prod");

            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath));
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };

            // Unauthenticated request must be rejected by the existing loopback token gate.
            using var unauth = await client.GetAsync("/api/hosted/status");
            Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

            // Authenticated request defaults to Local (Offline) with registered servers listed.
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");
            var status = await client.GetFromJsonAsync<HostedStatusDto>("/api/hosted/status", Json);
            Assert.NotNull(status);
            Assert.Equal("local", status!.Mode);
            Assert.Equal("local", status.TargetKey);
            Assert.Equal("Local (Offline)", status.StatusLabel);
            Assert.False(status.SignedIn);
            Assert.Null(status.Server);
            Assert.Null(status.User);
            Assert.Single(status.Servers);
            Assert.Equal("prod", status.Servers[0].Alias);

            // Verify client ID and scopes on the underlying HostedClientSessionService.
            var sessionService = app.Services.GetRequiredService<HostedClientSessionService>();
            Assert.Equal(HostedClientIds.DesktopStudio, sessionService.ClientId);
            Assert.Equal(HostedClientScopes.TuiDefault, sessionService.RequestedScope);
            Assert.DoesNotContain(HostedClientScopes.StudioSave, sessionService.RequestedScope, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HostedServers_RejectsNonLoopbackHttp_AndSupportsCrud()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath));
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");

            // Plain http:// to non-loopback host must be refused.
            using var badResp = await client.PostAsJsonAsync("/api/hosted/servers", new
            {
                alias = "bad",
                label = "Insecure",
                origin = "http://portal.corp.example"
            });
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);

            // Valid https:// origin succeeds and persists to user-level registry file.
            using var addResp = await client.PostAsJsonAsync("/api/hosted/servers", new
            {
                alias = "dev",
                label = "Dev Portal",
                origin = "https://dev.portal.example/",
                environment = "Dev"
            });
            Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
            var afterAdd = await addResp.Content.ReadFromJsonAsync<HostedStatusDto>(Json);
            Assert.NotNull(afterAdd);
            Assert.Single(afterAdd!.Servers);
            Assert.Equal("https://dev.portal.example", afterAdd.Servers[0].Origin);

            // Local offline run works even with a server registered and unreachable.
            using var localRunResp = await client.PostAsJsonAsync("/api/designer/run", new
            {
                script = "SELECT 42 AS Answer;"
            });
            Assert.Equal(HttpStatusCode.OK, localRunResp.StatusCode);
            var localRun = await localRunResp.Content.ReadFromJsonAsync<RunResponse>(Json);
            Assert.NotNull(localRun);
            Assert.True(localRun!.Success);
            Assert.Equal(1, localRun.RowCount);

            // Delete removes server from registry.
            using var delResp = await client.DeleteAsync("/api/hosted/servers/dev");
            Assert.Equal(HttpStatusCode.OK, delResp.StatusCode);
            var afterDel = await delResp.Content.ReadFromJsonAsync<HostedStatusDto>(Json);
            Assert.Empty(afterDel!.Servers);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HostedRuns_WhenSignedOut_RefusesAndNeverFallsBackToLocal()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "etl-sql-st58-unit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var registryPath = Path.Combine(tempDir, "hosted-servers.json");
        try
        {
            await using var app = WorkstationEditorApp.Create([], new WorkstationEditorOptions(
                tempDir, null, 0, false, "studio-token", StudioMode: true, HostedServerRegistryPath: registryPath));
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(WorkstationEditorApp.GetListeningUrl(app)) };
            client.DefaultRequestHeaders.Add("X-ETLSQL-EDITOR-TOKEN", "studio-token");

            using var runResp = await client.PostAsJsonAsync("/api/hosted/runs", new
            {
                pipelineId = 1,
                documentId = "doc-primary",
                documentPath = "FinanceCheck.etlsql"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, runResp.StatusCode);
            var body = await runResp.Content.ReadAsStringAsync();
            Assert.Contains("session_expired", body, StringComparison.Ordinal);

            using var recoverResp = await client.PostAsJsonAsync("/api/hosted/runs/recover", new { mode = "check" });
            Assert.Equal(HttpStatusCode.Unauthorized, recoverResp.StatusCode);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
