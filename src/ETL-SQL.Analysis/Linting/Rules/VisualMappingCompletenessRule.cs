using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// Validates that required mapping roles (X, Y, LABEL, VALUE, etc.) are present 
/// per visual type.
/// </summary>
public class VisualMappingCompletenessRule : ILintRule
{
    public string Name => "Visual Mapping Completeness";
    public string Description => "Ensures that required mapping roles are present for the visual type.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        foreach (var stmt in script.Statements)
        {
            if (stmt is not CreateVisualStatement visual) continue;

            var presentRoles = new HashSet<string>(visual.Mappings.Select(m => m.Role.ToUpperInvariant()));

            // Exact required roles — must match verbatim.
            var requiredRoles = GetRequiredRoles(visual);
            if (requiredRoles != null)
            {
                foreach (var role in requiredRoles)
                {
                    if (!presentRoles.Contains(role.ToUpperInvariant()))
                    {
                        results.Add(new LintResult
                        {
                            RuleName = Name,
                            Severity = LintSeverity.Error,
                            Message = $"Visual '{visual.Name}' of type {visual.VisualType} is missing the required mapping role: '{role}'.",
                            LineNumber = visual.Line,
                            ColumnNumber = visual.Column
                        });
                    }
                }
            }

            // Alias-group checks: any one alias in the group satisfies the requirement.
            foreach (var (group, label) in GetAliasGroups(visual))
            {
                if (!group.Any(alias => presentRoles.Contains(alias)))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Error,
                        Message = $"Visual '{visual.Name}' of type {visual.VisualType} is missing the required mapping role: '{label}' (accepted: {string.Join(", ", group)}).",
                        LineNumber = visual.Line,
                        ColumnNumber = visual.Column
                    });
                }
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    /// <summary>
    /// Returns unconditionally required single-spelling roles. Use <see cref="GetAliasGroups"/>
    /// for roles where multiple spellings are accepted.
    /// </summary>
    private static List<string>? GetRequiredRoles(CreateVisualStatement visual)
    {
        return visual.VisualType switch
        {
            VisualType.Bar => new List<string> { "X", "Y" },
            VisualType.Line => new List<string> { "X", "Y" },
            VisualType.HorizontalBar => new List<string> { "X", "Y" },
            // Waterfall: NAME|X and VALUE|Y are aliases — handled in GetAliasGroups.
            VisualType.Waterfall => null,
            // Gantt: Y|LABEL, START|X, and END|X2 are aliases — handled in GetAliasGroups.
            VisualType.Gantt => null,
            VisualType.Scatter => new List<string> { "X", "Y" },
            VisualType.HeatMap => new List<string> { "X", "Y", "VALUE" },
            VisualType.Pie => new List<string> { "LABEL", "VALUE" },
            VisualType.Donut => new List<string> { "LABEL", "VALUE" },
            VisualType.Funnel => new List<string> { "LABEL", "VALUE" },
            VisualType.Card => new List<string> { "VALUE" },
            VisualType.Gauge => new List<string> { "VALUE" },
            VisualType.Slicer => new List<string> { "VALUE" },
            VisualType.MultiSelect => new List<string> { "VALUE" },
            // BoxPlot: Either raw distribution (X/CATEGORY + Y/VALUE) or precomputed stats (X/CATEGORY + LOW/MIN + Q1 + MEDIAN + Q3 + HIGH/MAX) — handled in GetAliasGroups.
            VisualType.BoxPlot => null,
            VisualType.Combo => new List<string> { "X" },
            VisualType.Bubble => new List<string> { "X", "Y" },
            VisualType.Candlestick => new List<string> { "X", "OPEN", "HIGH", "LOW", "CLOSE" },
            VisualType.Map => GetMapRequiredRoles(visual),
            _ => null
        };
    }

    /// <summary>
    /// Returns alias groups for visual types where multiple role spellings map to the same
    /// semantic channel. Each entry is (set-of-accepted-aliases, display-label-for-errors).
    /// </summary>
    private static List<(HashSet<string> Group, string Label)> GetAliasGroups(CreateVisualStatement visual)
    {
        if (visual.VisualType == VisualType.Waterfall)
        {
            return
            [
                (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X", "NAME" },  "X / NAME"),
                (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Y", "VALUE" }, "Y / VALUE"),
            ];
        }

        if (visual.VisualType == VisualType.Gantt)
        {
            return
            [
                (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Y", "LABEL" },  "Y / LABEL"),
                (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "START", "X" },  "START / X"),
                (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "END", "X2" },   "END / X2"),
            ];
        }

        if (visual.VisualType == VisualType.BoxPlot)
        {
            var present = new HashSet<string>(visual.Mappings.Select(m => m.Role.ToUpperInvariant()));
            var usesPrecomputed = present.Contains("LOW") || present.Contains("MIN") ||
                                  present.Contains("Q1") || present.Contains("MEDIAN") ||
                                  present.Contains("Q3") || present.Contains("HIGH") || present.Contains("MAX");

            if (usesPrecomputed)
            {
                return
                [
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X", "CATEGORY" }, "X / CATEGORY"),
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LOW", "MIN" }, "LOW / MIN"),
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Q1" }, "Q1"),
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MEDIAN" }, "MEDIAN"),
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Q3" }, "Q3"),
                    (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HIGH", "MAX" }, "HIGH / MAX"),
                ];
            }

            return
            [
                (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X", "CATEGORY" }, "X / CATEGORY"),
                (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Y", "VALUE" }, "Y / VALUE"),
            ];
        }

        return [];
    }

    private static List<string> GetMapRequiredRoles(CreateVisualStatement visual)
    {
        var mode = visual.Options
            .FirstOrDefault(o => o.Key.Equals("MODE", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        return mode.Equals("POINTS", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { "LAT", "LON" }
            : new List<string> { "REGION" };
    }
}
