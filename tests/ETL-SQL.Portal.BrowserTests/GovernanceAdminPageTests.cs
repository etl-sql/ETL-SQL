using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(GovernanceAdminCollection.Name)]
public sealed class GovernanceAdminPageTests(GovernanceAdminFixture fixture)
{
    [Fact]
    public async Task Governance_NativeDialogsTrapFocusAndReopenAfterKeyboardDismissal()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        foreach (var (tab, opener, dialogId, firstInput, cancel, save) in new[]
        {
            ("settings", "btnAddNewCategory", "categoryModalBackdrop", "catLabel", "btnCancelCategoryModal", "btnConfirmCategoryModal"),
            ("glossary", "btnAddNewTerm", "glossaryModalBackdrop", "glossaryTerm", "btnCancelGlossaryModal", "btnConfirmGlossaryModal"),
        })
        {
            await page.GotoAsync($"/index.html#governance/{tab}");
            var open = page.Locator($"#{opener}");
            var dialog = page.Locator($"#{dialogId}");
            var first = page.Locator($"#{firstInput}");
            var last = page.Locator($"#{save}");
            await Expect(open).ToBeEnabledAsync();
            await open.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(dialog).ToBeVisibleAsync();
            await Expect(first).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Shift+Tab");
            await Expect(last).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Tab");
            await Expect(first).ToBeFocusedAsync();
            await first.FillAsync("Cancelled keyboard draft");
            await page.Keyboard.PressAsync("Escape");
            await Expect(dialog).ToBeHiddenAsync();
            await Expect(dialog).ToHaveAttributeAsync("aria-hidden", "true");
            await Expect(open).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(dialog).ToBeVisibleAsync();
            await Expect(first).ToHaveValueAsync("");
            await Expect(first).ToBeFocusedAsync();
            await page.Locator($"#{cancel}").FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Expect(dialog).ToBeHiddenAsync();
            await Expect(open).ToBeFocusedAsync();
        }
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_RealChangesPersistWithMatchingReceiptsAndAuditRows()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var table = $"gov_native_{Guid.NewGuid():N}";
        var entry = new LineageEntry(table, "SELECT");
        entry.Metadata["steward"] = "admin";
        await fixture.Factory.Services.GetRequiredService<ILineageCatalogStore>()
            .SaveLineageAsync([entry], $"job-{table}", $"loads/{table}.etlsql", DateTime.UtcNow);
        await page.GotoAsync("/index.html#governance/overview");
        await Expect(page.Locator("#btnRunScan")).ToBeEnabledAsync();
        var scan = await WriteAsync(page, () => page.Locator("#btnRunScan").ClickAsync(), "/api/governance/scan");
        Assert.Equal("completed", scan.GetProperty("status").GetString());

        await page.GotoAsync("/index.html#governance/workqueue");
        var asset = page.Locator(".gov-table tbody tr").Filter(new() { HasText = table });
        await Expect(asset).ToHaveCountAsync(1);
        var review = await WriteAsync(page, () => asset.Locator("[data-review-asset]").ClickAsync(), "/api/governance/assets/review");
        Assert.Equal(table, review.GetProperty("assetKey").GetString());
        Assert.False(string.IsNullOrEmpty(review.GetProperty("assetVersion").GetString()));
        Assert.True(review.TryGetProperty("reviewedAtUtc", out _));
        var badge = await WriteAsync(page, () => asset.Locator("[data-assign-badge-to]").SelectOptionAsync("Certified"), "/api/governance/assets/badges");
        Assert.Equal("Certified", badge.GetProperty("badge").GetString());
        Assert.False(badge.TryGetProperty("assignedAtUtc", out _)); // Preserve the existing new-assignment wire shape.
        await Expect(asset.Locator(".gov-badge-assigned").Filter(new() { HasText = "Certified" })).ToBeVisibleAsync();
        await WriteAsync(page, () => asset.Locator("[data-remove-badge-name='Certified']").ClickAsync(), "/api/governance/assets/badges?", 204, prefix: true);
        await Expect(asset.Locator("[data-remove-badge-name='Certified']")).ToHaveCountAsync(0);

