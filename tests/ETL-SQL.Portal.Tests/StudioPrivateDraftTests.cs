using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Private drafts keep a learner's saved, unpublished Studio work on the Portal. The learner here is
/// a Viewer who may open Studio and read scripts, and holds neither publish rights nor a grant on any
/// folder. What matters is that they can keep work at all, and that nobody else can read it.
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioPrivateDraftTests
{
    private const string Script = "SET REPORT TITLE = 'Practice';\n";

    private class LearnerFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Studio.RoleCapabilities["Viewer"] =
                [StudioCapabilities.StudioAccess, StudioCapabilities.ScriptRead, StudioCapabilities.ScriptPreview];
            Customize(config.Studio);
        }

        protected virtual void Customize(PortalStudioConfig studio) { }
    }

    private sealed class SmallLimitsFactory : LearnerFactory
    {
        protected override void Customize(PortalStudioConfig studio)
        {
            studio.PrivateDraftsPerUser = 2;
            studio.PrivateDraftRevisionsKept = 3;
            studio.PrivateDraftRetentionDays = 5;
        }
    }

    private sealed class PrivateDraftsOffFactory : LearnerFactory
    {
        protected override void Customize(PortalStudioConfig studio) => studio.PrivateDrafts = false;
    }

    private sealed class RecoveryOffFactory : LearnerFactory
    {
        protected override void Customize(PortalStudioConfig studio) => studio.DraftRecovery = false;
    }

    [Fact]
    public async Task ALearnerWithNoPublishRightsOrFolderCreatesSavesAndReopensADraft()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var learner = await CreateSignedInUserAsync(client, admin, "Viewer");

        // The learner really is one: no publish capability, no folder to write to, no catalog create.
        var session = await Json(await Send(client, HttpMethod.Get, learner, "/api/studio/session"));
        var capabilities = session["capabilities"]!.AsArray().Select(item => item!.GetValue<string>()).ToList();
        Assert.DoesNotContain("ReportPublish", capabilities);
        Assert.DoesNotContain("ScriptSave", capabilities);
        Assert.True(session["privateDrafts"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Post, learner, "/api/studio/reports",
            new { folderId = 1, name = "Nope", scriptText = Script })).StatusCode);

        var created = await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "My first report", content = Script, kind = "Report" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await Json(created);
        var id = draft["id"]!.GetValue<int>();
        Assert.Equal(1, draft["version"]!.GetValue<long>());

        var edited = Script + "-- a second thought\n";
        var saved = await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}",
            new { content = edited, baseVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(2, (await Json(saved))["version"]!.GetValue<long>());

        // Reopened from the list, as a fresh browser would.
        var listed = await JsonArray(await Send(client, HttpMethod.Get, learner, "/api/studio/private-drafts"));
        var summary = Assert.Single(listed);
        Assert.Equal(id, summary!["id"]!.GetValue<int>());
        Assert.Equal("My first report", summary["name"]!.GetValue<string>());
        Assert.Null(summary["content"]);
        var reopened = await Json(await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}"));
        Assert.Equal(edited, reopened["content"]!.GetValue<string>());
        Assert.Equal(2, reopened["version"]!.GetValue<long>());
        Assert.Equal("Report", reopened["kind"]!.GetValue<string>());

        // It is not a catalog document: nothing is listed for the learner or the administrator.
        Assert.Empty(await JsonArray(await Send(client, HttpMethod.Get, admin, "/api/studio/reports")));
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Reports.AnyAsync());
    }

    [Fact]
    public async Task AnotherPrincipalAndAnotherTenantGetTheAnswerOfAMissingDraft()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var learner = await CreateSignedInUserAsync(client, admin, "Viewer");
        var colleague = await CreateSignedInUserAsync(client, admin, "Viewer");
        var id = (await Json(await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "Mine", content = Script })))["id"]!.GetValue<int>();

        var missing = await Send(client, HttpMethod.Get, learner, "/api/studio/private-drafts/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var missingBody = await missing.Content.ReadAsStringAsync();

        // A colleague and an administrator: every route answers as it does for a draft that never existed.
        foreach (var other in new[] { colleague, admin })
        {
            var read = await Send(client, HttpMethod.Get, other, $"/api/studio/private-drafts/{id}");
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.Equal(missingBody, await read.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Put, other,
                $"/api/studio/private-drafts/{id}", new { content = "-- taken over", baseVersion = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, other,
                $"/api/studio/private-drafts/{id}/revisions")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, other,
                $"/api/studio/private-drafts/{id}/revisions/1")).StatusCode);
            Assert.Empty(await JsonArray(await Send(client, HttpMethod.Get, other, "/api/studio/private-drafts")));
            // A delete says nothing either way, and removes nothing.
            Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete, other,
                $"/api/studio/private-drafts/{id}")).StatusCode);
        }
        Assert.Equal(Script, (await Json(await Send(client, HttpMethod.Get, learner,
            $"/api/studio/private-drafts/{id}")))["content"]!.GetValue<string>());

        // The same owner id in another tenant's partition is another tenant's draft.
        int foreignId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var owner = await db.StudioPrivateDrafts.Where(item => item.Id == id).Select(item => item.OwnerUserId).SingleAsync();
            var foreign = new StudioPrivateDraft
            {
                TenantId = "tenant-b",
                OwnerUserId = owner,
                Name = "Theirs",
                Content = "-- tenant b",
                ContentHash = "sha256:b"
            };
            db.StudioPrivateDrafts.Add(foreign);
            await db.SaveChangesAsync();
            foreignId = foreign.Id;
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, learner,
            $"/api/studio/private-drafts/{foreignId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Put, learner,
            $"/api/studio/private-drafts/{foreignId}", new { content = "-- crossed", baseVersion = 1 })).StatusCode);
        Assert.Single(await JsonArray(await Send(client, HttpMethod.Get, learner, "/api/studio/private-drafts")));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete, learner,
            $"/api/studio/private-drafts/{foreignId}")).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Equal("-- tenant b", await check.ServiceProvider.GetRequiredService<PortalDbContext>()
            .StudioPrivateDrafts.Where(item => item.Id == foreignId).Select(item => item.Content).SingleAsync());
    }

    [Fact]
    public async Task AStaleSaveIsRefusedAsAConflictAndChangesNothing()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var learner = await CreateSignedInUserAsync(client, await AdminTokenAsync(client), "Viewer");
        var id = (await Json(await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "Two tabs", content = Script })))["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}",
            new { content = "-- first tab", baseVersion = 1 })).StatusCode);

        var stale = await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}",
            new { content = "-- second tab", baseVersion = 1 });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var refusal = await Json(stale);
        Assert.Equal("DRAFT_CONFLICT", refusal["code"]!.GetValue<string>());
        Assert.Equal(2, refusal["currentVersion"]!.GetValue<long>());
        var kept = await Json(await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}"));
        Assert.Equal("-- first tab", kept["content"]!.GetValue<string>());
        Assert.Equal(2, kept["version"]!.GetValue<long>());
    }

    [Fact]
    public async Task ADraftHoldingAPlaintextCredentialIsNotKept()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var learner = await CreateSignedInUserAsync(client, await AdminTokenAsync(client), "Viewer");
        const string secret = "CREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'hunter2');";

        var refusedCreate = await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts", new { name = "Leaky", content = secret });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusedCreate.StatusCode);
        Assert.Equal("PLAINTEXT_SECRET", (await Json(refusedCreate))["code"]!.GetValue<string>());
        Assert.Empty(await JsonArray(await Send(client, HttpMethod.Get, learner, "/api/studio/private-drafts")));

        var id = (await Json(await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "Clean", content = "CREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'SECRET:db');" })))["id"]!.GetValue<int>();
        var refusedSave = await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}", new { content = secret, baseVersion = 1 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusedSave.StatusCode);
        Assert.Equal(1, (await Json(await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}")))["version"]!.GetValue<long>());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.StudioPrivateDrafts.AnyAsync(item => item.Content.Contains("hunter2")));
        Assert.False(await db.StudioPrivateDraftRevisions.AnyAsync(item => item.Content.Contains("hunter2")));
    }

    [Fact]
    public async Task TheConfiguredLimitsDecideHowManyDraftsAndSavesAreKeptAndForHowLong()
    {
        using var factory = new SmallLimitsFactory();
        using var client = factory.CreateClient();
        var learner = await CreateSignedInUserAsync(client, await AdminTokenAsync(client), "Viewer");

        // Saves kept: three, newest first.
        var id = (await Json(await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "Kept", content = "-- v1" })))["id"]!.GetValue<int>();
        for (var version = 1; version <= 4; version++)
            Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}",
                new { content = $"-- v{version + 1}", baseVersion = version })).StatusCode);
        var revisions = await JsonArray(await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}/revisions"));
        Assert.Equal([5L, 4L, 3L], revisions.Select(item => item!["version"]!.GetValue<long>()).ToArray());
        Assert.Equal("-- v3", (await Json(await Send(client, HttpMethod.Get, learner,
            $"/api/studio/private-drafts/{id}/revisions/3")))["content"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, learner,
            $"/api/studio/private-drafts/{id}/revisions/2")).StatusCode);

        // Drafts per user: two.
        Assert.Equal(HttpStatusCode.Created, (await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "Second", content = Script })).StatusCode);
        var third = await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts", new { name = "Third", content = Script });
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("DRAFT_LIMIT", (await Json(third))["code"]!.GetValue<string>());

        // Retention: five days after the last save, the draft and its saves are gone for good.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var draft = await db.StudioPrivateDrafts.SingleAsync(item => item.Id == id);
            draft.UpdatedAt = DateTime.UtcNow.AddDays(-4);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var draft = await db.StudioPrivateDrafts.SingleAsync(item => item.Id == id);
            draft.UpdatedAt = DateTime.UtcNow.AddDays(-6);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}")).StatusCode);
        Assert.Single(await JsonArray(await Send(client, HttpMethod.Get, learner, "/api/studio/private-drafts")));
        using var check = factory.Services.CreateScope();
        var after = check.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await after.StudioPrivateDrafts.AnyAsync(item => item.Id == id));
        Assert.False(await after.StudioPrivateDraftRevisions.AnyAsync(item => item.DraftId == id));
    }

    [Fact]
    public async Task WithPrivateDraftsOff_NothingIsKeptAndTheSessionSaysSo()
    {
        using var factory = new PrivateDraftsOffFactory();
        using var client = factory.CreateClient();
        var learner = await CreateSignedInUserAsync(client, await AdminTokenAsync(client), "Viewer");

        Assert.False((await Json(await Send(client, HttpMethod.Get, learner, "/api/studio/session")))["privateDrafts"]!.GetValue<bool>());
        var refused = await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts", new { name = "Off", content = Script });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("PRIVATE_DRAFTS_OFF", (await Json(refused))["code"]!.GetValue<string>());
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().StudioPrivateDrafts.AnyAsync());
    }

    [Fact]
    public async Task UnsavedEditsAreKeptBesideTheDraftWithoutBecomingAVersion()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var learner = await CreateSignedInUserAsync(client, admin, "Viewer");
        var id = (await Json(await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "Recover me", content = Script })))["id"]!.GetValue<int>();
        var unsaved = $"/api/studio/private-drafts/{id}/unsaved";
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, learner, unsaved)).StatusCode);

        var savedAt = await SavedAtAsync(factory, id);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Put, learner, unsaved,
            new { content = "-- typed, not saved", baseVersion = 1 })).StatusCode);

        var kept = await Json(await Send(client, HttpMethod.Get, learner, unsaved));
        Assert.Equal("-- typed, not saved", kept["content"]!.GetValue<string>());
        Assert.Equal(1, kept["baseVersion"]!.GetValue<long>());
        // The saved draft is untouched: same text, same version, and its retention clock did not move.
        var draft = await Json(await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}"));
        Assert.Equal(Script, draft["content"]!.GetValue<string>());
        Assert.Equal(1, draft["version"]!.GetValue<long>());
        Assert.Equal(savedAt, await SavedAtAsync(factory, id));
        Assert.Single(await JsonArray(await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}/revisions")));

        // Nobody else can read, replace or clear them.
        foreach (var other in new[] { admin, await CreateSignedInUserAsync(client, admin, "Viewer") })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, other, unsaved)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Put, other, unsaved,
                new { content = "-- planted", baseVersion = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete, other, unsaved)).StatusCode);
        }
        Assert.Equal("-- typed, not saved", (await Json(await Send(client, HttpMethod.Get, learner, unsaved)))["content"]!.GetValue<string>());

        // A plaintext credential is refused and what was kept stays.
        var refused = await Send(client, HttpMethod.Put, learner, unsaved,
            new { content = "CREATE CONNECTION db AS MSSQL(SERVER = 'h', PASSWORD = 'hunter2');", baseVersion = 1 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("PLAINTEXT_SECRET", (await Json(refused))["code"]!.GetValue<string>());
        Assert.Equal("-- typed, not saved", (await Json(await Send(client, HttpMethod.Get, learner, unsaved)))["content"]!.GetValue<string>());

        // Saving makes a version of the text and there is nothing left to recover.
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}",
            new { content = "-- typed, now saved", baseVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, learner, unsaved)).StatusCode);
    }

    [Fact]
    public async Task UnsavedEditsExpireOnTheRecoveryPolicyAndTheDraftDoesNot()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var learner = await CreateSignedInUserAsync(client, await AdminTokenAsync(client), "Viewer");
        var id = (await Json(await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "Old edits", content = Script })))["id"]!.GetValue<int>();
        var unsaved = $"/api/studio/private-drafts/{id}/unsaved";
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Put, learner, unsaved,
            new { content = "-- a week old", baseVersion = 1 })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            await db.StudioPrivateDrafts.Where(item => item.Id == id).ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.UnsavedUpdatedAt, DateTime.UtcNow.AddDays(-8)));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, learner, unsaved)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Get, learner, $"/api/studio/private-drafts/{id}")).StatusCode);
        using var check = factory.Services.CreateScope();
        Assert.Null(await check.ServiceProvider.GetRequiredService<PortalDbContext>().StudioPrivateDrafts
            .Where(item => item.Id == id).Select(item => item.UnsavedContent).SingleAsync());
    }

    [Fact]
    public async Task WithDraftRecoveryOff_UnsavedEditsAreRefusedAndSavingStillWorks()
    {
        using var factory = new RecoveryOffFactory();
        using var client = factory.CreateClient();
        var learner = await CreateSignedInUserAsync(client, await AdminTokenAsync(client), "Viewer");
        var id = (await Json(await Send(client, HttpMethod.Post, learner, "/api/studio/private-drafts",
            new { name = "No recovery", content = Script })))["id"]!.GetValue<int>();

        var refused = await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}/unsaved",
            new { content = "-- typed", baseVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("DRAFT_RECOVERY_OFF", (await Json(refused))["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Put, learner, $"/api/studio/private-drafts/{id}",
            new { content = "-- saved", baseVersion = 1 })).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().StudioPrivateDrafts
            .Where(item => item.Id == id).Select(item => item.UnsavedContent).SingleAsync());
    }

    private static async Task<DateTime> SavedAtAsync(PortalWebFactory factory, int id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PortalDbContext>().StudioPrivateDrafts
            .Where(item => item.Id == id).Select(item => item.UpdatedAt).SingleAsync();
    }

    [Fact]
    public async Task PublishingADraftCreatesTheCatalogDocumentWithItsTextAndRecordsItsHistory()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var folderId = await CreateFolderAsync(client, admin);
        var id = (await Json(await Send(client, HttpMethod.Post, admin, "/api/studio/private-drafts",
            new { name = "Quarterly", content = "-- v1\n" + Script })))["id"]!.GetValue<int>();
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Put, admin, $"/api/studio/private-drafts/{id}",
            new { content = "-- v2\n" + Script, baseVersion = 1 })).StatusCode);
        var final = "-- v3\n" + Script;
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Put, admin, $"/api/studio/private-drafts/{id}",
            new { content = final, baseVersion = 2 })).StatusCode);
        var hashes = (await JsonArray(await Send(client, HttpMethod.Get, admin, $"/api/studio/private-drafts/{id}/revisions")))
            .Select(item => item!["contentHash"]!.GetValue<string>()).ToList();
        Assert.Equal(3, hashes.Count);

        var published = await Send(client, HttpMethod.Post, admin, $"/api/studio/private-drafts/{id}/publish",
            new { folderId, version = 3, name = "Quarterly sales" });

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var report = await Json(published);
        var reportId = report["id"]!.GetValue<int>();
        Assert.Equal("Quarterly sales", report["name"]!.GetValue<string>());
        Assert.Equal("Report", report["kind"]!.GetValue<string>());
        Assert.Equal(folderId, report["folderId"]!.GetValue<int>());
        // The text is the draft's last save, unchanged.
        var script = await Json(await Send(client, HttpMethod.Get, admin, $"/api/reports/{reportId}/script-content"));
        Assert.Equal(final, script["scriptText"]!.GetValue<string>());

        // The draft is gone, and the audit trail names every kept save without holding any text.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Get, admin, $"/api/studio/private-drafts/{id}")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.False(await db.StudioPrivateDrafts.AnyAsync(item => item.Id == id));
        Assert.False(await db.StudioPrivateDraftRevisions.AnyAsync(item => item.DraftId == id));
        var entry = await db.AuditLogs.SingleAsync(log => log.Action == "PUBLISH_PRIVATE_DRAFT");
        Assert.Equal(reportId.ToString(), entry.ResourceId);
        Assert.Contains($"draft={id}", entry.Detail);
        Assert.Contains("publishedVersion=3", entry.Detail);
        foreach (var hash in hashes) Assert.Contains(hash, entry.Detail);
        Assert.Contains("v1@", entry.Detail);
        Assert.Contains("v3@", entry.Detail);
        Assert.DoesNotContain("REPORT TITLE", entry.Detail);
    }

    [Fact]
    public async Task APipelineDraftIsPublishedAsAPipeline()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var folderId = await CreateFolderAsync(client, admin);
        var id = (await Json(await Send(client, HttpMethod.Post, admin, "/api/studio/private-drafts",
            new { name = "Load users", content = "SELECT 1 AS One;\n", kind = "Pipeline" })))["id"]!.GetValue<int>();

        var published = await Send(client, HttpMethod.Post, admin, $"/api/studio/private-drafts/{id}/publish", new { folderId, version = 1 });

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var report = await Json(published);
        Assert.Equal("Pipeline", report["kind"]!.GetValue<string>());
        // With no name given, the draft's own.
        Assert.Equal("Load users", report["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ARefusedPublishLeavesTheDraftAndTheCatalogAsTheyWere()
    {
        using var factory = new LearnerFactory();
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var folderId = await CreateFolderAsync(client, admin);
        var learner = await CreateSignedInUserAsync(client, admin, "Viewer");
        var publisher = await CreateSignedInUserAsync(client, admin, "Publisher");

        async Task<int> DraftAsync(string owner, string name)
        {
            var id = (await Json(await Send(client, HttpMethod.Post, owner, "/api/studio/private-drafts",
                new { name, content = Script })))["id"]!.GetValue<int>();
            Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Put, owner, $"/api/studio/private-drafts/{id}",
                new { content = Script + "-- kept\n", baseVersion = 1 })).StatusCode);
            return id;
        }

        async Task AssertIntactAsync(string owner, int id)
        {
            var draft = await Json(await Send(client, HttpMethod.Get, owner, $"/api/studio/private-drafts/{id}"));
            Assert.Equal(Script + "-- kept\n", draft["content"]!.GetValue<string>());
            Assert.Equal(2, draft["version"]!.GetValue<long>());
            Assert.Equal(2, (await JsonArray(await Send(client, HttpMethod.Get, owner, $"/api/studio/private-drafts/{id}/revisions"))).Count);
        }

        // A learner without ReportPublish or ScriptSave.
        var learnerDraft = await DraftAsync(learner, "Learner");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Post, learner,
            $"/api/studio/private-drafts/{learnerDraft}/publish", new { folderId, version = 2 })).StatusCode);
        await AssertIntactAsync(learner, learnerDraft);

        // Someone who may publish cannot publish another person's draft: to them it does not exist.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(client, HttpMethod.Post, admin,
            $"/api/studio/private-drafts/{learnerDraft}/publish", new { folderId, version = 2 })).StatusCode);
        await AssertIntactAsync(learner, learnerDraft);

        // A Publisher with the capability but no Manage on the folder.
        var publisherDraft = await DraftAsync(publisher, "Publisher");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Post, publisher,
            $"/api/studio/private-drafts/{publisherDraft}/publish", new { folderId, version = 2 })).StatusCode);
        await AssertIntactAsync(publisher, publisherDraft);

        // The owner, fully entitled, but naming a stale version, a taken name, or leaving edits unsaved.
        var adminDraft = await DraftAsync(admin, "Mine");
        var stale = await Send(client, HttpMethod.Post, admin, $"/api/studio/private-drafts/{adminDraft}/publish", new { folderId, version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("DRAFT_CONFLICT", (await Json(stale))["code"]!.GetValue<string>());

        Assert.True((await Send(client, HttpMethod.Post, admin, "/api/studio/reports",
            new { folderId, name = "Taken", scriptText = Script })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, HttpMethod.Post, admin,
            $"/api/studio/private-drafts/{adminDraft}/publish", new { folderId, version = 2, name = "Taken" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Put, admin,
            $"/api/studio/private-drafts/{adminDraft}/unsaved", new { content = "-- not saved yet", baseVersion = 2 })).StatusCode);
        var unsaved = await Send(client, HttpMethod.Post, admin, $"/api/studio/private-drafts/{adminDraft}/publish", new { folderId, version = 2 });
        Assert.Equal(HttpStatusCode.Conflict, unsaved.StatusCode);
        Assert.Equal("DRAFT_HAS_UNSAVED_EDITS", (await Json(unsaved))["code"]!.GetValue<string>());
        await AssertIntactAsync(admin, adminDraft);

        // Nothing but the one report created on purpose reached the catalog.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal("Taken", (await db.Reports.SingleAsync()).Name);
        Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == "PUBLISH_PRIVATE_DRAFT"));
    }

    private static async Task<int> CreateFolderAsync(HttpClient client, string admin)
    {
        var folder = await Send(client, HttpMethod.Post, admin, "/api/folders",
            new { name = $"publish_{Guid.NewGuid():N}"[..20], parentId = (int?)null });
        Assert.True(folder.IsSuccessStatusCode, $"folder create: {(int)folder.StatusCode}");
        return (await Json(folder))["id"]!.GetValue<int>();
    }

    [Fact]
    public async Task AUserWhoMayNotReadScriptsMayNotKeepADraft()
    {
        // The stock test Portal maps no Studio capability to Viewer.
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var viewer = await CreateSignedInUserAsync(client, await AdminTokenAsync(client), "Viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Post, viewer, "/api/studio/private-drafts",
            new { name = "No", content = Script })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(client, HttpMethod.Get, viewer, "/api/studio/private-drafts")).StatusCode);
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private static async Task<string> CreateSignedInUserAsync(HttpClient client, string admin, string role)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await Send(client, HttpMethod.Post, admin, "/api/admin/users",
            new { username = $"u{suffix}", password = $"Init@{suffix}9!", role, email = $"u{suffix}@localhost" });
        Assert.True(created.IsSuccessStatusCode, $"user create: {created.StatusCode}");
        var first = await LoginAsync(client, $"u{suffix}", $"Init@{suffix}9!");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, first, "/api/auth/change-password",
            new { currentPassword = $"Init@{suffix}9!", newPassword = $"Next@{suffix}9!" })).StatusCode);
        return await LoginAsync(client, $"u{suffix}", $"Next@{suffix}9!");
    }

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        const string initial = "Admin@12345!";
        const string changed = "Admin@Drafts99!";
        var first = await LoginAsync(client, "admin", initial);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, first, "/api/auth/change-password",
            new { currentPassword = initial, newPassword = changed })).StatusCode);
        return await LoginAsync(client, "admin", changed);
    }

    private static async Task<string> LoginAsync(HttpClient client, string user, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response))["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string token, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new("Bearer", token);
        // User administration is version-checked; without the If-Match stamp it answers 428/412.
        if (method == HttpMethod.Post && path.StartsWith("/api/admin/", StringComparison.Ordinal))
            await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }

    private static async Task<JsonObject> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;

    private static async Task<JsonArray> JsonArray(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonArray>())!;
}
