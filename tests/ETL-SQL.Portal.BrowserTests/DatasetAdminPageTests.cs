using System.IO.Compression;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(DatasetAdminCollection.Name)]
public sealed class DatasetAdminPageTests(DatasetAdminFixture fixture)
{
    [Fact]
    public async Task Datasets_RealCacheFiltersExportsAndAuditedMutationsUseServerContracts()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var dataset = await SeedDatasetAsync();
        int groupId, userId;
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var group = new Group { Name = $"dataset_group_{Guid.NewGuid():N}" };
            var user = new PortalUser { UserName = $"dataset_owner_{Guid.NewGuid():N}" };
            db.Groups.Add(group);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            groupId = group.Id;
            userId = user.Id;
            db.DatasetUserAcls.Add(new DatasetUserAcl { DatasetId = dataset.Id, UserId = user.Id, Permission = DatasetPermission.Owner });
            await db.SaveChangesAsync();
        }
        await OpenAsync(page);
        var action = $"[data-id='{dataset.Id}']";
        await using (var otherReader = new FileStream(dataset.ParquetFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            var data = await page.RunAndWaitForResponseAsync(() => page.Locator($"[data-action='view']{action}").ClickAsync(),
                response => response.Url.Contains($"/api/datasets/{dataset.Id}/data?", StringComparison.Ordinal));
            Assert.True(data.Status == 200, await data.TextAsync());
            await Expect(page.Locator("#dv-totalCount")).ToHaveTextAsync("30");
            await Expect(page.Locator("#dv-tableWrap tfoot td")).ToHaveCountAsync(3);
            Assert.Equal(0, otherReader.Position);
        }
        await page.Locator(".dv-picker-btn[data-col='label']").ClickAsync();
        await Expect(page.Locator("#dv-pickerList input")).ToHaveCountAsync(30);
        await page.Locator("#dv-pickerSearch").FillAsync("row20");
        await Expect(page.Locator("#dv-pickerList input")).ToHaveCountAsync(1);
        await page.Locator("#dv-pickerList input").CheckAsync();
        await page.Locator("#dv-pickerApply").ClickAsync();
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("1");
        await Expect(page.Locator("#dv-tableWrap tbody tr")).ToContainTextAsync("row20");
        await page.Locator("#dv-resetBtn").ClickAsync();
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("30");
        await page.Locator("#dv-pageSize").SelectOptionAsync("25");
        await Expect(page.Locator("#dv-pageInfo")).ToHaveTextAsync("Page 1 of 2");
        await page.Locator("#dv-nextBtn").ClickAsync();
        await Expect(page.Locator("#dv-tableWrap tbody tr")).ToHaveCountAsync(5);
        await page.Locator(".dv-col-header[data-col='v']").ClickAsync();
        await page.Locator(".dv-filter-op[data-col='v']").SelectOptionAsync("gte");
        await page.Locator(".dv-filter-val[data-col='v']").FillAsync("20");
        await page.Locator(".dv-filter-val[data-col='v']").PressAsync("Enter");
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("11");
        await Expect(page.Locator("#dv-tableWrap tbody tr").First).ToContainTextAsync("row20");
        await Expect(page.Locator("#dv-tableWrap tfoot td").First).ToContainTextAsync("Avg 25");
        var csv = await page.RunAndWaitForDownloadAsync(() => page.Locator("#dv-exportBtn").ClickAsync());
        var csvText = await File.ReadAllTextAsync((await csv.PathAsync())!);
        Assert.Contains("row20", csvText);
        Assert.DoesNotContain("row19", csvText);
        Assert.Equal(12, csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        var xlsx = await page.RunAndWaitForDownloadAsync(() => page.Locator("#dv-exportXlsxBtn").ClickAsync());
        using (var zip = ZipFile.OpenRead((await xlsx.PathAsync())!))
        {
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
            var parts = new List<string>();
            foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("xl/", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            {
                using var reader = new StreamReader(entry.Open());
                parts.Add(await reader.ReadToEndAsync());
            }
            Assert.Contains("row20", string.Join('\n', parts));
            Assert.DoesNotContain("row19", string.Join('\n', parts));
        }
        await page.Locator("#dv-resetBtn").ClickAsync();
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("30");
        await page.Locator(".dv-filter-op[data-col='nullable']").SelectOptionAsync("gte");
        await page.Locator(".dv-filter-val[data-col='nullable']").FillAsync("20");
        await page.Locator(".dv-filter-val[data-col='nullable']").PressAsync("Enter");
        await Expect(page.Locator("#dv-filteredCount")).ToHaveTextAsync("10");
        await Expect(page.Locator("#dv-tableWrap tbody")).Not.ToContainTextAsync("row30");
        await Expect(page.Locator("#dv-tableWrap tfoot td").Nth(2)).ToContainTextAsync("Nulls 0");
        await page.Locator("#dv-closeBtn").ClickAsync();
        await page.Locator($"[data-action='edit']{action}").ClickAsync();
        await page.Locator("#ds-accessLevel").SelectOptionAsync("Private");
        await page.Locator("#ds-ttl").FillAsync("30m");
        var saved = await page.RunAndWaitForResponseAsync(() => page.Locator("#ds-saveBtn").ClickAsync(),
            response => response.Request.Method == "PATCH" && response.Url.EndsWith($"/api/datasets/{dataset.Id}", StringComparison.Ordinal));
        Assert.Equal(200, saved.Status);
        Assert.Equal("\"1\"", saved.Request.Headers["if-match"]);
        await Expect(page.Locator("#editDatasetForm")).ToBeHiddenAsync();
        await page.Locator($"[data-action='acl']{action}").ClickAsync();
        await Expect(page.Locator($"#dsAclTableWrap [data-uid='{userId}']")).ToBeVisibleAsync();
        await page.Locator("#ds-acl-group").SelectOptionAsync(groupId.ToString());
        await page.Locator("#ds-acl-perm").SelectOptionAsync("Refresh");
        await page.Locator("#ds-acl-grantBtn").ClickAsync();
        await Expect(page.Locator($"#dsAclTableWrap [data-gid='{groupId}']")).ToBeVisibleAsync();
        await Expect(page.Locator($"#dsAclTableWrap [data-gid='{groupId}']").Locator("xpath=ancestor::tr")).ToContainTextAsync("Refresh");
        await page.Locator($"#dsAclTableWrap [data-gid='{groupId}']").ClickAsync();
        await Expect(page.Locator($"#dsAclTableWrap [data-gid='{groupId}']")).ToHaveCountAsync(0);
        await page.Locator($"#dsAclTableWrap [data-uid='{userId}']").ClickAsync();
        await Expect(page.Locator($"#dsAclTableWrap [data-uid='{userId}']")).ToHaveCountAsync(0);
        await page.Locator("#dsAclCloseBtn").ClickAsync();
        var refreshed = await page.RunAndWaitForResponseAsync(() => page.Locator($"[data-action='refresh']{action}").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith($"/api/datasets/{dataset.Id}/refresh", StringComparison.Ordinal));
        Assert.Equal(202, refreshed.Status);
        using var receipt = JsonDocument.Parse(await refreshed.TextAsync());
        Assert.False(receipt.RootElement.GetProperty("triggered").GetBoolean());
        await Expect(page.Locator($"[data-action='refresh']{action}").Locator("xpath=ancestor::tr")).ToContainTextAsync("Stale");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.Datasets.SingleAsync(item => item.Id == dataset.Id);
            Assert.Equal("30m", stored.Ttl);
            Assert.Equal(DatasetAccessLevel.Private, stored.AccessLevel);
            Assert.True(stored.Version > 1);
            Assert.Empty(await db.DatasetAcls.Where(item => item.DatasetId == dataset.Id).ToListAsync());
            Assert.Empty(await db.DatasetUserAcls.Where(item => item.DatasetId == dataset.Id).ToListAsync());
            var actions = await db.AuditLogs.Where(item => item.ResourceType == "Dataset" && item.ResourceId == dataset.Id.ToString()).Select(item => item.Action).ToListAsync();
            Assert.Contains("UPDATE_DATASET", actions);
            Assert.Contains("GRANT_DATASET_PERMISSION", actions);
            Assert.Equal(2, actions.Count(action => action == "REVOKE_DATASET_PERMISSION"));
            Assert.Contains("REFRESH_DATASET", actions);
        }
        await page.Locator($"#datasetsTableWrap [data-action='delete']{action}").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete dataset", Exact = true }).ClickAsync();
        await Expect(page.Locator($"[data-action='view']{action}")).ToHaveCountAsync(0);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.False(await db.Datasets.AnyAsync(item => item.Id == dataset.Id));
            Assert.True(await db.AuditLogs.AnyAsync(item => item.Action == "DELETE_DATASET" && item.ResourceId == dataset.Id.ToString()));
        }
        Assert.False(File.Exists(dataset.ParquetFilePath));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Datasets_OpenedVersionConflictsAndUnknownRefreshReceiptsRequireInspection()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        var dataset = await SeedDatasetAsync();
        await OpenAsync(page);
        var edit = page.Locator($"[data-action='edit'][data-id='{dataset.Id}']");
        await edit.ClickAsync();
        await page.Locator("#ds-ttl").FillAsync("45m");
        await page.EvaluateAsync("""
            async id => { const { datasetsApi } = await import('/js/api.js');
              await datasetsApi.update(id, { accessLevel: 'Public', ttl: '2h' }, 1); }
            """, dataset.Id);
        await page.Locator("#datasetsRefreshBtn").ClickAsync();
        await Expect(edit).ToBeVisibleAsync();
        var conflict = await page.RunAndWaitForResponseAsync(() => page.Locator("#ds-saveBtn").ClickAsync(),
            response => response.Request.Method == "PATCH" && response.Url.EndsWith($"/api/datasets/{dataset.Id}", StringComparison.Ordinal));
        Assert.Equal(409, conflict.Status);
        Assert.Equal("\"1\"", conflict.Request.Headers["if-match"]);
        await Expect(page.Locator("#ds-ttl")).ToHaveValueAsync("45m");
        await Expect(page.Locator("#ds-error")).ToBeVisibleAsync();
        await page.Locator("#ds-cancelBtn").ClickAsync();
        await edit.ClickAsync();
        await Expect(page.Locator("#ds-ttl")).ToHaveValueAsync("2h");
        await page.Locator("#ds-ttl").FillAsync("");
        await page.Locator("#ds-saveBtn").ClickAsync();
        await Expect(page.Locator("#editDatasetForm")).ToBeHiddenAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Null((await db.Datasets.SingleAsync(item => item.Id == dataset.Id)).Ttl);
        }
        var url = $"**/api/datasets/{dataset.Id}/refresh";
        await page.RouteAsync(url, route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = """{"triggered":true,"jobId":"unconfirmed"}""" }));
        var refresh = page.Locator($"[data-action='refresh'][data-id='{dataset.Id}']");
        await refresh.ClickAsync();
        await Expect(page.Locator("#datasetsStatus")).ToContainTextAsync("outcome is unconfirmed");
        await Expect(refresh).ToBeDisabledAsync();
        await page.UnrouteAsync(url);
        await page.Locator("[data-tab='users']").ClickAsync();
        await page.Locator("[data-tab='datasets']").ClickAsync();
        await Expect(refresh).ToBeDisabledAsync();
        await page.Locator("#datasetsRefreshBtn").ClickAsync();
        await Expect(refresh).ToBeEnabledAsync();
        Assert.Empty(session.PageErrors);
    }

    private static async Task OpenAsync(IPage page)
    {
        await page.GotoAsync("/admin.html");
        await page.Locator("[data-tab='datasets']").ClickAsync();
        await Expect(page.Locator("#datasetsTableWrap tbody tr")).Not.ToHaveCountAsync(0);
    }

    private async Task<Dataset> SeedDatasetAsync()
    {
        var name = $"browser_dataset_{Guid.NewGuid():N}";
        var path = Path.Combine(fixture.Factory.TempDir, "datasets", name + ".parquet");
        var plain = Path.Combine(fixture.Factory.TempDir, name + ".plain.parquet");
        await using var writer = new ETL_SQL.Connectors.Parquet.ParquetDataSource(SystemExecutionContext.Instance, plain);
        var batch = new ETL_SQL.Data.DataTable();
        batch.ColumnNames.AddRange(["v", "label", "nullable"]);
        for (var i = 1; i <= 30; i++)
        {
            var row = new ETL_SQL.Data.Row();
            row["v"] = (decimal)i;
            row["label"] = $"row{i}";
            row["nullable"] = i == 30 ? null : (decimal?)i;
            await batch.AddRowAsync(row);
        }
        await writer.WriteBatches(new[] { batch }.ToAsyncEnumerable(), false, CancellationToken.None);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<PortalConfig>();
        Assert.False(string.IsNullOrWhiteSpace(config.Dataset.AtRestKey));
        await new EncryptionOptions(new Dictionary<string, string> { ["ENCRYPT"] = "PASSWORD", ["PASSWORD"] = config.Dataset.AtRestKey! })
            .EncryptFileAsync(plain, path, CancellationToken.None);
        var dataset = new Dataset
        {
            Name = name,
            FolderPath = "/",
            ParquetFilePath = path,
            SourceQuery = "SELECT 1;",
            AccessLevel = DatasetAccessLevel.Public,
            EncryptionMode = DatasetEncryptionMode.Password,
            RowCount = 30,
            AtRestKeyVersion = config.Dataset.AtRestKeyVersion,
            LastRefresh = DateTime.UtcNow,
            ColumnSchema = """[{"name":"v","type":"DECIMAL"},{"name":"label","type":"NVARCHAR"},{"name":"nullable","type":"DECIMAL"}]"""
        };
        db.Datasets.Add(dataset);
        await db.SaveChangesAsync();
        return dataset;
    }
}

public sealed class DatasetAdminFixture() : PortalBrowserFixture(new PortalBrowserFactory());

[CollectionDefinition(Name)]
public sealed class DatasetAdminCollection : ICollectionFixture<DatasetAdminFixture>
{
    public const string Name = "Portal dataset administration";
}