        await asset.Locator("[data-ignore-finding]").ClickAsync();
        await page.Locator("#decisionReason").FillAsync("Reviewed by the native browser journey.");
        var ignored = await WriteAsync(page, () => page.Locator("#btnConfirmDecision").ClickAsync(), "/decide");
        var findingId = ignored.GetProperty("id").GetInt32();
        Assert.Equal("ignored", ignored.GetProperty("status").GetString());
        await page.GotoAsync("/index.html#governance/exceptions");
        await Expect(page.Locator(".gov-body")).ToContainTextAsync("do not follow the asset scope");
        var reopened = await WriteAsync(page, () => page.Locator($"[data-reopen-finding='{findingId}']").ClickAsync(), "/decide");
        Assert.Equal("reopened", reopened.GetProperty("status").GetString());

        await page.GotoAsync("/index.html#governance/settings");
        await Expect(page.Locator("#btnSaveScoringSettings")).ToBeEnabledAsync();
        await page.Locator("#settingsDeductMeta").FillAsync("7");
        var settings = await WriteAsync(page, () => page.Locator("#btnSaveScoringSettings").ClickAsync(), "/api/governance/settings");
        Assert.Equal(7, settings.GetProperty("deductMetadata").GetInt32());
        var category = "native-" + Guid.NewGuid().ToString("N");
        await page.Locator("#btnAddNewCategory").ClickAsync();
        await page.Locator("#catLabel").FillAsync("Native browser category");
        await page.Locator("#catValue").FillAsync(category);
        await page.Locator("#catExpiry").FillAsync("3");
        await WriteAsync(page, () => page.Locator("#btnConfirmCategoryModal").ClickAsync(), "/api/governance/categories");
        await page.Locator($"[data-disable-cat='{category}']").ClickAsync();
        await WriteAsync(page, () => page.Locator(".etlsql-feedback-dialog").GetByRole(AriaRole.Button, new() { Name = "Disable", Exact = true }).ClickAsync(), $"/api/governance/categories/{category}", 204);
        await page.Locator($"[data-edit-cat='{category}']").ClickAsync();
        await page.Locator("#catLabel").FillAsync("Retained disabled category");
        var editedCategory = await WriteAsync(page, () => page.Locator("#btnConfirmCategoryModal").ClickAsync(), "/api/governance/categories");
        Assert.True(editedCategory.GetProperty("disabled").GetBoolean());

