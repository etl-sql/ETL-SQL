using ETL_SQL.Core;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Tests;

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
