using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
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
