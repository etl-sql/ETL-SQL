using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;

namespace ETL_SQL.Tests.Portal;

public sealed class HtmlVisualDesignerRoundTripTests
{
    [Fact]
    public void UnrelatedDesignerEdit_PreservesHtmlVisualClausesByteForByte()
    {
        const string script = """
            CREATE VISUAL NodeStatus AS HTML (
              TITLE = 'Original',
              SOURCE = #cluster_nodes,
              MODE = REPEATER,
              TEMPLATE = '<article class="node"><h3>{{HostName}}</h3><p>{{CpuPercent}}</p></article>',
              STYLE (
                CSS = '.node { padding: 1rem; border: 1px solid #ccc; }'
              ),
              FALLBACK = 'Cluster node: {{HostName}} — CPU {{CpuPercent}}',
              ACTIONS (
                ON_CLICK = SET_PARAMETER(@selected_node, HostName)
              )
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = NodeStatus)));
            """;
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var page = Assert.Single(parsed.DesignState.Pages);
        var visual = Assert.Single(page.Visuals);
        var state = parsed.DesignState with
        {
            Pages = [page with { Visuals = [visual with { Title = "Updated Title" }] }]
        };

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Equal(Clause(script, "TEMPLATE"), Clause(patched, "TEMPLATE"));
        Assert.Equal(Clause(script, "STYLE"), Clause(patched, "STYLE"));
        Assert.Equal(Clause(script, "FALLBACK"), Clause(patched, "FALLBACK"));
        Assert.Equal(Clause(script, "MODE"), Clause(patched, "MODE"));
        Assert.Equal(Clause(script, "ACTIONS"), Clause(patched, "ACTIONS"));
        Assert.Contains("TITLE = 'Updated Title'", patched);
    }

    [Fact]
    public void HtmlVisualEdit_SurgicallyPatchesTemplateAndStyle_PreservingSurroundingSqlAndComments()
    {
        const string script = """
            -- Section 1: Extract data
            /* Important data comment */
            SELECT 'web-01' AS HostName, '42.5%' AS CpuPercent INTO #cluster_nodes;

            CREATE VISUAL NodeStatus AS HTML (
              TITLE = 'Node Status',
              SOURCE = #cluster_nodes,
              MODE = SINGLE,
              TEMPLATE = '<div class="old-node">{{HostName}}</div>',
              STYLE ( CSS = '.old-node { color: red; }' ),
              FALLBACK = 'Old fallback'
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = NodeStatus)));
            -- Footer comment
            """;

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var visual = Assert.Single(parsed.DesignState.Pages[0].Visuals);

        const string newTemplate = "<article class=\"node-card\"><h3>{{HostName}}</h3><p>{{CpuPercent}}</p></article>";
        const string newStyle = ".node-card { padding: 12px; border-radius: 4px; }";
        const string newFallback = "Node: {{HostName}} ({{CpuPercent}})";

        var updatedOptions = new Dictionary<string, string>(visual.Options)
        {
            ["html_mode"] = "REPEATER",
            ["html_template"] = newTemplate,
            ["html_style"] = newStyle,
            ["html_fallback"] = newFallback
        };

        var state = parsed.DesignState with
        {
            Pages = [parsed.DesignState.Pages[0] with
            {
                Visuals = [visual with { Options = updatedOptions }]
            }]
        };

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Contains("-- Section 1: Extract data", patched);
        Assert.Contains("/* Important data comment */", patched);
        Assert.Contains("-- Footer comment", patched);
        Assert.Contains("MODE = REPEATER", patched);
        Assert.Contains(newTemplate, patched);
        Assert.Contains(newStyle, patched);
        Assert.Contains(newFallback, patched);

        // Re-parsing patched script should succeed cleanly
        var reparse = analysis.Parse(patched, 100);
        Assert.Null(reparse.Error);
        var reVisual = Assert.Single(reparse.DesignState.Pages[0].Visuals);
        Assert.Equal("REPEATER", reVisual.Options["html_mode"]);
        Assert.Equal(newTemplate, reVisual.Options["html_template"]);
        Assert.Equal(newStyle, reVisual.Options["html_style"]);
        Assert.Equal(newFallback, reVisual.Options["html_fallback"]);
    }

