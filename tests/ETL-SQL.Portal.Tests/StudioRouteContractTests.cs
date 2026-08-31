using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Asserts that every server route the canonical Studio asset requests is actually served by every
/// host that mounts Studio.
///
/// <para>Studio ships as one shared <c>studio.js</c> across the Portal, the Workstation Editor
/// desktop host, VS Code, the Report Player, and the ui-sandbox. Those hosts do not expose the same
/// routes. Nothing previously checked the two against each other, and the result was that Portal
/// Studio requested <c>/api/analyze</c>, <c>/api/complete</c>, <c>/api/hover</c>, <c>/api/format</c>
/// and <c>/api/run</c> — all desktop-only names. Every one 404'd, and every caller swallowed the
/// failure: completions returned null, the linter pinned a spurious "Not Found" diagnostic to line 1,
/// format silently changed nothing while reporting success, and a failed run rendered as a green
/// "In-Memory Run Completed" over stale sample rows.</para>
///
/// <para>The failure was invisible to the existing suites because the ui-sandbox mock answers any
/// unmatched path with <c>{ok: true}</c> and an empty body, and because each host was only ever
/// tested against itself. This test is the cross-host check that was missing.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class StudioRouteContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string CanonicalStudioJs() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "studio.js"));

    private static string CanonicalStudioContractsJs() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", "studio-contracts.js"));

    /// <summary>Pulls the route literals out of a frozen table declared in studio.js.</summary>
    private static IReadOnlyList<string> RouteTable(string studioJs, string tableName)
    {
        var table = Regex.Match(
            studioJs,
            $@"const\s+{Regex.Escape(tableName)}\s*=\s*Object\.freeze\(\{{(?<body>.*?)\}}\);",
            RegexOptions.Singleline);
        Assert.True(table.Success, $"{tableName} was not found in studio-contracts.js. Routes must stay in the table so this contract can be checked.");

        var routes = Regex.Matches(table.Groups["body"].Value, @"'(?<route>/api/[^']+)'")
            .Select(m => m.Groups["route"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(routes);
        return routes;
    }

    /// <summary>Every route attribute reachable on the Portal's Studio-facing controllers.</summary>
    private static HashSet<string> PortalRoutes()
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in new[] { typeof(DesignerController), typeof(StudioController), typeof(DatasetController) })
        {
            var prefix = controller.GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>()
                .Select(a => a.Template)
                .FirstOrDefault() ?? string.Empty;

            foreach (var method in controller.GetMethods())
            {
                foreach (var attribute in method.GetCustomAttributes(true).OfType<IRouteTemplateProvider>())
                {
                    var template = attribute.Template;
                    // An absolute template escapes the controller prefix, e.g. "/api/session/metadata".
                    var full = string.IsNullOrEmpty(template)
                        ? $"/{prefix}"
                        : template.StartsWith('/') ? template : $"/{prefix}/{template}";
                    routes.Add(Normalize(full));
                }
            }
        }

        // Served outside the Studio controllers but consumed by Studio.
        routes.Add("/api/connectors/schema");
        return routes;
    }

    /// <summary>Minimal-API route literals mapped by the desktop Workstation Editor host.</summary>
    private static HashSet<string> DesktopRoutes()
    {
        var app = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "ETL-SQL.WorkstationEditor", "WorkstationEditorApp.cs"));

        return Regex.Matches(app, @"app\.Map(?:Get|Post|Put|Delete)\(""(?<route>/[^""]+)""")
            .Select(m => Normalize(m.Groups["route"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Route templates carry constraints (":int") and parameters that the client fills in; compare on
    // the literal segments only.
    private static string Normalize(string route)
    {
        var collapsed = Regex.Replace(route, @"\{[^}]*\}", "*");
        return "/" + string.Join('/', collapsed.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void EveryStudioRoute_IsServedByThePortal()
    {
        var portal = PortalRoutes();
        var missing = RouteTable(CanonicalStudioContractsJs(), "STUDIO_ROUTES")
            .Where(route => !portal.Contains(Normalize(route)))
            .ToList();

        Assert.True(missing.Count == 0,
            "studio.js requests routes the Portal does not serve, so they will 404 in Portal Studio: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void EveryStudioRoute_IsServedByTheDesktopHost()
    {
        var desktop = DesktopRoutes();
        var missing = RouteTable(CanonicalStudioContractsJs(), "STUDIO_ROUTES")
            .Where(route => !desktop.Contains(Normalize(route)))
            .ToList();

        Assert.True(missing.Count == 0,
            "studio.js requests routes the Workstation Editor does not serve, so they will 404 in desktop Studio: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void WorkspaceOnlyRoutes_AreServedByTheDesktopHostAndGuardedInStudio()
    {
        var studioJs = CanonicalStudioJs();
        var contractsJs = CanonicalStudioContractsJs();
        var desktop = DesktopRoutes();

        var missing = RouteTable(contractsJs, "STUDIO_WORKSPACE_ROUTES")
            .Where(route => !desktop.Contains(Normalize(route)))
            .ToList();
        Assert.True(missing.Count == 0,
            "Workspace routes must exist on the desktop host: " + string.Join(", ", missing));

        // The Portal has no workspace filesystem, so these must be reached behind a capability check
        // rather than requested blindly and left to 404.
        Assert.Contains("hasWorkspaceHost", studioJs, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogOnlyRoutes_AreServedByThePortalAndGuardedInStudio()
    {
        var studioJs = CanonicalStudioJs();
        var missing = RouteTable(CanonicalStudioContractsJs(), "STUDIO_CATALOG_ROUTES")
            .Where(route => !PortalRoutes().Contains(Normalize(route)))
            .ToList();

        Assert.True(missing.Count == 0, "Catalog routes must exist on the Portal: " + string.Join(", ", missing));
        Assert.Contains("hasWorkspaceHost", studioJs, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioJs_DeclaresNoApiRouteOutsideTheRouteTables()
    {
        var studioJs = CanonicalStudioJs();
        var contractsJs = CanonicalStudioContractsJs();
        var declared = RouteTable(contractsJs, "STUDIO_ROUTES")
            .Concat(RouteTable(contractsJs, "STUDIO_CATALOG_ROUTES"))
            .Concat(RouteTable(contractsJs, "STUDIO_WORKSPACE_ROUTES"))
            .ToHashSet(StringComparer.Ordinal);

        // Any other '/api/...' literal is a route that bypasses the tables, and therefore bypasses
        // the cross-host checks above. Comments are stripped first so prose mentioning a path does
        // not read as a call site.
        var code = Regex.Replace(studioJs + contractsJs, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        code = Regex.Replace(code, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

        var stray = Regex.Matches(code, @"['`](?<route>/api/[A-Za-z0-9\-_/]*)")
            .Select(m => m.Groups["route"].Value)
            .Where(route => !declared.Contains(route))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(stray.Count == 0,
            "studio.js hardcodes API routes outside STUDIO_ROUTES/STUDIO_WORKSPACE_ROUTES, which is how "
            + "the Portal/desktop route mismatch went unnoticed. Add them to a table instead: "
            + string.Join(", ", stray));
    }

    [Fact]
    public void StudioComposition_ImportsEachResponsibilityModule()
    {
        var studioJs = CanonicalStudioJs();
        var expectedModules = new[]
        {
            "studio-contracts.js",
            "studio-data.js",
            "studio-host.js",
            "studio-lifecycle.js",
            "studio-security.js",
            "studio-sql-mutations.js",
            "studio-state.js"
        };

        Assert.All(expectedModules, module =>
        {
            Assert.Contains($"'./{module}'", studioJs, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(
                RepoRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "designer", module)),
                $"Canonical Studio responsibility module is missing: {module}");
        });

        Assert.DoesNotContain("function createDocumentContext", studioJs, StringComparison.Ordinal);
        Assert.DoesNotContain("const STUDIO_ROUTES =", studioJs, StringComparison.Ordinal);
        Assert.DoesNotContain("function canonicalDesignerMutation", studioJs, StringComparison.Ordinal);
    }
}

/// <summary>
/// Behavioural cover for the editor-assist endpoints Studio depends on. These existed only on the
/// desktop host before, so nothing asserted the Portal could serve hover documentation or formatting
/// at all.
/// </summary>
[Trait("Category", "Portal")]
public sealed class DesignerAssistEndpointTests
{
    private static DesignerController Controller() => new(
        languageHelp: new ETL_SQL.Core.Metadata.LanguageHelpRegistry(),
        functionRegistry: BuildFunctionRegistry());

    private static ETL_SQL.Core.Functions.IFunctionRegistry BuildFunctionRegistry()
    {
        var registry = new ETL_SQL.Engine.Functions.FunctionRegistry();
        ETL_SQL.Engine.Functions.StandardFunctions.Register(registry);
        return registry;
    }

    [Fact]
    public void Hover_ReturnsMarkdownForALanguageKeyword()
    {
        var result = Assert.IsType<OkObjectResult>(Controller().Hover(new HoverDesignerRequest("SELECT")));
        var response = Assert.IsType<HoverDesignerResponse>(result.Value);

        Assert.False(string.IsNullOrWhiteSpace(response.Markdown));
        Assert.Equal("keyword", response.Kind);
    }

    [Fact]
    public void Hover_ReturnsNothingForAnUnknownToken()
    {
        var result = Assert.IsType<OkObjectResult>(
            Controller().Hover(new HoverDesignerRequest("not_a_language_token_zzz")));
        var response = Assert.IsType<HoverDesignerResponse>(result.Value);

        Assert.Null(response.Markdown);
    }

    [Fact]
    public void Hover_WithoutLanguageHelpConfigured_ReportsUnavailableRatherThanEmpty()
    {
        // A host that cannot serve help must say so. Returning an empty hover would be
        // indistinguishable from "this token has no documentation".
        var result = Assert.IsType<ObjectResult>(new DesignerController().Hover(new HoverDesignerRequest("SELECT")));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    [Fact]
    public void Snippets_AreOfferedForADollarTriggerAtStatementStart()
    {
        // The 83-snippet library already reached the TUI and VS Code; neither GUI editor exposed it,
        // so the two surfaces a newcomer is most likely to start in had no starter templates.
        var matches = ETL_SQL.Analysis.Services.SnippetCompletionSource.GetMatches("$kpi", "$kpi");

        var kpi = matches.FirstOrDefault(snippet => snippet.Trigger == "$kpi");
        Assert.NotNull(kpi);
        Assert.Contains("CREATE VISUAL", kpi!.TuiBody, StringComparison.Ordinal);
        // The GUI editors insert completion text literally, so placeholders stay in the readable
        // «guillemet» form rather than LSP ${1:} tab stops.
        Assert.Contains("«", kpi.TuiBody, StringComparison.Ordinal);
        Assert.DoesNotContain("${1:", kpi.TuiBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Snippets_AreNotOfferedMidExpression()
    {
        // A snippet expands to a whole statement, so firing one inside an expression would splice
        // a CREATE VISUAL into the middle of a SELECT.
        Assert.Empty(ETL_SQL.Analysis.Services.SnippetCompletionSource.GetMatches("SELECT $kpi", "$kpi"));
    }

    [Fact]
    public void Snippets_AreNotOfferedForAnOrdinaryWord()
    {
        Assert.Empty(ETL_SQL.Analysis.Services.SnippetCompletionSource.GetMatches("kpi", "kpi"));
    }

    [Fact]
    public void Format_ReturnsFormattedScriptUnderTheScriptField()
    {
        // Studio reads `script`. It previously read `formatted`, which no host has ever returned, so
        // Format silently changed nothing while reporting success.
        var result = Assert.IsType<OkObjectResult>(
            new DesignerController().Format(new FormatDesignerRequest("select 1 from dual;")));
        var response = Assert.IsType<FormatDesignerResponse>(result.Value);

        Assert.False(string.IsNullOrWhiteSpace(response.Script));
        Assert.Contains("SELECT", response.Script, StringComparison.Ordinal);
        Assert.Empty(response.Diagnostics);
    }

    [Fact]
    public void Format_LeavesAnEmptyScriptAlone()
    {
        var result = Assert.IsType<OkObjectResult>(
            new DesignerController().Format(new FormatDesignerRequest("   ")));
        var response = Assert.IsType<FormatDesignerResponse>(result.Value);

        Assert.Equal("   ", response.Script);
        Assert.Empty(response.Diagnostics);
    }

    [Fact]
    public void Format_RejectsScriptOverLimit()
    {
        var controller = new DesignerController(portalConfig: new PortalConfig
        {
            DesignerLimits = new PortalDesignerLimitsConfig { MaxScriptCharacters = 5 }
        });

        var result = Assert.IsType<ObjectResult>(controller.Format(new FormatDesignerRequest("SELECT 1 FROM t;")));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }
}
