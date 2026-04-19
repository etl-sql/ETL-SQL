using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Validates visual metadata, ensuring required sources and mappings are present.
    /// Covers Gaps 71 (Source Required) and 73 (Mapping Completeness).
    /// </summary>
    public class VisualMetadataRule : ILintRule
    {
        public string Name        => "VisualMetadata";
        public string Description => "Validates that visuals have required sources and mappings per visual type.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateVisualStatement visual) continue;

                CheckSourceRequired(visual, results);
                CheckMappingsComplete(visual, results);
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void CheckSourceRequired(CreateVisualStatement visual, List<LintResult> results)
        {
            // Slicer and MultiSelect MUST have a source (Item 71)
            if (IsSourceRequired(visual.VisualType))
            {
                if (visual.Source == null || (visual.Source.TempTableName == null && !visual.Source.IsInlineSelect))
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Error,
                        Message      = $"Visual '{visual.Name}' of type {visual.VisualType} requires a SOURCE clause.",
                        LineNumber   = visual.Line,
                        ColumnNumber = visual.Column
                    });
                }
            }
        }

        private void CheckMappingsComplete(CreateVisualStatement visual, List<LintResult> results)
        {
            // Mapping requirements per visual type (Item 73)
            var requiredRoles = GetRequiredRoles(visual.VisualType);
            if (requiredRoles == null || requiredRoles.Count == 0) return;

            var presentRoles = new HashSet<string>(visual.Mappings.Select(m => m.Role.ToUpperInvariant()));
            foreach (var role in requiredRoles)
            {
                if (!presentRoles.Contains(role.ToUpperInvariant()))
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Error,
                        Message      = $"Visual '{visual.Name}' of type {visual.VisualType} is missing the required mapping role: '{role}'.",
                        LineNumber   = visual.Line,
                        ColumnNumber = visual.Column
                    });
                }
            }
        }

        private static bool IsSourceRequired(VisualType type)
        {
            return type switch
            {
                VisualType.Slicer      => true,
                VisualType.MultiSelect => true,
                // Most other types (Bar, Line, etc.) also require source, 
                // but they are often caught by the parser. 
                // Adding them here provides a unified linter experience.
                VisualType.Bar         => true,
                VisualType.Line        => true,
                VisualType.Scatter     => true,
                VisualType.Pie         => true,
                VisualType.Donut       => true,
                VisualType.HorizontalBar => true,
                VisualType.BoxPlot     => true,
                VisualType.Treemap     => true,
                VisualType.HeatMap     => true,
                VisualType.Combo       => true,
                VisualType.Gauge       => true,
                VisualType.Funnel      => true,
                VisualType.Waterfall   => true,
                VisualType.Table       => true,
                VisualType.Card        => true,
                _                      => false // Text, DatePicker, Slider, Search don't need source
            };
        }

        private static List<string> GetRequiredRoles(VisualType type)
        {
            return type switch
            {
                VisualType.Bar          => new List<string> { "X", "Y" },
                VisualType.Line         => new List<string> { "X", "Y" },
                VisualType.HorizontalBar => new List<string> { "X", "Y" },
                VisualType.Waterfall    => new List<string> { "X", "Y" },
                VisualType.Scatter      => new List<string> { "X", "Y" },
                VisualType.HeatMap      => new List<string> { "X", "Y", "VALUE" },
                VisualType.Pie          => new List<string> { "LABEL", "VALUE" },
                VisualType.Donut        => new List<string> { "LABEL", "VALUE" },
                VisualType.Funnel       => new List<string> { "LABEL", "VALUE" },
                VisualType.Card         => new List<string> { "VALUE" },
                VisualType.Gauge        => new List<string> { "VALUE" },
                VisualType.Slicer       => new List<string> { "VALUE" },
                VisualType.MultiSelect  => new List<string> { "VALUE" },
                VisualType.BoxPlot      => new List<string> { "X", "LOW", "Q1", "MEDIAN", "Q3", "HIGH" },
                VisualType.Combo        => new List<string> { "X" },
                _                       => null
            };
        }
    }
}
