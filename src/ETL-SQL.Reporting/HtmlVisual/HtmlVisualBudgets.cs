using System;
using System.Text;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.HtmlVisual;

/// <summary>Closed resource limits for one constrained HTML visual.</summary>
public static class HtmlVisualBudgets
{
    public const int DefaultMaxRows = ConstrainedHtmlPolicy.DefaultMaxRows;
    public const int MaxTemplateNodes = ConstrainedHtmlPolicy.MaxTemplateNodes;
    public const int MaxOutputNodes = ConstrainedHtmlPolicy.MaxOutputNodes;
    public const int MaxTemplateBytes = ConstrainedHtmlPolicy.MaxTemplateBytes;
    public const int MaxCssBytes = ConstrainedHtmlPolicy.MaxCssBytes;
    public const int MaxOutputBytes = ConstrainedHtmlPolicy.MaxOutputBytes;
    public const int MaxVisualRenderWork = ConstrainedHtmlPolicy.MaxVisualRenderWork;
    public const int MaxReportOutputNodes = ConstrainedHtmlPolicy.MaxReportOutputNodes;
    public const int MaxReportOutputBytes = ConstrainedHtmlPolicy.MaxReportOutputBytes;
    public const int MaxReportRenderWork = ConstrainedHtmlPolicy.MaxReportRenderWork;

    public static HtmlVisualCost ValidateAuthored(string template, string? css, int rowCount, int maxRows, bool repeater)
    {
        var templateBytes = Encoding.UTF8.GetByteCount(template);
        if (templateBytes > MaxTemplateBytes)
            throw Exceeded("RPT3020", "template byte", templateBytes, MaxTemplateBytes);

        var cssBytes = css is null ? 0 : Encoding.UTF8.GetByteCount(css);
        if (cssBytes > MaxCssBytes)
            throw Exceeded("RPT3021", "CSS byte", cssBytes, MaxCssBytes);

        var templateNodes = ConstrainedHtmlPolicy.CountElementNodes(template);
        if (templateNodes > MaxTemplateNodes)
            throw Exceeded("RPT3022", "template node", templateNodes, MaxTemplateNodes);

        var instances = repeater ? rowCount : 1;
        if (repeater && rowCount > maxRows)
            throw Exceeded("RPT3023", "row", rowCount, maxRows);

        var outputNodes = checked(templateNodes * instances);
        if (outputNodes > MaxOutputNodes)
            throw Exceeded("RPT3024", "output node", outputNodes, MaxOutputNodes);

        return new HtmlVisualCost(templateBytes, cssBytes, templateNodes, outputNodes, 0, outputNodes);
    }

    public static HtmlVisualCost ValidateRendered(HtmlVisualCost authored, string html)
    {
        var outputNodes = ConstrainedHtmlPolicy.CountElementNodes(html);
        if (outputNodes > MaxOutputNodes)
            throw Exceeded("RPT3024", "output node", outputNodes, MaxOutputNodes);
        var outputBytes = Encoding.UTF8.GetByteCount(html);
        if (outputBytes > MaxOutputBytes)
            throw Exceeded("RPT3025", "output byte", outputBytes, MaxOutputBytes);

        var renderWork = checked(outputNodes + (int)Math.Ceiling(outputBytes / 256d));
        if (renderWork > MaxVisualRenderWork)
            throw Exceeded("RPT3026", "render-work", renderWork, MaxVisualRenderWork);

        return authored with { OutputNodes = outputNodes, OutputBytes = outputBytes, RenderWork = renderWork };
    }

    private static HtmlVisualBudgetException Exceeded(string code, string budget, int actual, int maximum) =>
        new(code, $"HTML visual {budget} budget exceeded: {actual} > {maximum}.", actual, maximum);
}

public sealed record HtmlVisualCost(
    int TemplateBytes,
    int CssBytes,
    int TemplateNodes,
    int OutputNodes,
    int OutputBytes,
    int RenderWork);

public sealed class HtmlVisualBudgetException(string code, string message, int actual, int maximum)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Actual { get; } = actual;
    public int Maximum { get; } = maximum;
}
