using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
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

                var requiredRoles = GetRequiredRoles(visual);
                if (requiredRoles == null || requiredRoles.Count == 0) continue;

                var presentRoles = new HashSet<string>(visual.Mappings.Select(m => m.Role.ToUpperInvariant()));
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

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private static List<string>? GetRequiredRoles(CreateVisualStatement visual)
        {
            return visual.VisualType switch
            {
                VisualType.Bar => new List<string> { "X", "Y" },
                VisualType.Line => new List<string> { "X", "Y" },
                VisualType.HorizontalBar => new List<string> { "X", "Y" },
                VisualType.Waterfall => new List<string> { "X", "Y" },
                VisualType.Scatter => new List<string> { "X", "Y" },
                VisualType.HeatMap => new List<string> { "X", "Y", "VALUE" },
                VisualType.Pie => new List<string> { "LABEL", "VALUE" },
                VisualType.Donut => new List<string> { "LABEL", "VALUE" },
                VisualType.Funnel => new List<string> { "LABEL", "VALUE" },
                VisualType.Card => new List<string> { "VALUE" },
                VisualType.Gauge => new List<string> { "VALUE" },
                VisualType.Slicer => new List<string> { "VALUE" },
                VisualType.MultiSelect => new List<string> { "VALUE" },
                VisualType.BoxPlot => new List<string> { "X", "LOW", "Q1", "MEDIAN", "Q3", "HIGH" },
                VisualType.Combo => new List<string> { "X" },
                VisualType.Bubble => new List<string> { "X", "Y" },
                VisualType.Candlestick => new List<string> { "X", "OPEN", "HIGH", "LOW", "CLOSE" },
                VisualType.Map => GetMapRequiredRoles(visual),
                _ => null
            };
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
}
