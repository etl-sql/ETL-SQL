using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class StudioPipelineRunIdempotencyTests
{
    private const string ScriptV1 =
        "CREATE CONNECTION sample_data AS MOCKDB();\nSELECT UserID INTO #users FROM sample_data.Users;\n"
        + "ASSERT (SELECT COUNT(*) FROM #users) > 0;\n";

    private const string ScriptV2 =
        ScriptV1 + "ASSERT (SELECT COUNT(*) FROM #users) >= 1;\n";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ConcurrentRequestsWithSameKeyAndRevision_DispatchAtMostOnce_AndReplayOriginalReceipt()
    {
        var handler = new StubOrchestratorHandler
        {
            Delay = TimeSpan.FromMilliseconds(120)
        };
        using var portal = new IdempotencyPortalFactory(handler);
        await MarkAdminReadyAsync(portal);
        using var client = portal.CreateClient();
        var adminToken = await LoginAsync(client, "admin", "Admin@12345!");

        var folderId = await CreateFolderAsync(portal, ownerId: null);
        var (pipelineId, version) = await CreatePipelineAsync(client, adminToken, folderId, ScriptV1);
        var key = Guid.NewGuid().ToString("D");

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => SubmitAsync(client, pipelineId, version, adminToken, key))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        Assert.Equal(1, handler.DispatchCount);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));

        var payloads = new List<PipelineRunSubmittedDto>();
        foreach (var response in responses)
        {
            var dto = await response.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json);
            Assert.NotNull(dto);
            payloads.Add(dto!);
        }

        var expectedRunId = payloads[0].RunId;
        Assert.True(expectedRunId > 0);
        Assert.All(payloads, dto =>
        {
            Assert.Equal(expectedRunId, dto.RunId);
            Assert.Equal(pipelineId, dto.PipelineId);
            Assert.Equal(version, dto.Version);
            Assert.Equal(Sha(ScriptV1), dto.ScriptHash);
            Assert.Equal($"PIPELINE:{pipelineId}", dto.JobName);
        });
        Assert.Equal(1, payloads.Count(dto => !dto.Replayed));
        Assert.Equal(4, payloads.Count(dto => dto.Replayed));
        Assert.Equal(0, portal.Services.GetRequiredService<StudioRunIdempotencyCoordinator>().ActiveGateCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.Conflict)]
    public Task TwoPortalInstances_PendingLookupDoesNotFinalizeAnotherNodesClaim(HttpStatusCode outcome) =>
        CheckTwoPortalInstancesAsync(outcome);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostgresPortals_ShareTheDurableClaimAndReceipt()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await CheckTwoPortalInstancesAsync(HttpStatusCode.Accepted, postgres.GetConnectionString());
    }

    private static async Task CheckTwoPortalInstancesAsync(HttpStatusCode outcome, string? postgres = null)
    {
        var sharedTempDir = Path.Combine(Path.GetTempPath(), $"portal_idemp_ha_{Guid.NewGuid():N}");
        var dispatchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatchOutcome = new TaskCompletionSource<HttpStatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHandler = new StubOrchestratorHandler { DispatchEntered = dispatchEntered, DispatchOutcome = dispatchOutcome.Task };
        var secondHandler = new StubOrchestratorHandler();
        try
        {
            using var nodeA = new IdempotencyPortalFactory(firstHandler, sharedTempDir, postgres);
            await MarkAdminReadyAsync(nodeA);
            using var clientA = nodeA.CreateClient();
            var token = await LoginAsync(clientA, "admin", "Admin@12345!");
            var folderId = await CreateFolderAsync(nodeA, ownerId: null);
            var (pipelineId, version) = await CreatePipelineAsync(clientA, token, folderId, ScriptV1);

            using var nodeB = new IdempotencyPortalFactory(secondHandler, sharedTempDir, postgres);
            using var clientB = nodeB.CreateClient();
            var key = Guid.NewGuid().ToString("D");
            var submit = SubmitAsync(clientA, pipelineId, version, token, key);
            try
            {
                await dispatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var recovery = await GetByRequestAsync(clientB, pipelineId, key, token);
                var repeat = await SubmitAsync(clientB, pipelineId, version, token, key);
                Assert.Equal(HttpStatusCode.Conflict, recovery.StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
                Assert.Equal("submission_outcome_unknown",
                    (await repeat.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
                Assert.Equal(0, secondHandler.DispatchCount);
                using var scope = nodeB.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                Assert.Equal("Pending", (await db.StudioRunIdempotencies.SingleAsync()).State);
            }
            finally
            {
                dispatchOutcome.TrySetResult(outcome);
            }

            Assert.Equal(outcome, (await submit).StatusCode);
            var recovered = await GetByRequestAsync(clientB, pipelineId, key, token);
            if (outcome == HttpStatusCode.Accepted)
            {
                Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
                Assert.True((await recovered.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!.Replayed);
            }
            else
            {
                Assert.Equal(HttpStatusCode.NotFound, recovered.StatusCode);
                Assert.Equal(HttpStatusCode.Accepted, (await SubmitAsync(clientB, pipelineId, version, token, key)).StatusCode);
                Assert.Equal(1, secondHandler.DispatchCount);
            }
            Assert.Equal(1, firstHandler.DispatchCount);
        }
        finally
        {
            try { Directory.Delete(sharedTempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ScriptBytesMustMatchTheSavedVersionBeforeDispatch()
    {
        var handler = new StubOrchestratorHandler();
        using var portal = new IdempotencyPortalFactory(handler);
        await MarkAdminReadyAsync(portal);
        using var client = portal.CreateClient();
        var token = await LoginAsync(client, "admin", "Admin@12345!");
        var folderId = await CreateFolderAsync(portal, ownerId: null);
        var (pipelineId, version) = await CreatePipelineAsync(client, token, folderId, ScriptV1);
        using var scope = portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var pipeline = await db.Reports.SingleAsync(r => r.Id == pipelineId);
        pipeline.PublishedScriptHash = Sha(ScriptV2);
        await db.SaveChangesAsync();

        var refusal = await SubmitAsync(client, pipelineId, version, token, Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.Equal("revision_changed", (await refusal.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
        Assert.Equal(0, handler.DispatchCount);
        Assert.Empty(await db.StudioRunIdempotencies.ToListAsync());
    }

    [Fact]
    public async Task OrchestratorReceiptMustMatchTheDispatchedVersion()
    {
        var handler = new StubOrchestratorHandler { MismatchReceipt = true };
        using var portal = new IdempotencyPortalFactory(handler);
        await MarkAdminReadyAsync(portal);
        using var client = portal.CreateClient();
        var token = await LoginAsync(client, "admin", "Admin@12345!");
        var folderId = await CreateFolderAsync(portal, ownerId: null);
        var (pipelineId, version) = await CreatePipelineAsync(client, token, folderId, ScriptV1);
        var key = Guid.NewGuid().ToString("D");
        var refusal = await SubmitAsync(client, pipelineId, version, token, key);
        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.Equal("submission_outcome_unknown", (await refusal.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await GetByRequestAsync(client, pipelineId, key, token)).StatusCode);
        Assert.Equal(1, handler.DispatchCount);
    }

    [Fact]
    public async Task AcceptedReplayAndRecovery_SurvivePortalRestart_AndNewerSavedRevision()
    {
        var sharedTempDir = Path.Combine(Path.GetTempPath(), $"portal_idemp_restart_{Guid.NewGuid():N}");
        var handler = new StubOrchestratorHandler();
        int pipelineId;
        long version1;
        long version2;
        long originalRunId;
        var key = Guid.NewGuid().ToString("D");

        try
        {
            using (var portal1 = new IdempotencyPortalFactory(handler, sharedTempDir))
            {
                await MarkAdminReadyAsync(portal1);
                using var client1 = portal1.CreateClient();
                var adminToken1 = await LoginAsync(client1, "admin", "Admin@12345!");
                var folderId = await CreateFolderAsync(portal1, ownerId: null);
                (pipelineId, version1) = await CreatePipelineAsync(client1, adminToken1, folderId, ScriptV1);

                var submit = await SubmitAsync(client1, pipelineId, version1, adminToken1, key);
                Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
                var submitted = (await submit.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
                Assert.False(submitted.Replayed);
                originalRunId = submitted.RunId;
                Assert.Equal(1, handler.DispatchCount);

                // Save a newer revision (version 2) before simulating a Portal restart.
                version2 = await SaveRevisionAsync(client1, pipelineId, version1, ScriptV2, adminToken1);
                Assert.Equal(version1 + 1, version2);
            }

            // Boot a fresh Portal instance against the same durable database & scripts.
            using (var portal2 = new IdempotencyPortalFactory(handler, sharedTempDir))
            {
                using var client2 = portal2.CreateClient();
                var adminToken2 = await LoginAsync(client2, "admin", "Admin@12345!");

                // Recovery by request key finds the original version 1 run even though version 2 is saved.
                var recoveredRes = await GetByRequestAsync(client2, pipelineId, key, adminToken2);
                Assert.Equal(HttpStatusCode.OK, recoveredRes.StatusCode);
                var recovered = (await recoveredRes.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
                Assert.Equal(originalRunId, recovered.RunId);
                Assert.Equal(pipelineId, recovered.PipelineId);
                Assert.Equal(version1, recovered.Version);
                Assert.Equal(Sha(ScriptV1), recovered.ScriptHash);
                Assert.Equal($"PIPELINE:{pipelineId}", recovered.JobName);
                Assert.True(recovered.Replayed);

                // Replaying POST with the same key and original version returns 202 with replayed: true and no redispatch.
                var replayRes = await SubmitAsync(client2, pipelineId, version1, adminToken2, key);
                Assert.Equal(HttpStatusCode.Accepted, replayRes.StatusCode);
                var replayed = (await replayRes.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
                Assert.Equal(originalRunId, replayed.RunId);
                Assert.Equal(version1, replayed.Version);
                Assert.True(replayed.Replayed);
                Assert.Equal(1, handler.DispatchCount);

                // Reusing the same key with a different revision (version 2) returns 409 conflict without dispatching.
                var conflictRes = await SubmitAsync(client2, pipelineId, version2, adminToken2, key);
                Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
                var conflict = (await conflictRes.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!;
                Assert.Equal("idempotency_key_conflict", conflict.Code);
                Assert.Equal(version1, conflict.CurrentVersion);
                Assert.Equal(1, handler.DispatchCount);
            }
        }
        finally
        {
            try { Directory.Delete(sharedTempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AmbiguousDispatchAndRestartedPendingClaim_Return409SubmissionOutcomeUnknown_AndNeverRedispatch()
    {
        var handler = new StubOrchestratorHandler
        {
            ThrowOnNextDispatch = new HttpRequestException("Simulated timeout after dispatch")
        };
        using var portal = new IdempotencyPortalFactory(handler);
        await MarkAdminReadyAsync(portal);
        using var client = portal.CreateClient();
        var adminToken = await LoginAsync(client, "admin", "Admin@12345!");
        var folderId = await CreateFolderAsync(portal, ownerId: null);
        var (pipelineId, version) = await CreatePipelineAsync(client, adminToken, folderId, ScriptV1);

        // 1. Timeout / dropped response during dispatch.
        var ambiguousKey = Guid.NewGuid().ToString("D");
        var first = await SubmitAsync(client, pipelineId, version, adminToken, ambiguousKey);
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal("submission_outcome_unknown", (await first.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
        Assert.Equal(1, handler.DispatchCount);

        // Retrying the same key must NOT dispatch again and must return 409 submission_outcome_unknown.
        var retry = await SubmitAsync(client, pipelineId, version, adminToken, ambiguousKey);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal("submission_outcome_unknown", (await retry.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
        Assert.Equal(1, handler.DispatchCount);

        // Recovery lookup also reports 409 submission_outcome_unknown.
        var lookup = await GetByRequestAsync(client, pipelineId, ambiguousKey, adminToken);
        Assert.Equal(HttpStatusCode.Conflict, lookup.StatusCode);
        Assert.Equal("submission_outcome_unknown", (await lookup.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);

        // 2. Orphaned Pending row left behind by a Portal restart before Orchestrator answered.
        var crashedKey = Guid.NewGuid().ToString("D");
        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var adminId = (await db.Users.SingleAsync(u => u.UserName == "admin")).Id;
            db.StudioRunIdempotencies.Add(new StudioRunIdempotency
            {
                TenantId = "portal-host",
                UserId = adminId,
                PipelineId = pipelineId,
                IdempotencyKey = crashedKey,
                RequestedVersion = version,
                ScriptHash = Sha(ScriptV1),
                JobName = $"PIPELINE:{pipelineId}",
                State = "Pending",
                OwnerInstanceId = "previous-portal-instance",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(23)
            });
            await db.SaveChangesAsync();
        }

        var afterRestartRetry = await SubmitAsync(client, pipelineId, version, adminToken, crashedKey);
        Assert.Equal(HttpStatusCode.Conflict, afterRestartRetry.StatusCode);
        Assert.Equal("submission_outcome_unknown", (await afterRestartRetry.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
        Assert.Equal(1, handler.DispatchCount);

        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var row = await db.StudioRunIdempotencies.SingleAsync(r => r.IdempotencyKey == crashedKey);
            Assert.Equal("Pending", row.State);
            Assert.Null(row.CompletedAt);
        }

        // 3. A key with no visible submission record returns 404 submission_not_found.
        var missingKey = Guid.NewGuid().ToString("D");
        var missing = await GetByRequestAsync(client, pipelineId, missingKey, adminToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("submission_not_found", (await missing.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
    }

    [Fact]
    public async Task TenantUserPipelineIsolation_AndRevokedPermissions_BlockRecovery()
    {
        var handler = new StubOrchestratorHandler();
        using var portal = new IdempotencyPortalFactory(handler);
        await MarkAdminReadyAsync(portal);
        using var client = portal.CreateClient();
        var adminToken = await LoginAsync(client, "admin", "Admin@12345!");

        var userAId = await CreateUserAsync(portal, "alice_idemp", "Publisher");
        var userBId = await CreateUserAsync(portal, "bob_idemp", "Publisher");
        var groupId = await CreateGroupWithMembersAsync(portal, "PipelineRunners", userAId, userBId);

        var folderId = await CreateFolderAsync(portal, ownerId: null);
        await GrantFolderExecuteAsync(portal, folderId, groupId);

        var (pipeline1Id, version1) = await CreatePipelineAsync(client, adminToken, folderId, ScriptV1);
        var (pipeline2Id, _) = await CreatePipelineAsync(client, adminToken, folderId, ScriptV1);

        var tokenA = await LoginAsync(client, "alice_idemp", "Idemp@Pass99!");
        var tokenB = await LoginAsync(client, "bob_idemp", "Idemp@Pass99!");

        var keyA = Guid.NewGuid().ToString("D");
        var submitA = await SubmitAsync(client, pipeline1Id, version1, tokenA, keyA);
        Assert.Equal(HttpStatusCode.Accepted, submitA.StatusCode);

        // User A can recover on pipeline 1.
        Assert.Equal(HttpStatusCode.OK, (await GetByRequestAsync(client, pipeline1Id, keyA, tokenA)).StatusCode);

        // Pipeline isolation: User A cannot see keyA under pipeline 2.
        Assert.Equal(HttpStatusCode.NotFound, (await GetByRequestAsync(client, pipeline2Id, keyA, tokenA)).StatusCode);

        // User isolation: User B (with Execute on pipeline 1) cannot see User A's keyA.
        Assert.Equal(HttpStatusCode.NotFound, (await GetByRequestAsync(client, pipeline1Id, keyA, tokenB)).StatusCode);

        // Tenant isolation: a row stored under another tenant is invisible on portal-host.
        var foreignTenantKey = Guid.NewGuid().ToString("D");
        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.StudioRunIdempotencies.Add(new StudioRunIdempotency
            {
                TenantId = "tenant-other",
                UserId = userAId,
                PipelineId = pipeline1Id,
                IdempotencyKey = foreignTenantKey,
                RequestedVersion = version1,
                ScriptHash = Sha(ScriptV1),
                JobName = $"PIPELINE:{pipeline1Id}",
                State = "Accepted",
                RunId = 9999,
                OwnerInstanceId = "other",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await GetByRequestAsync(client, pipeline1Id, foreignTenantKey, tokenA)).StatusCode);

        // Revoking User A's folder Execute permission blocks recovery immediately.
        using (var scope = portal.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var acls = await db.FolderAcls.Where(a => a.FolderId == folderId && a.GroupId == groupId).ToListAsync();
            db.FolderAcls.RemoveRange(acls);
            await db.SaveChangesAsync();
        }
        var revoked = await GetByRequestAsync(client, pipeline1Id, keyA, tokenA);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal("document_execute_denied", (await revoked.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
    }

    [Fact]
    public async Task InvalidKeysAreRejected_AndCallersOmittingHeaderStillSubmitNormally()
    {
        var handler = new StubOrchestratorHandler();
        using var portal = new IdempotencyPortalFactory(handler);
        await MarkAdminReadyAsync(portal);
        using var client = portal.CreateClient();
        var adminToken = await LoginAsync(client, "admin", "Admin@12345!");
        var folderId = await CreateFolderAsync(portal, ownerId: null);
        var (pipelineId, version) = await CreatePipelineAsync(client, adminToken, folderId, ScriptV1);

        foreach (var badKey in new[] { "   ", "not-a-uuid", "11111111-1111-1111-8111-111111111111", "11111111-1111-4111-0111-111111111111" })
        {
            var badPost = await SubmitAsync(client, pipelineId, version, adminToken, badKey);
            Assert.Equal(HttpStatusCode.BadRequest, badPost.StatusCode);
            Assert.Equal("invalid_idempotency_key", (await badPost.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
        }

        var badGet = await GetByRequestAsync(client, pipelineId, "11111111-1111-1111-8111-111111111111", adminToken);
        Assert.Equal(HttpStatusCode.BadRequest, badGet.StatusCode);
        Assert.Equal("invalid_idempotency_key", (await badGet.Content.ReadFromJsonAsync<PipelineRunRefusalDto>(Json))!.Code);
        Assert.Equal(0, handler.DispatchCount);

        // Callers omitting Idempotency-Key still work and dispatch each submission independently.
        var first = await SubmitAsync(client, pipelineId, version, adminToken, idempotencyKey: null);
        var second = await SubmitAsync(client, pipelineId, version, adminToken, idempotencyKey: null);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        var dto1 = (await first.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
        var dto2 = (await second.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(Json))!;
        Assert.NotEqual(dto1.RunId, dto2.RunId);
        Assert.False(dto1.Replayed);
        Assert.False(dto2.Replayed);
        Assert.Equal(2, handler.DispatchCount);
    }

    private static async Task MarkAdminReadyAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var admin = await db.Users.SingleAsync(u => u.UserName == "admin");
        admin.MustChangePassword = false;
        await db.SaveChangesAsync();
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static async Task<int> CreateUserAsync(PortalWebFactory portal, string userName, params string[] roles)
    {
        using var scope = portal.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var user = new PortalUser
        {
            UserName = userName,
            Email = $"{userName}@test.local",
            IsActive = true,
            MustChangePassword = false,
            Provider = "Local"
        };
        var created = await users.CreateAsync(user, "Idemp@Pass99!");
        Assert.True(created.Succeeded);
        foreach (var role in roles)
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    private static async Task<int> CreateGroupWithMembersAsync(PortalWebFactory portal, string name, params int[] userIds)
    {
        using var scope = portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var group = new ETL_SQL.Portal.Data.Group { Name = name, Provider = "Local" };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        foreach (var userId in userIds)
            db.UserGroups.Add(new UserGroup { GroupId = group.Id, UserId = userId });
        await db.SaveChangesAsync();
        return group.Id;
    }

    private static async Task GrantFolderExecuteAsync(PortalWebFactory portal, int folderId, int groupId)
    {
        using var scope = portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.FolderAcls.Add(new FolderAcl
        {
            FolderId = folderId,
            GroupId = groupId,
            Permission = FolderPermission.Execute
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> CreateFolderAsync(PortalWebFactory portal, int? ownerId)
    {
        using var scope = portal.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var name = "Idemp_" + Guid.NewGuid().ToString("N")[..8];
        var resolvedOwnerId = ownerId ?? (await db.Users.SingleAsync(u => u.UserName == "admin")).Id;
        var folder = new Folder { Name = name, Path = "/" + name, OwnerId = resolvedOwnerId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private static async Task<(int Id, long Version)> CreatePipelineAsync(
        HttpClient client, string token, int folderId, string script)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/studio/reports")
        {
            Content = JsonContent.Create(new
            {
                folderId,
                name = "IdempPipeline_" + Guid.NewGuid().ToString("N")[..8],
                scriptText = script,
                kind = "Pipeline"
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
        return (body["id"]!.GetValue<int>(), body["version"]!.GetValue<long>());
    }

    private static async Task<long> SaveRevisionAsync(
        HttpClient client, int pipelineId, long currentVersion, string script, string token)
    {
        using (var leaseReq = new HttpRequestMessage(HttpMethod.Post, "/api/designer/lease")
        {
            Content = JsonContent.Create(new { reportId = pipelineId, force = false })
        })
        {
            leaseReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var leaseRes = await client.SendAsync(leaseReq);
            Assert.True(leaseRes.IsSuccessStatusCode);
        }

        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/designer/save")
        {
            Content = JsonContent.Create(new
            {
                reportId = pipelineId,
                scriptText = script
            })
        })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{currentVersion}\"");
            var response = await client.SendAsync(req);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }

        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/reports/{pipelineId}/script-content");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var contentRes = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, contentRes.StatusCode);
        return (await contentRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["version"]!.GetValue<long>();
    }

    private static Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, int pipelineId, long version, string token, string? idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/studio/pipelines/{pipelineId}/runs")
        {
            Content = JsonContent.Create(new { version })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null)
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> GetByRequestAsync(
        HttpClient client, int pipelineId, string key, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/studio/pipelines/{pipelineId}/runs/by-request/{Uri.EscapeDataString(key)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(req);
    }

    private static string Sha(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private sealed class StubOrchestratorHandler : HttpMessageHandler
    {
        private long _nextRunId = 100;
        private int _dispatchCount;

        public int DispatchCount => Volatile.Read(ref _dispatchCount);
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public Exception? ThrowOnNextDispatch { get; set; }
        public TaskCompletionSource? DispatchEntered { get; init; }
        public Task<HttpStatusCode>? DispatchOutcome { get; init; }
        public bool MismatchReceipt { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/runs", StringComparison.Ordinal) == true)
            {
                Interlocked.Increment(ref _dispatchCount);
                DispatchEntered?.TrySetResult();
                if (DispatchOutcome is not null)
                {
                    var status = await DispatchOutcome.WaitAsync(cancellationToken);
                    if (status != HttpStatusCode.Accepted)
                    {
                        return new HttpResponseMessage(status)
                        {
                            Content = JsonContent.Create(new { code = "already_running" })
                        };
                    }
                }
                if (Delay > TimeSpan.Zero)
                    await Task.Delay(Delay, cancellationToken);

                var ex = ThrowOnNextDispatch;
                if (ex is not null)
                {
                    ThrowOnNextDispatch = null;
                    throw ex;
                }

                var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                var scriptHash = Sha(payload.GetProperty("scriptText").GetString()!);
                var segments = request.RequestUri!.AbsolutePath.Split('/');
                var jobName = Uri.UnescapeDataString(segments[^2]);
                var runId = Interlocked.Increment(ref _nextRunId);
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = JsonContent.Create(new { runId, jobName, scriptHash = MismatchReceipt ? Sha(ScriptV2) : scriptHash })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "OK" })
            };
        }
    }

    private sealed class IdempotencyPortalFactory : PortalWebFactory
    {
        private readonly StubOrchestratorHandler _handler;
        private readonly string? _sharedTempDir;

        private readonly string? _postgres;

        public IdempotencyPortalFactory(StubOrchestratorHandler handler, string? sharedTempDir = null, string? postgres = null)
        {
            _handler = handler;
            _sharedTempDir = sharedTempDir;
            _postgres = postgres;
            if (_sharedTempDir is not null)
            {
                Directory.CreateDirectory(_sharedTempDir);
                Directory.CreateDirectory(Path.Combine(_sharedTempDir, "scripts"));
                Directory.CreateDirectory(Path.Combine(_sharedTempDir, "snapshots"));
                Directory.CreateDirectory(Path.Combine(_sharedTempDir, "maps"));
                Directory.CreateDirectory(Path.Combine(_sharedTempDir, "datasets"));
                Directory.CreateDirectory(Path.Combine(_sharedTempDir, "keys"));
                Directory.CreateDirectory(Path.Combine(_sharedTempDir, "security"));
            }
        }

        protected override void CustomizeConfiguration(Dictionary<string, string?> settings)
        {
            if (_sharedTempDir is not null)
            {
                settings["Portal:DatabasePath"] = Path.Combine(_sharedTempDir, "portal.db");
                settings["Portal:ScriptRootPath"] = Path.Combine(_sharedTempDir, "scripts");
                settings["Portal:SnapshotDirectory"] = Path.Combine(_sharedTempDir, "snapshots");
                settings["Portal:MapRootPath"] = Path.Combine(_sharedTempDir, "maps");
                settings["Portal:DatasetRootPath"] = Path.Combine(_sharedTempDir, "datasets");
                settings["Portal:Storage:KeyRingPath"] = Path.Combine(_sharedTempDir, "keys");
                settings["Portal:Orchestrator:DatabasePath"] = Path.Combine(_sharedTempDir, "etlsql.db");
            }
            if (_postgres is not null)
            {
                settings["Portal:Database:Provider"] = "Postgres";
                settings["Portal:Database:ConnectionString"] = _postgres;
            }
        }

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            if (_sharedTempDir is not null)
            {
                config.DatabasePath = Path.Combine(_sharedTempDir, "portal.db");
                config.ScriptRootPath = Path.Combine(_sharedTempDir, "scripts");
                config.SnapshotDirectory = Path.Combine(_sharedTempDir, "snapshots");
                config.MapRootPath = Path.Combine(_sharedTempDir, "maps");
                config.DatasetRootPath = Path.Combine(_sharedTempDir, "datasets");
                config.Storage.KeyRingPath = Path.Combine(_sharedTempDir, "keys");
                config.Orchestrator.DatabasePath = Path.Combine(_sharedTempDir, "etlsql.db");
            }
            if (_postgres is not null)
                config.Database = new PortalDatabaseConfig { Provider = "Postgres", ConnectionString = _postgres };
            config.Orchestrator.ApiUrl = "http://orchestrator.test";
            config.Orchestrator.ApiKey = "test-orch-key-12345";
            config.Orchestrator.IdentitySigningSecret = OrchestratorWebFactory.IdentitySecret;
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            if (_sharedTempDir is not null)
            {
                var dbPath = Path.Combine(_sharedTempDir, "portal.db");
                services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_sharedTempDir, "keys")))
                    .SetApplicationName("ETL-SQL.Portal");
                services.RemoveAll<DbContextOptions<PortalDbContext>>();
                services.RemoveAll<PortalDbContext>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<PortalDbContext>>();
                services.AddDbContext<PortalDbContext>((sp, opt) =>
                {
                    if (_postgres is null)
                        opt.UseSqlite($"Data Source={dbPath}");
                    else
                        opt.UseNpgsql(_postgres, provider => provider.MigrationsAssembly(PortalDatabase.PostgresMigrationsAssembly));
                    opt.AddInterceptors(sp.GetRequiredService<AuditFailClosedInterceptor>());
                    opt.UsePortalEncryption(sp.GetRequiredService<PortalPiiProtector>());
                });
            }

            services.AddHttpClient<OrchestratorProxyService>()
                .ConfigurePrimaryHttpMessageHandler(() => _handler);
        }
    }
}
