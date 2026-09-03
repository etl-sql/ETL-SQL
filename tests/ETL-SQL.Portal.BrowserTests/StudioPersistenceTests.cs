using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

[Trait("Category", "Browser")]
[Collection(PortalBrowserCollection.Name)]
public sealed class StudioPersistenceTests(PortalBrowserFixture fixture)
{
    private const string InitialScript = """
        SET REPORT TITLE = 'Studio Save';
        SELECT 'before' AS Value INTO #data;
        CREATE VISUAL Result AS TABLE (SOURCE = #data, MAPPINGS (Value = Value));
        CREATE PAGE Main AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = Result));
        """;

    private const string SavedScript = """
        SET REPORT TITLE = 'Studio Save';
        -- This comment and spacing must survive the catalog save exactly.
        SELECT  'after'  AS Value INTO #data;
        CREATE VISUAL Result AS TABLE (SOURCE = #data, MAPPINGS (Value = Value));
        CREATE PAGE Main AS DASHBOARD(STRUCTURE = 'A', MAP ('A' = Result));
        """;

    [Fact]
    public async Task AuthenticatedPortal_AuthoringJourney_PersistsAcrossReloadAndClose()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var connectionAlias = $"studio_sample_{Guid.NewGuid():N}";
        var createConnectionStatus = await page.EvaluateAsync<int>(
            """
            async alias => {
                const { auth } = await import('/js/api.js');
                const response = await fetch(`/api/admin/connections/${encodeURIComponent(alias)}`, {
                    method: 'PUT',
                    headers: {
                        Authorization: `Bearer ${auth.getToken()}`,
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ connectorType: 'MOCKDB', options: {} })
                });
                return response.status;
            }
            """, connectionAlias);
        Assert.Equal(204, createConnectionStatus);
        var folderId = await CreateWritableFolderAsync();
        var report = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { studioApi } = await import('/js/api.js');
                return studioApi.createReport(request);
            }
            """,
            new
            {
                folderId,
                name = $"Studio Journey {Guid.NewGuid():N}",
                scriptText = $"""
                    SELECT UserID, UserName INTO #users FROM {connectionAlias}.Users;
                    CREATE VISUAL UsersTable AS TABLE (
                      SOURCE = #users,
                      MAPPINGS (USER_ID = UserID, USER_NAME = UserName)
                    );
                    CREATE PAGE Main AS DASHBOARD (
                      LAYOUT (STRUCTURE = 'A', MAP ('A' = UsersTable))
                    );
                    """
            });
        var reportId = report.GetProperty("id").GetInt32();

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(page);
        await page.Locator("[data-activity='catalog']").ClickAsync();
        var schemaRequest = page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/designer/schema", StringComparison.Ordinal)
            && response.Url.Contains(Uri.EscapeDataString(connectionAlias), StringComparison.Ordinal));
        await page.Locator($"[data-connection='{connectionAlias}']").ClickAsync();
        var schemaResponse = await schemaRequest;
        if (schemaResponse.Status != 200)
        {
            var details = await page.EvaluateAsync<JsonElement>(
                """
                async alias => {
                    const { auth } = await import('/js/api.js');
                    const response = await fetch(`/api/designer/schema?connection=${encodeURIComponent(alias)}`, {
                        headers: { Authorization: `Bearer ${auth.getToken()}` }
                    });
                    return { status: response.status, body: await response.text() };
                }
                """, connectionAlias);
            Assert.Fail($"Schema request returned {details.GetProperty("status").GetInt32()}: {details.GetProperty("body").GetString()}");
        }
        await page.Locator("[data-table='Users']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].studioContext.snapshot?.rowCount > 0");

        await page.EvaluateAsync("() => { window.__STUDIO__.state.selectedVisualId = 'UsersTable'; }");
        await page.Locator("[data-field='UserName']").ClickAsync();
        await page.Locator("[data-filter-dialog-apply]").ClickAsync();
        var firstValue = page.Locator("[data-filter-value='UserName']").First;
        await firstValue.CheckAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.editorInstance.getValue().includes('ETL-SQL-STUDIO-FILTER')");

        const string editMarker = "-- production Portal browser journey";
        await page.EvaluateAsync(
            "marker => window.__STUDIO__.state.editorInstance.setValue(window.__STUDIO__.state.editorInstance.getValue() + `\n${marker}\n`)",
            editMarker);
        await page.EvaluateAsync("() => window.__STUDIO__.state.editorInstance.gotoLine(1, 1)");
        await page.Locator("[data-action='run-selected']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].studioContext.runActive === false");
        var runTrace = await page.EvaluateAsync<JsonElement>(
            "() => window.__STUDIO__.state.documents[0].studioContext.resultsTrace");
        Assert.True(runTrace.EnumerateArray().Any(item => item.GetProperty("type").GetString() == "results"),
            runTrace.ToString());

        await page.Locator("[data-action='save']").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0].isDirty === false");
        await page.ReloadAsync();
        await WaitForStudioAsync(page);
        Assert.Contains(editMarker,
            await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()"),
            StringComparison.Ordinal);

        await page.Locator(".etlsql-tab-close").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents.length === 0");
        Assert.False(await HasEditLeaseAsync(reportId));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task CatalogHome_OpenAndClose_CarriesIdentityAndEditLease()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var folderId = await CreateWritableFolderAsync();
        var reportName = $"Studio Catalog {Guid.NewGuid():N}";
        var report = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { studioApi } = await import('/js/api.js');
                return studioApi.createReport(request);
            }
            """,
            new { folderId, name = reportName, scriptText = InitialScript });
        var reportId = report.GetProperty("id").GetInt32();

        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(page);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Catalog Reports" })).ToBeVisibleAsync();
        await Expect(page.GetByText(reportName + ".rptsql", new() { Exact = true })).ToBeVisibleAsync();
        await page.ClickAsync($"[data-open-report='{reportId}'][data-open-proj='split']");
        await page.WaitForFunctionAsync($"() => window.__STUDIO__.state.documents.some(doc => doc.reportId === {reportId})");

        var opened = await page.EvaluateAsync<JsonElement>(
            """
            id => {
                const studio = window.__STUDIO__;
                const doc = studio.state.documents.find(item => item.reportId === id);
                return {
                    reportId: doc.reportId,
                    folderId: doc.folderId,
                    version: doc.version,
                    leaseAcquired: doc.lease?.acquired,
                    canSave: doc.canSave,
                    canPublish: studio.state.capabilities.has('ReportPublish')
                };
            }
            """, reportId);
        Assert.Equal(reportId, opened.GetProperty("reportId").GetInt32());
        Assert.Equal(folderId, opened.GetProperty("folderId").GetInt32());
        Assert.True(opened.GetProperty("version").GetInt64() > 0);
        Assert.True(opened.GetProperty("leaseAcquired").GetBoolean());
        Assert.True(opened.GetProperty("canSave").GetBoolean());
        Assert.True(opened.GetProperty("canPublish").GetBoolean());
        Assert.True(await HasEditLeaseAsync(reportId));

        await page.ClickAsync(".etlsql-tab-close");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents.length === 0");
        Assert.False(await HasEditLeaseAsync(reportId));

        var createdName = $"Studio Created {Guid.NewGuid():N}";
        // The home screen no longer offers one generic "report": the choice of dashboard or
        // paginated is made up front, which is the same question `switchDoc` otherwise has to stop
        // and ask later. This picks the plain dashboard card rather than the sample-seeded one, so
        // the report it creates is empty and the lease assertions below are about the lease.
        await page.ClickAsync("[data-create-from-home='dashboard']:not([data-seed-sample])");
        await page.FillAsync("[data-catalog-report-name]", createdName);
        await page.SelectOptionAsync("[data-catalog-report-folder]", folderId.ToString());
        await page.ClickAsync("[data-catalog-create-confirm]");
        await page.WaitForFunctionAsync(
            "name => window.__STUDIO__.state.documents.some(doc => doc.name === `${name}.rptsql`)", createdName);
        var createdReportId = await page.EvaluateAsync<int>(
            "name => window.__STUDIO__.state.documents.find(doc => doc.name === `${name}.rptsql`).reportId", createdName);
        Assert.True(await HasEditLeaseAsync(createdReportId));
        await page.ClickAsync(".etlsql-tab-close");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents.length === 0");
        Assert.False(await HasEditLeaseAsync(createdReportId));
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task CatalogReport_SaveReloadAndConflict_PreserveDocumentIntegrity()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);

        var folderId = await CreateWritableFolderAsync();
        var report = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { studioApi } = await import('/js/api.js');
                return studioApi.createReport(request);
            }
            """,
            new { folderId, name = $"Studio Save {Guid.NewGuid():N}", scriptText = InitialScript });
        var reportId = report.GetProperty("id").GetInt32();

        await page.GotoAsync($"/studio.html?reportId={reportId}");
        await WaitForStudioAsync(page);

        await page.EvaluateAsync("script => window.__STUDIO__.state.editorInstance.setValue(script)", SavedScript);
        await Expect(page.Locator(".etlsql-tab-dirty")).ToBeVisibleAsync();
        await page.ClickAsync("[data-action='save']");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0]?.isDirty === false");

        await page.ReloadAsync();
        await WaitForStudioAsync(page);
        var reloaded = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        // Endings normalised: `reloaded` is the CodeMirror buffer, which is LF by construction, and
        // `SavedScript` is a raw string literal whose endings come from the checkout. Comparing the
        // raw bytes asserted a property of the developer's `core.autocrlf` setting. What the
        // *file* is written with is asserted in <see cref="StudioLineEndingTests"/>.
        Assert.Equal(NormalizeEndings(SavedScript), NormalizeEndings(reloaded));

        var current = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const doc = window.__STUDIO__.state.documents[0];
                return { version: doc.version, sourceRevision: doc.sourceRevision };
            }
            """);
        var externalSave = await page.EvaluateAsync<JsonElement>(
            """
            async request => {
                const { auth } = await import('/js/api.js');
                const response = await fetch('/api/designer/save', {
                    method: 'POST',
                    headers: {
                        'Authorization': `Bearer ${auth.getToken()}`,
                        'Content-Type': 'application/json',
                        'If-Match': `"${request.version}"`
                    },
                    body: JSON.stringify({
                        reportId: request.reportId,
                        scriptText: request.scriptText,
                        baseRevision: request.sourceRevision
                    })
                });
                return { status: response.status, body: await response.text() };
            }
            """,
            new
            {
                reportId,
                version = current.GetProperty("version").GetInt64(),
                sourceRevision = current.GetProperty("sourceRevision").ValueKind == JsonValueKind.Null
                    ? null
                    : current.GetProperty("sourceRevision").GetString(),
                scriptText = SavedScript + "\n-- external writer\n"
            });
        Assert.Equal(200, externalSave.GetProperty("status").GetInt32());

        await page.EvaluateAsync("script => window.__STUDIO__.state.editorInstance.setValue(script)", SavedScript + "\n-- stale local writer\n");
        await page.ClickAsync("[data-action='save']");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.documents[0]?.isDirty === true");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));

        await page.ClickAsync(".etlsql-tab-close");
        await Expect(page.Locator(".etlsql-feedback-dialog")).ToBeVisibleAsync();
        await page.ClickAsync(".etlsql-feedback-btn-primary");
        await Expect(page.Locator(".etlsql-feedback-dialog")).ToBeHiddenAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__STUDIO__.state.documents.length"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task SaveEncryption_HidesPlaintextAndProducesEngineCompatibleCiphertext()
    {
        const string secret = "Studio-P0-Secret!";
        const string passphrase = "Studio-P0-Passphrase!";

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(page);

        await page.EvaluateAsync(
            """
            script => {
                const studio = window.__STUDIO__;
                studio.state.documents.push({
                    id: 'secret-test', path: 'secret-test.rptsql', name: 'secret-test.rptsql',
                    content: script, isDirty: true, projection: 'code'
                });
                return studio.switchDoc('secret-test');
            }
            """,
            // The page mode is declared for the same reason as in SwitchingDocuments below: a
            // `.rptsql` that does not say what kind of report it is makes `switchDoc` stop and ask,
            // and an evaluate that never answers hangs instead of failing.
            $"""
            CREATE CONNECTION c AS MSSQL(PASSWORD = '{secret}');
            CREATE PAGE [Main] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );
            """);

        await page.ClickAsync("[data-action='save']");
        await Expect(page.Locator("[data-encrypt-passphrase]")).ToBeVisibleAsync();
        Assert.DoesNotContain(secret, await page.Locator("[data-modal-box]").InnerTextAsync(), StringComparison.Ordinal);
        Assert.False(await page.Locator("[data-modal-box]").EvaluateAsync<bool>(
            "(element, value) => element.innerHTML.includes(value)", secret));

        await page.FillAsync("[data-encrypt-passphrase]", passphrase);
        await page.ClickAsync("[data-modal-encrypt]");
        await page.WaitForFunctionAsync("() => window.__STUDIO__.state.editorInstance.getValue().includes('ENC:')");

        var encryptedScript = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.DoesNotContain(secret, encryptedScript, StringComparison.Ordinal);
        var start = encryptedScript.IndexOf("ENC:", StringComparison.Ordinal);
        var end = encryptedScript.IndexOf('\'', start);
        var ciphertext = encryptedScript[start..end];
        Assert.Equal(secret, CryptoUtils.Decrypt(ciphertext, passphrase));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__STUDIO__.state.documents[0].isDirty"));

        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task SwitchingDocuments_RestoresEachDocumentsDataAndResultContext()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(page);

        // Each document declares its page mode. An empty `.rptsql` does not say which kind of report
        // it is, so switching to one asks — and `switchDoc` awaits that answer, which no automated
        // caller gives. The test then hung rather than failing, for nine minutes, over a question
        // that has nothing to do with what it is checking.
        var restored = await page.EvaluateAsync<JsonElement>(
            """
            async () => {
                const studio = window.__STUDIO__;
                const declared = name => `CREATE PAGE [${name}] AS DASHBOARD ( LAYOUT ( STRUCTURE = '.' ) );`;
                studio.state.documents.push(
                    { id: 'context-a', path: 'context-a.rptsql', name: 'context-a.rptsql', content: declared('A'), isDirty: false, projection: 'code' },
                    { id: 'context-b', path: 'context-b.rptsql', name: 'context-b.rptsql', content: declared('B'), isDirty: false, projection: 'code' });

                await studio.switchDoc('context-a');
                const first = studio.state.documents.find(doc => doc.id === 'context-a').studioContext;
                first.snapshot = { source: 'alpha.orders', columns: ['region'], rowCount: 1, rows: [{ region: 'North' }] };
                first.activeFilters.region = { kind: 'values', values: ['North'] };
                first.filterFields.push('region');
                first.selectedSource = { connection: 'alpha', table: 'orders' };
                studio.setDocumentTrace(studio.state.documents.find(doc => doc.id === 'context-a'), [
                    { type: 'results', columns: ['note'], rows: [{ note: 'alpha result' }] },
                ]);

                await studio.switchDoc('context-b');
                const second = studio.state.documents.find(doc => doc.id === 'context-b').studioContext;
                second.snapshot = { source: 'beta.customers', columns: ['country'], rowCount: 1, rows: [{ country: 'CA' }] };
                second.activeFilters.country = { kind: 'values', values: ['CA'] };
                second.selectedSource = { connection: 'beta', table: 'customers' };
                studio.setDocumentTrace(studio.state.documents.find(doc => doc.id === 'context-b'), [
                    { type: 'results', columns: ['note'], rows: [{ note: 'beta result' }] },
                ]);

                await studio.switchDoc('context-a');
                return {
                    source: first.snapshot.source,
                    filterFields: first.filterFields,
                    filterValues: first.activeFilters.region.values,
                    connection: first.selectedSource.connection,
                    results: document.querySelector('[data-results-host]').textContent,
                    restoredTrace: JSON.stringify(first.resultsTrace),
                    secondSource: second.snapshot.source,
                    contextsAreDistinct: first !== second
                };
            }
            """);

        Assert.Equal("alpha.orders", restored.GetProperty("source").GetString());
        Assert.Equal("region", restored.GetProperty("filterFields")[0].GetString());
        Assert.Equal("North", restored.GetProperty("filterValues")[0].GetString());
        Assert.Equal("alpha", restored.GetProperty("connection").GetString());
        // Results now live in the shared Results/Messages/Performance panel as a per-document trace
        // rather than an HTML blob, so assert the restored trace and that it reached the panel.
        Assert.Contains("alpha result", restored.GetProperty("restoredTrace").GetString(), StringComparison.Ordinal);
        Assert.Contains("alpha result", restored.GetProperty("results").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("beta result", restored.GetProperty("results").GetString(), StringComparison.Ordinal);
        Assert.Equal("beta.customers", restored.GetProperty("secondSource").GetString());
        Assert.True(restored.GetProperty("contextsAreDistinct").GetBoolean());
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task VisualMutations_UseCanonicalPatcherAndPreserveHandAuthoredSql()
    {
        const string script = """
            -- preserve this preparation exactly
            WITH source_rows AS (SELECT Region, Amount FROM #raw)
            SELECT Region, Amount INTO #sales FROM source_rows;

            CREATE VISUAL SalesBar AS BAR (
                TITLE = 'Sales',
                SOURCE = #sales,
                MAPPINGS (X = Region, Y = Amount)
            );

            CREATE PAGE [Main] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesBar))
            );
            """;
        var protectedSql = script[..script.IndexOf("CREATE VISUAL", StringComparison.Ordinal)];

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(page);

        var result = await page.EvaluateAsync<JsonElement>(
            """
            async script => {
                const studio = window.__STUDIO__;
                studio.state.documents.push({
                    id: 'canonical-mutation', path: 'canonical-mutation.rptsql', name: 'canonical-mutation.rptsql',
                    content: script, isDirty: false, projection: 'split'
                });
                await studio.switchDoc('canonical-mutation');
                const doc = studio.state.documents.find(item => item.id === 'canonical-mutation');
                doc.studioContext.snapshot = {
                    source: '#sales', columns: ['Region', 'Amount'], rowCount: 1,
                    rows: [{ Region: 'North', Amount: 10 }]
                };

                const operations = {};
                operations.option = await studio.surgicalPatchVisualOption('SalesBar', 'TITLE', 'Regional Sales');
                operations.mapping = await studio.surgicalPatchVisualMapping('SalesBar', 'Y', 'Revenue');
                operations.duplicate = await studio.duplicateVisual('SalesBar');
                operations.remove = await studio.deleteVisual('SalesBar_copy');
                operations.add = await studio.addVisualToCanvas('TABLE');
                studio.state.selectedVisualId = 'SalesBar';
                doc.studioContext.activeFilters.Region = {
                    id: 'region', kind: 'categorical', values: ['North'],
                    scope: 'visual', target: 'SalesBar'
                };
                operations.filter = await studio.persistFilter('Region');
                const filtered = studio.state.editorInstance.getValue();
                operations.slicer = await studio.promoteFilterToSlicer('Region');

                const patched = studio.state.editorInstance.getValue();
                const { auth } = await import('/js/api.js');
                const response = await fetch('/api/designer/parse', {
                    method: 'POST',
                    headers: { Authorization: `Bearer ${auth.getToken()}`, 'Content-Type': 'application/json' },
                    body: JSON.stringify({ script: patched })
                });
                const parsed = await response.json();
                return {
                    patched,
                    filtered,
                    parseStatus: response.status,
                    parseError: parsed.error,
                    parameters: parsed.designState?.parameters,
                    visuals: parsed.designState?.pages?.flatMap(page => page.visuals || []),
                    operations
                };
            }
            """, script);

        var patched = result.GetProperty("patched").GetString()!;
        var filtered = result.GetProperty("filtered").GetString()!;
        Assert.All(result.GetProperty("operations").EnumerateObject(), operation =>
            Assert.NotEqual(JsonValueKind.Null, operation.Value.ValueKind));
        // Compared with the line endings normalised on both sides. The claim is that the
        // hand-authored preparation survives the patcher character for character; which endings it
        // has is decided by the checkout — `core.autocrlf` gives this raw string CRLF on Windows and
        // LF on CI — and by CodeMirror, which normalises every buffer it holds. Asserting the raw
        // bytes here made the test pass on one platform and fail on the other while the product
        // behaved identically on both. The endings a *file* is written with are a real claim and are
        // asserted where they are actually decided, in
        // <see cref="StudioLineEndingTests"/>.
        Assert.Contains(NormalizeEndings(protectedSql), NormalizeEndings(patched), StringComparison.Ordinal);
        Assert.Contains("TITLE = 'Regional Sales'", patched, StringComparison.Ordinal);
        Assert.Contains("MAPPINGS (X = Region, Y = Revenue)", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("SalesBar_copy", patched, StringComparison.Ordinal);
        Assert.Contains("ETL-SQL-STUDIO-FILTER", filtered, StringComparison.Ordinal);
        Assert.Contains("Region = 'North'", filtered, StringComparison.Ordinal);
        Assert.Contains("DECLARE @selected_region VARCHAR = 'North' INPUT;", patched, StringComparison.Ordinal);
        Assert.Contains("SELECT DISTINCT Region", patched, StringComparison.Ordinal);
        Assert.Contains("FROM #sales", patched, StringComparison.Ordinal);
        Assert.Contains("ORDER BY Region", patched, StringComparison.Ordinal);
        Assert.Contains("ACTIONS (ON_CHANGE = SET_PARAMETER(@selected_region, Region))", patched, StringComparison.Ordinal);
        Assert.Contains("@selected_region = 'All' OR Region = @selected_region", patched, StringComparison.Ordinal);
        Assert.Equal(200, result.GetProperty("parseStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("parseError").ValueKind);
        Assert.Contains(result.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "@selected_region");
        Assert.Contains(result.GetProperty("visuals").EnumerateArray(),
            visual => visual.GetProperty("type").GetString() == "SLICER");
        Assert.Contains(result.GetProperty("visuals").EnumerateArray(),
            visual => visual.GetProperty("type").GetString() == "TABLE");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task FilterScope_SwitchesBetweenDatasetWhereAndVisualSource()
    {
        const string script = """
            CREATE DATASET &sales AS (
                SELECT Region, Amount FROM #sales WHERE IsActive = 1
            );

            CREATE VISUAL SalesBar AS BAR (
                SOURCE = &sales,
                MAPPINGS (X = Region, Y = Amount)
            );

            CREATE PAGE [Main] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesBar))
            );
            """;

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(page);

        var result = await page.EvaluateAsync<JsonElement>(
            """
            async script => {
                const studio = window.__STUDIO__;
                studio.state.documents.push({
                    id: 'filter-scope', path: 'filter-scope.rptsql', name: 'filter-scope.rptsql',
                    content: script, isDirty: false, projection: 'split'
                });
                await studio.switchDoc('filter-scope');
                const doc = studio.state.documents.find(item => item.id === 'filter-scope');
                doc.studioContext.snapshot = {
                    source: '&sales', columns: ['Region', 'Amount'], rowCount: 2,
                    rows: [{ Region: 'North', Amount: 10 }, { Region: 'South', Amount: 20 }]
                };
                doc.studioContext.activeFilters.Region = {
                    id: 'region', kind: 'categorical', values: ['North'],
                    scope: 'dataset', target: '&sales'
                };
                const datasetTarget = await studio.persistFilter('Region');
                const datasetScript = studio.state.editorInstance.getValue();

                const previous = { ...doc.studioContext.activeFilters.Region };
                studio.state.selectedVisualId = 'SalesBar';
                doc.studioContext.activeFilters.Region.scope = 'visual';
                doc.studioContext.activeFilters.Region.target = 'SalesBar';
                await studio.persistFilter('Region', previous);
                const visualTarget = await studio.persistFilter('Region');
                const visualScript = studio.state.editorInstance.getValue();
                return { datasetTarget, visualTarget, datasetScript, visualScript };
            }
            """, script);

        Assert.Equal("&sales", result.GetProperty("datasetTarget").GetString());
        Assert.Equal("SalesBar", result.GetProperty("visualTarget").GetString());
        var datasetScript = result.GetProperty("datasetScript").GetString()!;
        Assert.Contains("WHERE IsActive = 1", datasetScript, StringComparison.Ordinal);
        Assert.Contains("Region = 'North'", datasetScript, StringComparison.Ordinal);
        Assert.Contains("SOURCE = &sales", datasetScript, StringComparison.Ordinal);

        var visualScript = result.GetProperty("visualScript").GetString()!;
        var datasetEnd = visualScript.IndexOf("CREATE VISUAL", StringComparison.Ordinal);
        Assert.DoesNotContain("ETL-SQL-STUDIO-FILTER", visualScript[..datasetEnd], StringComparison.Ordinal);
        Assert.Contains("SOURCE = (SELECT * FROM &sales", visualScript, StringComparison.Ordinal);
        Assert.Contains("Region = 'North'", visualScript, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task NumericAndDateFilters_PromoteToTypedParameterControls()
    {
        const string script = """
            CREATE VISUAL SalesTable AS TABLE (
                SOURCE = #sales,
                MAPPINGS (AMOUNT = Amount, ORDER_DATE = OrderDate)
            );

            CREATE PAGE [Main] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = SalesTable))
            );
            """;

        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(page);

        var patched = await page.EvaluateAsync<string>(
            """
            async script => {
                const studio = window.__STUDIO__;
                studio.state.documents.push({
                    id: 'typed-controls', path: 'typed-controls.rptsql', name: 'typed-controls.rptsql',
                    content: script, isDirty: false, projection: 'split'
                });
                await studio.switchDoc('typed-controls');
                const doc = studio.state.documents.find(item => item.id === 'typed-controls');
                doc.studioContext.snapshot = {
                    source: '#sales',
                    columns: [{ name: 'Amount', type: 'DECIMAL' }, { name: 'OrderDate', type: 'DATE' }],
                    rowCount: 2,
                    rows: [
                        { Amount: 10, OrderDate: '2026-08-01' },
                        { Amount: 20, OrderDate: '2026-08-28' }
                    ]
                };
                studio.state.selectedVisualId = 'SalesTable';
                await studio.promoteFilterToSlicer('Amount');
                studio.state.selectedVisualId = 'SalesTable';
                await studio.promoteFilterToSlicer('OrderDate');
                return studio.state.editorInstance.getValue();
            }
            """, script);

        Assert.Contains("DECLARE @selected_amount DECIMAL = 20 INPUT;", patched, StringComparison.Ordinal);
        Assert.Contains("CREATE VISUAL amount_slicer AS SLIDER", patched, StringComparison.Ordinal);
        Assert.Contains("SET_PARAMETER(@selected_amount, value)", patched, StringComparison.Ordinal);
        Assert.Contains("Amount <= @selected_amount", patched, StringComparison.Ordinal);
        Assert.Contains("DECLARE @selected_orderdate DATE = '2026-08-01' INPUT;", patched, StringComparison.Ordinal);
        Assert.Contains("CREATE VISUAL orderdate_slicer AS DATEPICKER", patched, StringComparison.Ordinal);
        Assert.Contains("SET_PARAMETER(@selected_orderdate, value)", patched, StringComparison.Ordinal);
        Assert.Contains("OrderDate >= @selected_orderdate", patched, StringComparison.Ordinal);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task ConnectionWizard_UsesProductionRegistryAndInsertsParserValidMockDb()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await fixture.SignInAsync(page);
        await page.GotoAsync("/studio.html");
        await WaitForStudioAsync(page);

        await page.EvaluateAsync(
            """
            async () => {
                const studio = window.__STUDIO__;
                studio.state.documents.push({
                    id: 'mockdb-script', path: 'mockdb-script.etlsql', name: 'mockdb-script.etlsql',
                    content: 'SELECT 1 AS Value;', isDirty: false, projection: 'code'
                });
                await studio.switchDoc('mockdb-script');
            }
            """);
        await page.Locator("[data-activity='catalog']").ClickAsync();
        await page.Locator(".etlsql-studio-sidebar [data-action='wizard']").ClickAsync();
        await page.Locator("button[data-cat='testdata']").ClickAsync();

        var mockDb = page.Locator("button[data-type='MOCKDB']");
        await mockDb.WaitForAsync();
        await mockDb.ClickAsync();
        await page.Locator("#etlsql-cw-alias-input").FillAsync("sample_data");
        Assert.Equal("CREATE CONNECTION sample_data AS MOCKDB();",
            (await page.Locator(".etlsql-cw-sql-box").InnerTextAsync()).Trim());
        await page.Locator("#etlsql-cw-submit-btn").ClickAsync();

        var script = await page.EvaluateAsync<string>("() => window.__STUDIO__.state.editorInstance.getValue()");
        Assert.StartsWith("CREATE CONNECTION sample_data AS MOCKDB();", script, StringComparison.Ordinal);
        var parseError = await page.EvaluateAsync<string?>(
            """
            async script => {
                const { auth } = await import('/js/api.js');
                const response = await fetch('/api/designer/parse', {
                    method: 'POST',
                    headers: { Authorization: `Bearer ${auth.getToken()}`, 'Content-Type': 'application/json' },
                    body: JSON.stringify({ script })
                });
                if (!response.ok) return `HTTP ${response.status}`;
                return (await response.json()).error;
            }
            """, script);
        Assert.Null(parseError);
        Assert.Empty(session.PageErrors);
    }

    /// <summary>Line endings belong to the checkout and to CodeMirror, not to the claim under test.</summary>
    private static string NormalizeEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private async Task<int> CreateWritableFolderAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var adminId = await db.Users.Where(user => user.UserName == PortalBrowserFixture.AdminUsername)
            .Select(user => user.Id)
            .SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folder = new Folder
        {
            Name = $"Studio {suffix}",
            Path = $"/Studio-{suffix}",
            OwnerId = adminId
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder.Id;
    }

    private async Task<bool> HasEditLeaseAsync(int reportId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.Reports.Where(report => report.Id == reportId)
            .Select(report => report.EditSessionUserId != null && report.EditSessionExpiresAtUtc > DateTime.UtcNow)
            .SingleAsync();
    }

    private static async Task WaitForStudioAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => Boolean(window.__STUDIO__)", null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
    }
}
