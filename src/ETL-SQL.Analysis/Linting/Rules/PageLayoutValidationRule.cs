using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;

public class PageLayoutValidationRule : ILintRule
{
    public string Name => "PageLayoutValidation";
    public string Description => "Validates PAGE_LAYOUT attributes such as SIZE, ORIENTATION, UNITS, OVERFLOW, and CUSTOM dimensions.";

    private static readonly HashSet<string> ValidSizes = new(StringComparer.OrdinalIgnoreCase) { "Letter", "A4", "Custom" };
    private static readonly HashSet<string> ValidOrientations = new(StringComparer.OrdinalIgnoreCase) { "Portrait", "Landscape" };
    private static readonly HashSet<string> ValidUnits = new(StringComparer.OrdinalIgnoreCase) { "in", "cm", "mm", "px" };
    private static readonly HashSet<string> ValidOverflows = new(StringComparer.OrdinalIgnoreCase) { "Scale", "Split", "Clip" };

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        ProcessStatements(script.Statements, results);
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void ProcessStatements(IEnumerable<Statement> statements, List<LintResult> results)
    {
        foreach (var stmt in statements)
        {
            if (stmt is CreatePageStatement page && page.PrintLayout != null)
            {
                ValidateLayout(page.PrintLayout, page.Name, page.Line, page.Column, results);
            }
            else if (stmt is BlockStatement block)
            {
                ProcessStatements(block.Statements, results);
            }
        }
    }

    private void ValidateLayout(PageLayoutDefinition layout, string pageName, int line, int column, List<LintResult> results)
    {
        if (layout.PageSize != null && !ValidSizes.Contains(layout.PageSize))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Error,
                Message = $"Page '{pageName}': Invalid PAGE_LAYOUT SIZE '{layout.PageSize}'. Expected 'Letter', 'A4', or 'Custom'.",
                LineNumber = line,
                ColumnNumber = column
            });
        }

        if (layout.Orientation != null && !ValidOrientations.Contains(layout.Orientation))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Error,
                Message = $"Page '{pageName}': Invalid PAGE_LAYOUT ORIENTATION '{layout.Orientation}'. Expected 'Portrait' or 'Landscape'.",
                LineNumber = line,
                ColumnNumber = column
            });
        }

        if (layout.Units != null && !ValidUnits.Contains(layout.Units))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Error,
                Message = $"Page '{pageName}': Invalid PAGE_LAYOUT UNITS '{layout.Units}'. Expected 'in', 'cm', 'mm', or 'px'.",
                LineNumber = line,
                ColumnNumber = column
            });
        }

        if (layout.Overflow != null && !ValidOverflows.Contains(layout.Overflow))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Error,
                Message = $"Page '{pageName}': Invalid PAGE_LAYOUT OVERFLOW '{layout.Overflow}'. Expected 'Scale', 'Split', or 'Clip'.",
                LineNumber = line,
                ColumnNumber = column
            });
        }

        bool isCustom = layout.PageSize != null && layout.PageSize.Equals("Custom", StringComparison.OrdinalIgnoreCase);

        if (isCustom && (layout.CustomWidth == null || layout.CustomHeight == null))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Error,
                Message = $"Page '{pageName}': PAGE_LAYOUT SIZE 'Custom' requires CUSTOM_WIDTH and CUSTOM_HEIGHT to be specified.",
                LineNumber = line,
                ColumnNumber = column
            });
        }

        if (!isCustom && (layout.CustomWidth != null || layout.CustomHeight != null))
        {
            results.Add(new LintResult
            {
                RuleName = Name,
                Severity = LintSeverity.Warning,
                Message = $"Page '{pageName}': CUSTOM_WIDTH and CUSTOM_HEIGHT are ignored unless SIZE is 'Custom'.",
                LineNumber = line,
                ColumnNumber = column
            });
        }
    }
}