        await page.GotoAsync("/index.html#governance/glossary");
        await Expect(page.Locator("#btnAddNewTerm")).ToBeEnabledAsync();
        var term = "native-" + Guid.NewGuid().ToString("N");
        await page.Locator("#btnAddNewTerm").ClickAsync();
        await page.Locator("#glossaryTerm").FillAsync(term);
        await page.Locator("#glossaryType").FillAsync("DECIMAL");
        await page.Locator("#glossaryAliases").FillAsync("native_amount");
        await page.Locator("#glossaryDesc").FillAsync("Native browser definition.");
        await WriteAsync(page, () => page.Locator("#btnConfirmGlossaryModal").ClickAsync(), "/api/governance/glossary");
        await page.EvaluateAsync("""
            async term => { const { governanceApi } = await import('/js/api.js');
              await governanceApi.saveGlossaryTerm({ term, dataType: 'DECIMAL', aliases: 'native_amount',
                description: 'Native browser definition.', formula: null, steward: 'Native steward', disabled: true }); }
            """, term);
        await page.Locator("#btnGovRefresh").ClickAsync();
        await Expect(page.Locator($"[data-edit-term='{term}']")).ToBeEnabledAsync();
        await page.Locator($"[data-edit-term='{term}']").ClickAsync();
        await page.Locator("#glossaryDesc").FillAsync("Edited without changing stewardship.");
        var editedTerm = await WriteAsync(page, () => page.Locator("#btnConfirmGlossaryModal").ClickAsync(), "/api/governance/glossary");
        Assert.Equal("Native steward", editedTerm.GetProperty("steward").GetString());
        Assert.True(editedTerm.GetProperty("disabled").GetBoolean());
        await page.Locator($"[data-delete-term='{term}']").ClickAsync();
        await WriteAsync(page, () => page.Locator(".etlsql-feedback-dialog").GetByRole(AriaRole.Button, new() { Name = "Delete term", Exact = true }).ClickAsync(), $"/api/governance/glossary/{term}", 204);
        await Expect(page.Locator($"[data-edit-term='{term}']")).ToHaveCountAsync(0);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal("reopened", (await db.StewardshipFindings.SingleAsync(item => item.Id == findingId)).Status);
        Assert.Equal(2, await db.StewardshipFindingDecisions.CountAsync(item => item.FindingId == findingId));
        Assert.Equal(7, (await db.StewardshipSettings.SingleAsync()).DeductMetadata);
        Assert.True((await db.StewardshipResolutionCategories.SingleAsync(item => item.Value == category)).Disabled);
        Assert.False(await db.StewardshipGlossaryTerms.AnyAsync(item => item.Term == term));
        Assert.False(await db.StewardshipAssetBadges.AnyAsync(item => item.AssetKey == table && item.Badge == "Certified"));
        var actions = await db.AuditLogs.Select(item => item.Action).ToListAsync();
        foreach (var action in new[] { "SCAN", "REVIEW_ASSET", "ASSIGN_BADGE", "REMOVE_BADGE", "IGNORE", "REOPEN",
            "UPDATE_SETTINGS", "CREATE_CATEGORY", "DISABLE_CATEGORY", "UPDATE_CATEGORY", "CREATE_TERM", "UPDATE_TERM", "DELETE_TERM" })
            Assert.Contains("GOVERNANCE_" + action, actions);
        Assert.Empty(session.PageErrors);
    }

    private static async Task<JsonElement> WriteAsync(IPage page, Func<Task> action, string endpoint, int expected = 200, bool prefix = false)
    {
        var response = await page.RunAndWaitForResponseAsync(action, response => response.Request.Method != "GET"
            && (prefix ? response.Url.Contains(endpoint, StringComparison.Ordinal) : response.Url.EndsWith(endpoint, StringComparison.Ordinal)));
        Assert.Equal(expected, response.Status);
        var result = expected == 204 ? default : JsonDocument.Parse(await response.TextAsync()).RootElement.Clone();
        await Expect(page.Locator("#govMutationStatus")).ToBeHiddenAsync(new() { Timeout = 20_000 });
        return result;
    }

    [Theory]
    [InlineData("StewardshipViewer", false, false)]
    [InlineData("DataSteward", false, true)]
    [InlineData("StewardshipManager", true, true)]
    public async Task Governance_ConfigureAndDecisionControlsFollowTheRealRole(string role, bool canConfigure, bool canDecide)
    {
        var table = $"gov_role_{Guid.NewGuid():N}";
        await fixture.Factory.Services.GetRequiredService<ILineageCatalogStore>()
            .SaveLineageAsync([new LineageEntry(table, "SELECT")], $"job-{table}", $"loads/{table}.etlsql", DateTime.UtcNow);
        var username = "gov_" + Guid.NewGuid().ToString("N");
        const string initial = "Governance@Role99!", password = "Governance@Role99b!";
        await using (var admin = await fixture.NewSessionAsync())
        {
            await fixture.SignInAsync(admin.Page);
            var token = await admin.Page.EvaluateAsync<string>("() => sessionStorage.getItem('etlsql_token')");
            var created = await admin.Page.APIRequest.PostAsync($"{fixture.BaseUrl}/api/admin/users", new()
            {
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
                DataObject = new { username, password = initial, role, email = username + "@example.test" },
            });
            Assert.True(created.Ok, $"Creating {role} returned HTTP {created.Status}: {await created.TextAsync()}");
            await admin.Page.EvaluateAsync("async () => { const { governanceApi } = await import('/js/api.js'); await governanceApi.scan(); }");
        }
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoAsync("/login.html");
        await page.Locator("#username").FillAsync(username);
        await page.Locator("#password").FillAsync(initial);
        await page.Locator("#loginBtn").ClickAsync();
        await Expect(page.Locator("#changeForm")).ToBeVisibleAsync();
        await page.Locator("#currentPwd").FillAsync(initial);
        await page.Locator("#newPwd").FillAsync(password);
        await page.Locator("#confirmPwd").FillAsync(password);
        await page.Locator("#changeBtn").ClickAsync();
        await page.WaitForURLAsync("**/index.html");
        await page.GotoAsync("/index.html#governance/overview");
        await Expect(page.Locator(".gov-kpis")).ToBeVisibleAsync();
        if (canConfigure) await Expect(page.Locator("#btnRunScan")).ToBeEnabledAsync();
        else
        {
            await Expect(page.Locator("#btnRunScan")).ToBeDisabledAsync();
            Assert.Equal(403, await page.EvaluateAsync<int>("async () => (await fetch('/api/governance/scan', { method: 'POST', headers: { Authorization: 'Bearer ' + sessionStorage.getItem('etlsql_token') } })).status"));
        }
        await page.GotoAsync("/index.html#governance/workqueue");
        var row = page.Locator(".gov-table tbody tr").Filter(new() { HasText = table });
        await Expect(row).ToHaveCountAsync(1);
        var review = row.Locator("[data-review-asset]");
        if (canDecide)
        {
            await Expect(review).ToBeEnabledAsync();
            await WriteAsync(page, () => review.ClickAsync(), "/api/governance/assets/review");
        }
        else
        {
            await Expect(review).ToBeDisabledAsync();
            await Expect(row.Locator("[data-ignore-finding]")).ToBeDisabledAsync();
            await Expect(row.Locator("[data-assign-badge-to]")).ToBeDisabledAsync();
            Assert.Equal(403, await page.EvaluateAsync<int>("""
                async assetKey => (await fetch('/api/governance/assets/review', { method: 'POST',
                  headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + sessionStorage.getItem('etlsql_token') },
                  body: JSON.stringify({ assetKey, assetVersion: 'forbidden-version' }) })).status
                """, table));
        }
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_ATabOpenedWhileAScanReloadsIsDrawnWhenTheReloadFinishes()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/index.html#governance/overview");
        await Expect(page.Locator("#btnRunScan")).ToBeEnabledAsync();

        // Hold the read that follows the scan, so the tab change lands while it is in flight.
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string routePattern = "**/api/governance/dashboard**";
        await page.RouteAsync(routePattern, async route =>
        {
            reached.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        await page.Locator("#btnRunScan").ClickAsync();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await page.Locator("#govNavWorkqueue").ClickAsync();
        await Expect(page.Locator("[data-gov-state='loading']")).ToBeVisibleAsync();
        release.TrySetResult();

        await Expect(page.Locator("[data-gov-state='loading']")).ToHaveCountAsync(0);
        await Expect(page.Locator("#govNavWorkqueue")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        await Expect(page.Locator("#btnRunScan")).ToBeEnabledAsync();
        await page.UnrouteAsync(routePattern);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Governance_PendingAndUnconfirmedWritesSurviveLeavingAndRemountingTheView()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string routePattern = "**/api/governance/scan";
        await page.RouteAsync(routePattern, async route =>
        {
            reached.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" });
        });
        await page.GotoAsync("/index.html#governance/overview");
        await Expect(page.Locator("#btnRunScan")).ToBeEnabledAsync();
        await page.Locator("#btnRunScan").ClickAsync();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await page.GotoAsync("/index.html#governance/lineage");
        await Expect(page.Locator("#lineageSearchForm")).ToBeVisibleAsync();
        await page.GotoAsync("/index.html#governance/overview");
        await Expect(page.Locator(".gov-kpis")).ToBeVisibleAsync();
        await Expect(page.Locator("#btnRunScan")).ToBeDisabledAsync();
        await Expect(page.Locator("#govMutationStatus")).ToContainTextAsync("Saving");
        release.TrySetResult();
        await Expect(page.Locator("#govMutationStatus")).ToContainTextAsync("unconfirmed");
        await Expect(page.Locator("#btnRunScan")).ToBeDisabledAsync();
        await page.UnrouteAsync(routePattern);
        await page.Locator("#btnGovRefresh").ClickAsync();
        await Expect(page.Locator("#btnRunScan")).ToBeEnabledAsync();
        await Expect(page.Locator("#govMutationStatus")).ToBeHiddenAsync();
        Assert.Empty(session.PageErrors);
    }
}

public sealed class GovernanceAdminFixture() : PortalBrowserFixture(new PortalBrowserFactory());

[CollectionDefinition(Name)]
public sealed class GovernanceAdminCollection : ICollectionFixture<GovernanceAdminFixture>
{
    public const string Name = "Portal governance administration";
}
