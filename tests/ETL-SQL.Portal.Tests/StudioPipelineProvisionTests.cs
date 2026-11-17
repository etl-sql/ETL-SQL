using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Unifies Studio catalog pipelines (<c>PIPELINE:{id}</c>) with Orchestrator run slots:
/// catalog enrichment, one-click provisioning + author EXECUTE grants, and rejection of
/// diverging inline <c>Jobs.Script</c> edits.
/// </summary>
public sealed class StudioPipelineProvisionTests(StudioPipelineRunFixture fixture)
    : IClassFixture<StudioPipelineRunFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Script = "SELECT 1 AS Value INTO #rows;\nASSERT (SELECT COUNT(*) FROM #rows) = 1;\n";

    [Fact]
    public async Task GetPipelineCatalog_ReturnsPipelinesOnlyWithFolderAndProvisionStatus()
    {
        var folderId = await CreateFolderAsync(fixture.AuthorId, "FinanceOps");
        var (pipelineId, _, pipelineName) = await CreateDocumentAsync(fixture.AuthorToken, folderId, "Pipeline", Script);
        var (reportId, _, _) = await CreateDocumentAsync(fixture.AdminToken, folderId, "Report", "SELECT 1 AS Value;\n");

        // An ordinary Studio author without OrchestratorAccess cannot list Orchestrator catalog slots.
        using (var authorCatalog = await Send(HttpMethod.Get, "/api/orchestrator/pipelines/catalog", fixture.AuthorToken))
            Assert.Equal(HttpStatusCode.Forbidden, authorCatalog.StatusCode);

        using var resp = await Send(HttpMethod.Get, "/api/orchestrator/pipelines/catalog", fixture.AdminToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var entries = (await resp.Content.ReadFromJsonAsync<JsonArray>(Json))!;

        var entry = entries
            .Select(node => node!.AsObject())
            .Single(item => item["pipelineId"]!.GetValue<int>() == pipelineId);
        Assert.Equal($"PIPELINE:{pipelineId}", entry["jobName"]!.GetValue<string>());
        Assert.Equal(pipelineName, entry["name"]!.GetValue<string>());
        Assert.StartsWith("/FinanceOps-", entry["folderPath"]!.GetValue<string>());
        Assert.Equal(fixture.AuthorPrincipalKey, entry["ownerPrincipalKey"]!.GetValue<string>());
        Assert.Equal("pipeline_author", entry["ownerDisplayName"]!.GetValue<string>());
        Assert.False(entry["isProvisioned"]!.GetValue<bool>());
        Assert.DoesNotContain(entries.Select(n => n!.AsObject()), item => item["pipelineId"]!.GetValue<int>() == reportId);
    }

    [Fact]
    public async Task ProvisionPipelineSlot_CreatesManualSlotGrantsOwnerExecuteAndIsIdempotent()
    {
        var folderId = await CreateFolderAsync(fixture.AuthorId, "Ingest");
        var (id, version, name) = await CreateDocumentAsync(fixture.AuthorToken, folderId, "Pipeline", Script);
        var jobName = $"PIPELINE:{id}";

        // Before provisioning, an ordinary author hits the expected 403 job_not_provisioned refusal,
        // and cannot self-provision.
        using (var unprovisioned = await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs", fixture.AuthorToken, new { version }))
            Assert.Equal(HttpStatusCode.Forbidden, unprovisioned.StatusCode);
        using (var forbidden = await Send(HttpMethod.Post, $"/api/orchestrator/pipelines/{id}/provision", fixture.AuthorToken))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // One-click provision creates the manual slot without starting a run and grants owner EXECUTE.
        using (var provisioned = await Send(HttpMethod.Post, $"/api/orchestrator/pipelines/{id}/provision", fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, provisioned.StatusCode);
            var body = (await provisioned.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.Equal(id, body["pipelineId"]!.GetValue<int>());
            Assert.Equal(jobName, body["jobName"]!.GetValue<string>());
            Assert.True(body["slotCreated"]!.GetValue<bool>());
            Assert.True(body["executeGrantApplied"]!.GetValue<bool>());
            Assert.Equal(fixture.AuthorPrincipalKey, body["grantedPrincipalKey"]!.GetValue<string>());
        }

        using (var scope = fixture.Orchestrator.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobHistoryStore>();
            var job = await store.GetJobAsync("portal-host", jobName);
            Assert.NotNull(job);
            Assert.Equal(SchedulerService.ManualOnlyNextRun, job.NextRun);
            Assert.Equal(name, job.DisplayName);
            Assert.Empty(await store.GetHistoryAsync(job.Id, 10));
        }

        // Catalog now shows isProvisioned: true.
        using (var catalog = await Send(HttpMethod.Get, "/api/orchestrator/pipelines/catalog", fixture.AdminToken))
        {
            var entries = (await catalog.Content.ReadFromJsonAsync<JsonArray>(Json))!;
            var entry = entries.Select(n => n!.AsObject()).Single(item => item["pipelineId"]!.GetValue<int>() == id);
            Assert.True(entry["isProvisioned"]!.GetValue<bool>());
        }

        // Author can now submit and run the saved revision without waiting on an admin run.
        using (var submitted = await Send(HttpMethod.Post, $"/api/studio/pipelines/{id}/runs", fixture.AuthorToken, new { version }))
        {
            Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
            var runId = (await submitted.Content.ReadFromJsonAsync<JsonObject>(Json))!["runId"]!.GetValue<long>();
            var terminal = await FollowAsync(id, runId, fixture.AuthorToken);
            Assert.Equal("SUCCESS", terminal["status"]!.GetValue<string>());
        }

        // Upgrade author grant to MANAGE and verify re-provisioning is idempotent without downgrading MANAGE.
        using (var manage = await Send(
            HttpMethod.Put,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString(jobName)}/USER/{fixture.AuthorPrincipalKey}",
            fixture.AdminToken,
            new { permission = "MANAGE" }))
            Assert.Equal(HttpStatusCode.OK, manage.StatusCode);

        using (var secondProvision = await Send(HttpMethod.Post, $"/api/orchestrator/pipelines/{id}/provision", fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, secondProvision.StatusCode);
            var body = (await secondProvision.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.False(body["slotCreated"]!.GetValue<bool>());
            Assert.False(body["executeGrantApplied"]!.GetValue<bool>());
        }

        using (var grants = await Send(
            HttpMethod.Get,
            $"/api/orchestrator/authorization/JOB/{Uri.EscapeDataString(jobName)}",
            fixture.AdminToken))
        {
            Assert.Equal(HttpStatusCode.OK, grants.StatusCode);
            var list = (await grants.Content.ReadFromJsonAsync<JsonArray>(Json))!;
            var authorGrant = list.Select(n => n!.AsObject())
                .Single(g => g["principalId"]!.GetValue<string>() == fixture.AuthorPrincipalKey);
            Assert.Equal("MANAGE", authorGrant["permission"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task UpdateJob_RejectsInlineScriptEditsOnPipelineSlot_WhileAllowingEnabledToggle()
    {
        var folderId = await CreateFolderAsync(fixture.AuthorId, "Curated");
        var (id, _, _) = await CreateDocumentAsync(fixture.AuthorToken, folderId, "Pipeline", Script);
        var jobName = $"PIPELINE:{id}";

        using (var provisioned = await Send(HttpMethod.Post, $"/api/orchestrator/pipelines/{id}/provision", fixture.AdminToken))
            Assert.Equal(HttpStatusCode.OK, provisioned.StatusCode);

        long version;
        using (var scope = fixture.Orchestrator.Services.CreateScope())
        {
            var job = await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>()
                .GetJobAsync("portal-host", jobName);
            Assert.NotNull(job);
            version = job.Version;
        }

        // Direct Jobs.Script edits must be refused so Studio catalog and Orchestrator never diverge.
        using (var editReq = Request(HttpMethod.Put, $"/api/orchestrator/jobs/{Uri.EscapeDataString(jobName)}",
            fixture.AdminToken, new { scriptText = "SELECT 999;" }))
        {
            editReq.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var conflict = await fixture.Client.SendAsync(editReq);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            var refusal = (await conflict.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            Assert.Equal("catalog_managed_script", refusal["code"]!.GetValue<string>());
        }

        // Enabling/disabling the slot without ScriptText continues to succeed.
        using (var toggleReq = Request(HttpMethod.Put, $"/api/orchestrator/jobs/{Uri.EscapeDataString(jobName)}",
            fixture.AdminToken, new { isEnabled = false }))
        {
            toggleReq.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var toggled = await fixture.Client.SendAsync(toggleReq);
            Assert.Equal(HttpStatusCode.NoContent, toggled.StatusCode);
        }

        using (var scope = fixture.Orchestrator.Services.CreateScope())
        {
            var job = await scope.ServiceProvider.GetRequiredService<IJobHistoryStore>()
                .GetJobAsync("portal-host", jobName);
            Assert.NotNull(job);
            Assert.Equal(Script, job.Script);
            Assert.False(job.IsEnabled);
        }
    }

    private async Task<JsonObject> FollowAsync(int id, long runId, string token)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            using var response = await Send(HttpMethod.Get, $"/api/studio/pipelines/{id}/runs/{runId}", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var run = (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
            if (run["isTerminal"]!.GetValue<bool>()) return run;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run {runId} did not reach terminal state.");
    }

    private async Task<int> CreateFolderAsync(int ownerId, string prefix)
    {
        using var scope = fixture.Portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder { Name = $"{prefix}-{suffix}", Path = $"/{prefix}-{suffix}", OwnerId = ownerId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private async Task<(int Id, long Version, string Name)> CreateDocumentAsync(
        string token, int folderId, string kind, string script)
    {
        var name = $"{kind}-{Guid.NewGuid():N}"[..24];
        using var created = await Send(HttpMethod.Post, "/api/studio/reports", token,
            new { folderId, name, scriptText = script, kind });
        Assert.True(created.IsSuccessStatusCode, $"create: {(int)created.StatusCode} {await created.Content.ReadAsStringAsync()}");
        var body = (await created.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        return (body["id"]!.GetValue<int>(), body["version"]!.GetValue<long>(), name);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
    {
        using var req = Request(method, path, token, body);
        return await fixture.Client.SendAsync(req);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }
}
