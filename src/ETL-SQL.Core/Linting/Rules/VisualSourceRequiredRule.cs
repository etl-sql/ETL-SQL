using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// Flags visuals that require a SOURCE clause but are missing it.
    /// Specifically flags SLICER and MULTISELECT which must have a source for options.
    /// </summary>
    public class VisualSourceRequiredRule : ILintRule
    {
        public string Name => "Visual Source Required";
        public string Description => "Ensures visuals like Slicer and MultiSelect have a SOURCE clause.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            foreach (var stmt in script.Statements)
            {
                if (stmt is not CreateVisualStatement visual) continue;

                if (IsSourceRequired(visual.VisualType))
                {
                    // Check if Source exists and is not "empty" (both query and table are null)
                    bool hasSource = visual.Source != null && (visual.Source.IsInlineSelect || !string.IsNullOrEmpty(visual.Source.TempTableName));
                    
                    if (!hasSource)
                    {
                        results.Add(new LintResult
                        {
                            RuleName = Name,
                            Severity = LintSeverity.Error,
                            Message = $"Visual '{visual.Name}' of type {visual.VisualType} requires a SOURCE clause.",
                            LineNumber = visual.Line,
                            ColumnNumber = visual.Column
                        });
                    }
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private static bool IsSourceRequired(VisualType type)
        {
            return type switch
            {
                VisualType.Slicer => true,
                VisualType.MultiSelect => true,
                VisualType.Bar => true,
                VisualType.Line => true,
                VisualType.Scatter => true,
                VisualType.Pie => true,
                VisualType.Donut => true,
                VisualType.HorizontalBar => true,
                VisualType.BoxPlot => true,
                VisualType.Treemap => true,
                VisualType.HeatMap => true,
                VisualType.Combo => true,
                VisualType.Gauge       => true,
                VisualType.Funnel      => true,
                VisualType.Waterfall   => true,
                VisualType.Table       => true,
                VisualType.Card        => true,
                VisualType.Bubble      => true,
                VisualType.Radar       => true,
                VisualType.Candlestick => true,
                VisualType.Map         => true,
                _ => false // Text, DatePicker, Slider, Search don't need source
            };
        }
    }
}
