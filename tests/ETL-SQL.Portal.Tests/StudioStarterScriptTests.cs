using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Services;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Parses the MOCKDB starter scripts that Studio Home's "Start with sample data" seeds.
///
/// <para>These exist because a first session was otherwise a dead end: the visual palette stays
/// disabled until a data sample exists, and a sample needs a connection a newcomer does not have.
/// They are the first ETL-SQL a new author ever reads, so a starter that does not parse would teach
/// the wrong syntax and break the canvas on arrival — and they live in a JavaScript string literal
/// where no compiler would notice.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioStarterScriptTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Extracts the starter scripts from their canonical contracts module.</summary>
    public static TheoryData<string, string> Starters()
    {
        var studioJs = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "studio-contracts.js"));

        var table = Regex.Match(
            studioJs,
            @"const\s+STUDIO_STARTER_SCRIPTS\s*=\s*Object\.freeze\(\{(?<body>.*?)\}\);",
            RegexOptions.Singleline);
        Assert.True(table.Success, "STUDIO_STARTER_SCRIPTS was not found in studio-contracts.js.");

        var data = new TheoryData<string, string>();
        foreach (Match entry in Regex.Matches(
            table.Groups["body"].Value,
            @"(?<name>\w+):\s*`(?<script>[^`]*)`",
            RegexOptions.Singleline))
        {
            data.Add(entry.Groups["name"].Value, entry.Groups["script"].Value);
        }

        Assert.True(data.Count >= 3, "Expected a starter script per Studio Home creation action.");
        return data;
    }

    [Theory]
    [MemberData(nameof(Starters))]
    public void StarterScript_ParsesWithoutError(string name, string script)
    {
        var parsed = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        var errors = parsed.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"line {diagnostic.Line}: {diagnostic.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Starter script '{name}' does not parse: {string.Join("; ", errors)}");
    }

    [Theory]
    [MemberData(nameof(Starters))]
    public void StarterScript_UsesOnlyTheBuiltInSampleConnector(string name, string script)
    {
        // The whole point is a first run with no external dependency; a starter that reached for a
        // real database would reintroduce the dead end it exists to remove.
        Assert.Contains("MOCKDB()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PASSWORD", script, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void DashboardAndPaginatedWorkflowTemplates_AreParserValidAndDeclareTheirMode()
    {
        var studioJs = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "studio-contracts.js"));
        var table = Regex.Match(
            studioJs,
            @"const\s+REPORT_WORKFLOW_TEMPLATES\s*=\s*Object\.freeze\(\{(?<body>.*?)\}\);",
            RegexOptions.Singleline);
        Assert.True(table.Success, "REPORT_WORKFLOW_TEMPLATES was not found in studio-contracts.js.");

        var templates = Regex.Matches(
                table.Groups["body"].Value,
                @"(?<name>dashboard|paginated):\s*`(?<script>[^`]*)`",
                RegexOptions.Singleline)
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["script"].Value);
        Assert.Equal(2, templates.Count);

        foreach (var (name, script) in templates)
        {
            var parsed = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
            var errors = parsed.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0, $"Workflow template '{name}' does not parse: {string.Join("; ", errors.Select(error => error.Message))}");
            Assert.Contains($"AS {name.ToUpperInvariant()}", script, StringComparison.Ordinal);
        }

        Assert.Contains("PRINT_LAYOUT", templates["paginated"], StringComparison.Ordinal);
        Assert.DoesNotContain("PRINT_LAYOUT", templates["dashboard"], StringComparison.Ordinal);
    }
}

