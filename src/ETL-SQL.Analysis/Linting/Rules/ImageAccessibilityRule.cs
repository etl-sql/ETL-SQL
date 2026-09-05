using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>
/// Flags IMAGE visuals that lack an ALT option for accessibility compliance.
/// </summary>
public sealed class ImageAccessibilityRule : ILintRule
{
    public string Name => "ImageAccessibility";
    public string Description => "Flags IMAGE visuals that lack an ALT text option for screen readers and accessibility compliance.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        foreach (var visual in script.Statements.OfType<CreateVisualStatement>())
        {
            if (visual.VisualType == VisualType.Image)
            {
                var hasAlt = visual.Options.Any(o => string.Equals(o.Key, "ALT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(o.Value));
                if (!hasAlt)
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Code = "RPT4001",
                        Severity = LintSeverity.Warning,
                        Message = $"IMAGE visual '{visual.Name}' is missing an ALT text option for accessibility compliance.",
                        LineNumber = visual.Line,
                        ColumnNumber = visual.Column
                    });
                }
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }
}