    [Fact]
    public void GenerateFromScratch_EmitsCompleteHtmlVisualClause()
    {
        var visual = new DesignerVisualDto(
            Id: "v_html_1",
            Name: "ClusterSummary",
            Type: "HTML",
            GridCol: 1,
            GridRow: 1,
            GridColSpan: 12,
            GridRowSpan: 4,
            Title: "Cluster Summary",
            Dataset: "cluster_nodes",
            Mappings: new Dictionary<string, string>(),
            Options: new Dictionary<string, string>
            {
                ["html_mode"] = "REPEATER",
                ["html_template"] = "<div class=\"node-item\">{{HostName}}</div>",
                ["html_style"] = ".node-item { font-weight: bold; }",
                ["html_fallback"] = "Summary: {{HostName}}"
            }
        );

        var state = new DesignerStateDto(
            Pages: [new DesignerPageDto("p1", "Overview", "Dashboard", [visual])],
            Datasets: [new DesignerDatasetDto("ds1", "cluster_nodes", "SELECT 'web-01' AS HostName")]
        );

        var generated = new DesignerScriptGenerationService().Generate(state);

        Assert.Contains("CREATE VISUAL ClusterSummary AS HTML", generated);
        Assert.Contains("TITLE = 'Cluster Summary'", generated);
        Assert.Contains("SOURCE = &cluster_nodes", generated);
        Assert.Contains("MODE = REPEATER", generated);
        Assert.Contains("TEMPLATE = '<div class=\"node-item\">{{HostName}}</div>'", generated);
        Assert.Contains("STYLE ( CSS = '.node-item { font-weight: bold; }' )", generated);
        Assert.Contains("FALLBACK = 'Summary: {{HostName}}'", generated);

        // Script must parse without error
        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(generated, 100);
        Assert.Null(parsed.Error);
    }

    [Fact]
    public void ConvertStandardVisualToHtml_GeneratesValidHtmlSyntax()
    {
        const string script = """
            CREATE VISUAL KpiCard AS CARD (
              TITLE = 'KPI Card',
              SOURCE = #metrics,
              MAPPINGS ( Value = total_revenue )
            );
            CREATE PAGE [Dashboard] AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = KpiCard)));
            """;

        var analysis = new DesignerAnalysisService();
        var parsed = analysis.Parse(script, 100);
        Assert.Null(parsed.Error);
        var visual = Assert.Single(parsed.DesignState.Pages[0].Visuals);

        // Convert visual to HTML type with template and style
        var htmlOptions = new Dictionary<string, string>
        {
            ["html_mode"] = "SINGLE",
            ["html_template"] = "<div class=\"kpi\"><span class=\"val\">{{total_revenue}}</span></div>",
            ["html_style"] = ".kpi { font-size: 24px; }",
            ["html_fallback"] = "Revenue: {{total_revenue}}"
        };

        var state = parsed.DesignState with
        {
            Pages = [parsed.DesignState.Pages[0] with
            {
                Visuals = [visual with { Type = "HTML", Mappings = new Dictionary<string, string>(), Options = htmlOptions }]
            }]
        };

        var patched = new DesignerScriptPatcher().Patch(script, state);

        Assert.Contains("CREATE VISUAL KpiCard AS HTML (", patched);
        Assert.Contains("TEMPLATE = '<div class=\"kpi\"><span class=\"val\">{{total_revenue}}</span></div>'", patched);
        Assert.Contains("STYLE ( CSS = '.kpi { font-size: 24px; }' )", patched);

        var reparse = analysis.Parse(patched, 100);
        Assert.Null(reparse.Error);
        var reVisual = Assert.Single(reparse.DesignState.Pages[0].Visuals);
        Assert.Equal("HTML", reVisual.Type);
    }

    private static string Clause(string script, string keyword)
    {
        var start = script.IndexOf(keyword, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = script.IndexOf('\n', start);
        return end < 0 ? script[start..] : script[start..end];
    }
}
