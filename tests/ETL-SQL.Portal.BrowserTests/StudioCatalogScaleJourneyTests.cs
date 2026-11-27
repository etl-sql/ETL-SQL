using System.Text.Json;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(StudioAuthoringCollection.Name)]
public sealed class StudioCatalogScaleJourneyTests(StudioAuthoringFixture fixture)
{
    [Fact]
    public async Task HomePagesAndSearches2500DocumentsAndRefreshesWithoutChangingDirtyBuffers()
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(session.Page, workspace, false);
        var page = session.Page;
        const string dirtyText = "-- keep my unsaved work\nSELECT 42;";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, dirtyText);
        var prefix = "Scale" + Guid.NewGuid().ToString("N")[..8];
        int folderId;
        int otherFolderId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(user => user.UserName == PortalBrowserFixture.AdminUsername);
            var folder = new Folder { Name = prefix, Path = "/" + prefix, OwnerId = admin.Id };
            var other = new Folder { Name = prefix + "Moved", Path = "/" + prefix + "Moved", OwnerId = admin.Id };
            db.Folders.AddRange(folder, other);
            await db.SaveChangesAsync();
            folderId = folder.Id;
            otherFolderId = other.Id;
            db.Reports.AddRange(Enumerable.Range(0, 2500).Select(index => new Report
            {
                Name = $"{prefix}-{index:D4}",
                FolderId = folderId,
                CreatedBy = admin.Id,
                ScriptPath = $"fixture/{prefix}-{index}.rptsql"
            }));
            await db.SaveChangesAsync();
        }
        await page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Home", Exact = true }).ClickAsync();
        await page.GetByRole(Microsoft.Playwright.AriaRole.Textbox, new() { Name = "Search catalog", Exact = true }).FillAsync(prefix);
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("prefix => window.__STUDIO__.state.catalogReports.length === 50 && window.__STUDIO__.state.catalogReports.every(r => r.name.startsWith(prefix))", prefix);
        var first = await page.EvaluateAsync<int>("() => window.__STUDIO__.state.catalogReports[0].id");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("first => window.__STUDIO__.state.catalogReports.length === 50 && window.__STUDIO__.state.catalogReports[0].id > first", first);
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Previous page", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("first => window.__STUDIO__.state.catalogReports[0].id === first", first);

        await page.GetByRole(Microsoft.Playwright.AriaRole.Textbox, new() { Name = "Search catalog", Exact = true }).FillAsync(prefix + "Probe");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.catalogReports.length === 0");
        var probe = await page.EvaluateAsync<JsonElement>("async data => { const {studioApi} = await import('/js/api.js'); return studioApi.createReport(data); }",
            new { folderId, name = prefix + "Probe", scriptText = "SELECT 1;" });
        var probeId = probe.GetProperty("id").GetInt32();
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("id => window.__STUDIO__.state.catalogReports.length === 1 && window.__STUDIO__.state.catalogReports[0].id === id", probeId);
        var directPage = await page.Context.NewPageAsync();
        try
        {
            await fixture.SignInAsync(directPage);
            await directPage.GotoWithDiagnosticsAsync($"/studio.html?reportId={probeId}");
            await directPage.Locator("[data-choose-workflow='dashboard']").ClickAsync();
            await directPage.WaitForFunctionAsync("id => window.__STUDIO__?.state.documents.some(document => document.reportId === id)", probeId);
            Assert.Equal("SELECT 1;", await directPage.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
        }
        finally { await directPage.CloseAsync(); }
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(report => report.Id == probeId);
            report.FolderId = otherFolderId;
            report.Name = prefix + "ProbeRenamed";
            await db.SaveChangesAsync();
        }
        await page.Locator($"[data-open-report='{probeId}']").Last.ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("moved since the catalog was loaded");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("id => window.__STUDIO__.state.catalogReports[0]?.folderId === id", otherFolderId);
        Assert.Equal(prefix + "ProbeRenamed", await page.EvaluateAsync<string>("() => window.__STUDIO__.state.catalogReports[0].name"));
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            (await db.Reports.SingleAsync(report => report.Id == probeId)).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        await page.Locator($"[data-open-report='{probeId}']").Last.ClickAsync();
        await Expect(page.Locator("body")).ToContainTextAsync("unavailable or outside your catalog permissions");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.catalogReports.length === 0");
        Assert.Equal(dirtyText, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.documents[0].content"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task HomePagedDiscovery_PreservesKeyboardFocusRejectsStaleSearchAndRespectsVisibility()
    {
        var prefix = "St45" + Guid.NewGuid().ToString("N")[..8];
        var username = $"st45_{Guid.NewGuid():N}"[..16];
        var password = $"Studio!9{Guid.NewGuid():N}";

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>();
            var admin = await db.Users.SingleAsync(user => user.UserName == PortalBrowserFixture.AdminUsername);

            var author = new PortalUser
            {
                UserName = username,
                Email = $"{username}@example.test",
                IsActive = true,
                MustChangePassword = false
            };
            var created = await userManager.CreateAsync(author, password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            Assert.True((await userManager.AddToRoleAsync(author, "Publisher")).Succeeded);

            var group = new Group { Name = $"St45Group-{prefix}" };
            db.Groups.Add(group);
            await db.SaveChangesAsync();
            db.UserGroups.Add(new UserGroup { UserId = author.Id, GroupId = group.Id });
            db.GroupStudioCapabilities.AddRange(
                new GroupStudioCapability { GroupId = group.Id, Capability = "StudioAccess" },
                new GroupStudioCapability { GroupId = group.Id, Capability = "ScriptRead" });

            var restrictedFolder = new Folder { Name = $"00-Restricted-{prefix}", Path = $"/00-Restricted-{prefix}", OwnerId = admin.Id };
            var visibleFolder = new Folder { Name = $"Visible-{prefix}", Path = $"/Visible-{prefix}", OwnerId = admin.Id };
            db.Folders.AddRange(restrictedFolder, visibleFolder);
            await db.SaveChangesAsync();

            db.FolderAcls.Add(new FolderAcl
            {
                FolderId = visibleFolder.Id,
                GroupId = group.Id,
                Permission = FolderPermission.Author
            });

            // 60 hidden docs in restricted folder (sorting first) + 120 visible docs (3 pages at 50/page).
            db.Reports.AddRange(Enumerable.Range(0, 60).Select(index => new Report
            {
                Name = $"{prefix}-Hidden-{index:D4}",
                FolderId = restrictedFolder.Id,
                CreatedBy = admin.Id,
                ScriptPath = $"fixture/{prefix}-hidden-{index}.rptsql"
            }));
            db.Reports.AddRange(Enumerable.Range(0, 120).Select(index => new Report
            {
                Name = $"{prefix}-Visible-{index:D4}",
                FolderId = visibleFolder.Id,
                CreatedBy = admin.Id,
                ScriptPath = $"fixture/{prefix}-visible-{index}.rptsql"
            }));
            await db.SaveChangesAsync();
        }

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await page.GotoWithDiagnosticsAsync("/login.html");
        await page.FillAsync("#username", username);
        await page.FillAsync("#password", password);
        await page.ClickAsync("#loginBtn");
        await page.WaitForURLWithDiagnosticsAsync("**/index.html", new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 20_000 });
        await page.GotoWithDiagnosticsAsync("/studio.html");
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)");

        // 1. Search by keyboard (Enter in input) — focus stays in Search catalog input, hidden docs excluded from results & total.
        var searchInput = page.GetByRole(Microsoft.Playwright.AriaRole.Textbox, new() { Name = "Search catalog", Exact = true });
        await searchInput.FocusAsync();
        await searchInput.FillAsync(prefix);
        await page.RunAndWaitForResponseAsync(
            async () => await page.Keyboard.PressAsync("Enter"),
            response => response.Url.Contains("/api/studio/catalog", StringComparison.Ordinal)
                && response.Url.Contains($"query={Uri.EscapeDataString(prefix)}", StringComparison.Ordinal));
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 1–50 of 120 (Page 1 of 3).");
        Assert.Equal("query", await page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('name')"));
        Assert.True(await page.EvaluateAsync<bool>("prefix => window.__STUDIO__.state.catalogReports.every(r => r.name.startsWith(prefix + '-Visible-'))", prefix));

        // 2. Keyboard paging: Next page -> Page 2 (focus stays on Next page) -> Page 3 (Next disabled, focus moves to Previous page).
        var nextBtn = page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Next page", Exact = true });
        await nextBtn.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 51–100 of 120 (Page 2 of 3).");
        Assert.True(await page.EvaluateAsync<bool>("() => Boolean(document.activeElement?.hasAttribute('data-catalog-next'))"));

        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 101–120 of 120 (Page 3 of 3).");
        await Expect(nextBtn).ToBeDisabledAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => Boolean(document.activeElement?.hasAttribute('data-catalog-previous'))"));

        // Pressing Enter on Previous page returns to Page 2 and keeps focus on Previous page.
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 51–100 of 120 (Page 2 of 3).");
        Assert.True(await page.EvaluateAsync<bool>("() => Boolean(document.activeElement?.hasAttribute('data-catalog-previous'))"));

        // 3. Slow response to an older search cannot overwrite newer results.
        var slowSearchQuery = $"{prefix}-Visible-00";
        var fastSearchQuery = $"{prefix}-Visible-011";
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await page.RouteAsync("**/api/studio/catalog?*", async route =>
        {
            if (route.Request.Url.Contains(Uri.EscapeDataString(slowSearchQuery), StringComparison.Ordinal))
            {
                slowStarted.TrySetResult();
                await releaseSlow.Task;
                try
                {
                    await route.ContinueAsync();
                }
                catch (Microsoft.Playwright.PlaywrightException)
                {
                    // Request was aborted by the client when superseded — expected.
                }
                return;
            }
            await route.ContinueAsync();
        });

        await searchInput.FocusAsync();
        await searchInput.FillAsync(slowSearchQuery);
        await page.Keyboard.PressAsync("Enter");
        await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Issue a newer, faster search while the older one is still in flight.
        await searchInput.FillAsync(fastSearchQuery);
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 1–10 of 10 (Page 1 of 1).");

        // Release the older slow response and confirm the newer results stay intact.
        releaseSlow.TrySetResult();
        await page.WaitForTimeoutAsync(250);
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 1–10 of 10 (Page 1 of 1).");
        Assert.True(await page.EvaluateAsync<bool>("q => window.__STUDIO__.state.catalogReports.every(r => r.name.includes(q))", fastSearchQuery));

        await page.UnrouteAsync("**/api/studio/catalog?*");

        // Unsubmitted input and focus survive the arrival of a previously submitted search.
        var pendingSearch = new TaskCompletionSource<Microsoft.Playwright.IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/studio/catalog?*", route =>
        {
            pendingSearch.TrySetResult(route);
            return Task.CompletedTask;
        });
        await searchInput.FillAsync($"{prefix}-not-found");
        await searchInput.PressAsync("Enter");
        var pending = await pendingSearch.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await searchInput.FillAsync("New search still being typed");
        await pending.ContinueAsync();
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("0 matching catalog documents.");
        await Expect(searchInput).ToHaveValueAsync("New search still being typed");
        await Expect(searchInput).ToBeFocusedAsync();
        await page.UnrouteAsync("**/api/studio/catalog?*");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task HomeRejectsStaleSelectionOfAnAlreadyOpenDirtyDocument()
    {
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(page, workspace, false);

        const string dirtyText = "-- Keep this edit when Home opens a stale selection\nSELECT 42;";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, dirtyText);
        var reportId = await page.EvaluateAsync<int>("() => window.__STUDIO__.state.documents[0].reportId");
        var documentId = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.documents[0].id");
        var folderPath = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.documents[0].folderPath");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Home", Exact = true }).ClickAsync();
        var searchInput = page.GetByRole(Microsoft.Playwright.AriaRole.Textbox, new() { Name = "Search catalog", Exact = true });
        await searchInput.FillAsync(folderPath);
        await searchInput.PressAsync("Enter");
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 1–1 of 1");
        var open = page.Locator($"[data-open-report='{reportId}']").First;
        await Expect(open).ToBeVisibleAsync();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(report => report.Id == reportId);
            report.Name += " Renamed";
            await db.SaveChangesAsync();
        }

        await open.ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToContainTextAsync("renamed since the catalog was loaded");
        Assert.Equal("__home__", await page.EvaluateAsync<string>("() => window.__STUDIO__.state.activeDocId"));

        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToHaveCountAsync(0);
        await open.ClickAsync();
        await page.WaitForFunctionAsync("id => window.__STUDIO__.state.activeDocId === id", documentId);
        Assert.Equal(dirtyText, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task HomeCatalogRefreshAndStaleOpenRecovery_CoversCreateRenameMoveDeleteWithoutDisturbingDirtyBuffer()
    {
        // ST46 fixture: 51 documents (2 pages at pageSize=50: 50 on page 1, 1 on page 2)
        // plus an external create, in-place rename, move, and delete of the page-2 item
        // while an unsaved dirty document stays open in the editor.
        using var workspace = new StudioTempWorkspace();
        await using var session = await fixture.NewSessionAsync();
        await using var host = await new StudioExecutionScopeJourneyTests(fixture).OpenAsync(session.Page, workspace, false);
        var page = session.Page;

        const string dirtyText = "-- ST46 unsaved buffer must survive stale opens and refreshes\nSELECT 'keep-me';";
        await StudioExecutionScopeJourneyTests.ReplaceAsync(page, dirtyText);

        var prefix = "St46" + Guid.NewGuid().ToString("N")[..8];
        int folderAId;
        int folderBId;
        int page2ReportId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(user => user.UserName == PortalBrowserFixture.AdminUsername);
            var folderA = new Folder { Name = $"{prefix}-A", Path = $"/{prefix}-A", OwnerId = admin.Id };
            var folderB = new Folder { Name = $"{prefix}-B", Path = $"/{prefix}-B", OwnerId = admin.Id };
            db.Folders.AddRange(folderA, folderB);
            await db.SaveChangesAsync();
            folderAId = folderA.Id;
            folderBId = folderB.Id;

            var seeded = Enumerable.Range(0, 51).Select(index => new Report
            {
                Name = $"{prefix}-{index:D3}",
                FolderId = folderAId,
                CreatedBy = admin.Id,
                ScriptPath = $"fixture/{prefix}-{index:D3}.rptsql"
            }).ToList();
            db.Reports.AddRange(seeded);
            await db.SaveChangesAsync();
            page2ReportId = seeded[50].Id;
        }

        // Open Home, search for prefix, navigate to Page 2 (1 item: St46...-050).
        await page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Home", Exact = true }).ClickAsync();
        var searchInput = page.GetByRole(Microsoft.Playwright.AriaRole.Textbox, new() { Name = "Search catalog", Exact = true });
        await searchInput.FillAsync(prefix);
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 1–50 of 51 (Page 1 of 2).");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 51–51 of 51 (Page 2 of 2).");

        // 1. External create: appears on Page 2 after Refresh catalog.
        int createdId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var admin = await db.Users.SingleAsync(user => user.UserName == PortalBrowserFixture.AdminUsername);
            var created = new Report
            {
                Name = $"{prefix}-051-Created",
                FolderId = folderAId,
                CreatedBy = admin.Id,
                ScriptPath = $"fixture/{prefix}-051-created.rptsql"
            };
            db.Reports.Add(created);
            await db.SaveChangesAsync();
            createdId = created.Id;
        }
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 51–52 of 52 (Page 2 of 2).");
        Assert.Contains(createdId, await page.EvaluateAsync<int[]>("() => window.__STUDIO__.state.catalogReports.map(r => r.id)"));

        // 2. External in-place rename: stale open fails clearly and shows inline alert; Refresh catalog updates name.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(r => r.Id == page2ReportId);
            report.Name = $"{prefix}-050-Renamed";
            await db.SaveChangesAsync();
        }
        await page.Locator($"[data-open-report='{page2ReportId}']").First.ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToContainTextAsync("renamed since the catalog was loaded");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToHaveCountAsync(0);
        Assert.Equal($"{prefix}-050-Renamed", await page.EvaluateAsync<string?>(
            "id => window.__STUDIO__.state.catalogReports.find(r => r.id === id)?.name ?? null", page2ReportId));

        // 3. External move to folder B: stale open fails clearly; Refresh catalog shows updated folder.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var report = await db.Reports.SingleAsync(r => r.Id == page2ReportId);
            report.FolderId = folderBId;
            await db.SaveChangesAsync();
        }
        await page.Locator($"[data-open-report='{page2ReportId}']").First.ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToContainTextAsync("moved since the catalog was loaded");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToHaveCountAsync(0);
        Assert.Equal(folderBId, await page.EvaluateAsync<int?>(
            "id => window.__STUDIO__.state.catalogReports.find(r => r.id === id)?.folderId ?? null", page2ReportId));

        // 4. External delete of both Page-2 items: stale open fails clearly; Refresh catalog clamps cleanly back to Page 1 of 1.
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            foreach (var report in await db.Reports.Where(r => r.Id == page2ReportId || r.Id == createdId).ToListAsync())
                report.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        await page.Locator($"[data-open-report='{page2ReportId}']").First.ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToContainTextAsync("unavailable or outside your catalog permissions");
        await page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Refresh catalog", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-catalog-alert]")).ToHaveCountAsync(0);
        await Expect(page.Locator("[data-catalog-status]")).ToContainTextAsync("Showing 1–50 of 50 (Page 1 of 1).");

        // 5. Switch back to the dirty editor tab and confirm unsaved text is intact.
        var dirtyTabId = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.documents[0].id");
        await page.Locator($"#etlsql-tab-{dirtyTabId}").ClickAsync();
        Assert.Equal(dirtyText, await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));
        Assert.Empty(session.PageErrors);
    }
}