/// <summary>
/// Verifies MOCKDB reaches Studio's Connection Wizard as a Test Data connector.
///
/// <para>MOCKDB is the zero-dependency on-ramp: Studio Home's "Start with sample data" seeds a
/// script that uses it, and it is the only connector a new author can pick without provisioning a
/// database. The wizard groups it under **Test Data** by matching `connectorType == "MOCKDB"`
/// against whatever the connector registry returns from <c>/api/connectors/schema</c>, and it falls
/// back to a built-in connector list when that request fails — a fallback that has no MOCKDB in it.
/// So "is MOCKDB registered?" and "does it carry a schema descriptor?" are the two things that
/// decide whether the Test Data category is empty.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class ConnectionWizardTestDataTests
{
    [Fact]
    public void ProductionConnectorRegistration_IncludesMockDb()
    {
        // Mirrors the production registration in
        // ETL-SQL.Orchestrator/DependencyInjectionExtensions.cs, which registers MockDbConnector
        // alongside the real connectors.
        var registry = new ETL_SQL.Data.ConnectorRegistry();
        registry.Register(new ETL_SQL.Connectors.MockDb.MockDbConnector());

        var schemas = registry.GetAllConnectorSchemas().ToList();

        var mockDb = schemas.FirstOrDefault(schema =>
            string.Equals(schema.ConnectorType, "MOCKDB", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mockDb);
    }

    [Fact]
    public void MockDbSchema_MatchesTheWizardsTestDataCategoryRule()
    {
        // The wizard's rule is `type === 'MOCKDB'` after upper-casing (connection-wizard.js,
        // isConnectorInCategory). Asserting the descriptor's casing keeps that match honest.
        // GetSchemaDescriptor is a default interface method, so it needs an interface-typed reference.
        ETL_SQL.Data.IConnector connector = new ETL_SQL.Connectors.MockDb.MockDbConnector();
        var schema = connector.GetSchemaDescriptor();

        Assert.Equal("MOCKDB", schema.ConnectorType.ToUpperInvariant());
        Assert.Equal("MOCKDB", connector.Name);
    }

    /// <summary>Connector types the wizard can derive an alias from, read from its fallback list.</summary>
    public static TheoryData<string> WizardConnectorTypes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var wizard = File.ReadAllText(Path.Combine(dir!.FullName,
            "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "connection-wizard.js"));

        var data = new TheoryData<string>();
        foreach (Match match in Regex.Matches(wizard, @"connectorType:\s*'(?<type>[A-Za-z0-9_]+)'"))
            data.Add(match.Groups["type"].Value);

        Assert.True(data.Count > 5, "Expected the wizard's fallback connector list to be found.");
        return data;
    }

    [Theory]
    [MemberData(nameof(WizardConnectorTypes))]
    public void SuggestedAliasForEveryConnectorType_IsParserValid(string connectorType)
    {
        // The wizard prefills the alias by lower-casing the connector type. That is only safe while
        // no connector type is also a reserved word — a real trap here, since `SAMPLE` is reserved
        // and `CREATE CONNECTION sample AS MOCKDB();` does not parse. If a future connector collides,
        // this fails rather than shipping a wizard whose default suggestion is unusable.
        var alias = connectorType.ToLowerInvariant();
        var script = $"CREATE CONNECTION {alias} AS MOCKDB();";

        var parsed = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
        var errors = parsed.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        Assert.True(errors.Count == 0,
            $"The alias '{alias}' suggested for connector '{connectorType}' does not parse: "
            + string.Join("; ", errors));
    }

    [Fact]
    public void MockDbConnection_NeedsNoServerDetails()
    {
        // "Start with sample data" emits `MOCKDB()` with no arguments. If the descriptor demanded a
        // required field, the wizard would render an unfillable form for the one connector that is
        // supposed to need nothing.
        ETL_SQL.Data.IConnector connector = new ETL_SQL.Connectors.MockDb.MockDbConnector();
        var schema = connector.GetSchemaDescriptor();

        var required = (schema.Options ?? [])
            .Where(option => option.IsMandatory)
            .Select(option => option.Name)
            .ToList();

        Assert.True(required.Count == 0,
            "MOCKDB must be usable with no configuration; mandatory options: " + string.Join(", ", required));
    }
}

/// <summary>
/// Verifies end-to-end execution of Studio starter scripts in Portal host via /api/designer/run,
/// including deliberate validation failure, repair, and restricted learner execution.
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioStarterScriptPortalExecutionTests : IClassFixture<StudioStarterScriptPortalExecutionTests.PracticePortalFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly PracticePortalFactory _factory;

    public StudioStarterScriptPortalExecutionTests(PracticePortalFactory factory)
    {
        _factory = factory;
    }

    public sealed class PracticePortalFactory : PortalWebFactory
    {
        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Studio.RoleCapabilities["Viewer"] =
            [
                StudioCapabilities.StudioAccess,
                StudioCapabilities.ScriptRun
            ];
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string GetEtlStarterScript()
    {
        var studioJs = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "studio-contracts.js"));
        var match = Regex.Match(studioJs, @"etl:\s*`(?<script>[^`]*)`", RegexOptions.Singleline);
        Assert.True(match.Success, "etl starter script not found in studio-contracts.js");
        return match.Groups["script"].Value;
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initialRes = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin@12345!" });
        if (initialRes.StatusCode == HttpStatusCode.OK)
        {
            var initialToken = (await initialRes.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
            var change = await SendAsync(client, HttpMethod.Post, initialToken, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Practice99!" });
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        }
        return await LoginAsync(client, "admin", "Admin@Practice99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return body!["token"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task EtlStarterScript_InitialExecution_FailsOnDeliberateAssertion()
    {
        using var client = _factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var script = GetEtlStarterScript();

        var response = await SendAsync(client, HttpMethod.Post, adminToken, "/api/designer/run", new { script });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        var error = body!["error"]?.GetValue<string>();
        Assert.NotNull(error);
        Assert.Contains("Data quality check failed: Expected at least 500 orders", error);
    }

    [Fact]
    public async Task EtlStarterScript_RepairedExecution_SucceedsAndReturnsSummaryRows()
    {
        using var client = _factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var script = GetEtlStarterScript().Replace(">= 500", ">= 50");

        var response = await SendAsync(client, HttpMethod.Post, adminToken, "/api/designer/run", new { script });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        var rows = body!["rows"]!.AsArray();
        Assert.True(rows.Count > 0, "Expected regional summary rows from the repaired pipeline run.");
        var columns = body["columns"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        Assert.Contains("Region", columns);
        Assert.Contains("Orders", columns);
        Assert.Contains("Revenue", columns);
        Assert.False(body["capped"]!.GetValue<bool>());
    }

    [Fact]
    public async Task EtlStarterScript_LearnerWithPracticePermissions_CanRunPipelineWithoutPublishPermission()
    {
        using var client = _factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"learner_{suffix}";

        var createRes = await SendAsync(client, HttpMethod.Post, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        var initialToken = await LoginAsync(client, username, "Initial@Test1!");
        var changeRes = await SendAsync(client, HttpMethod.Post, initialToken, "/api/auth/change-password",
            new { currentPassword = "Initial@Test1!", newPassword = "Learner@Pass123!" });
        Assert.Equal(HttpStatusCode.NoContent, changeRes.StatusCode);

        var learnerToken = await LoginAsync(client, username, "Learner@Pass123!");

        // 1. Practice execution in Studio succeeds for learner
        var repairedScript = GetEtlStarterScript().Replace(">= 500", ">= 50");
        var runResponse = await SendAsync(client, HttpMethod.Post, learnerToken, "/api/designer/run", new { script = repairedScript });
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        var runBody = await runResponse.Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.True(runBody!["rows"]!.AsArray().Count > 0);

        // 2. Authoring / publishing operations are denied
        var saveResponse = await SendAsync(client, HttpMethod.Post, learnerToken, "/api/designer/save", new
        {
            reportId = 1,
            scriptText = repairedScript
        });
        Assert.Equal(HttpStatusCode.Forbidden, saveResponse.StatusCode);

        var publishResponse = await SendAsync(client, HttpMethod.Post, learnerToken, "/api/reports", new
        {
            folderId = 1,
            name = "Unauthorized Publish",
            scriptPath = "test.rptsql"
        });
        Assert.Equal(HttpStatusCode.Forbidden, publishResponse.StatusCode);
    }
}
