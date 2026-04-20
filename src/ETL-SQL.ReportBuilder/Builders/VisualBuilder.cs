using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.ReportBuilder.Builders
{
    public class VisualBuilder(IExecutionContext ctx, EChartsRenderer renderer, StyleBuilder styleBuilder)
    {
        public async Task<VisualManifest> BuildAsync(string name, CreateVisualStatement vStmt)
        {
            var (title, titleMd) = styleBuilder.ResolveMarkdown(vStmt.Title, vStmt.TitleIsMarkdown);
            var (subtitle, subtitleMd) = styleBuilder.ResolveMarkdown(vStmt.Subtitle, vStmt.SubtitleIsMarkdown);

            var vm = new VisualManifest
            {
                Name            = name,
                VisualType      = vStmt.VisualType.ToString(),
                DefaultValue    = vStmt.DefaultValue,
                TitleIsMarkdown = titleMd,
                SubtitleIsMarkdown = subtitleMd,
                Tooltip         = styleBuilder.BuildTooltipManifest(vStmt.Tooltip)
            };

            if (title != null) vm.Options["title"] = title;
            if (subtitle != null) vm.Options["subtitle"] = subtitle;

            // Copy flat options and map special ones
            foreach (var opt in vStmt.Options)
            {
                vm.Options[opt.Key] = opt.Value;
                if (opt.Key == "GRID") vm.GridStyle = opt.Value;
                if (opt.Key == "DATA_LABELS")
                {
                    vm.DataLabels ??= new DataLabelsManifest();
                    vm.DataLabels.Show = opt.Value == "ON";
                }
                if (opt.Key.StartsWith("DATA_LABELS:"))
                {
                    vm.DataLabels ??= new DataLabelsManifest();
                    var sub = opt.Key.Substring("DATA_LABELS:".Length);
                    switch (sub)
                    {
                        case "POSITION": vm.DataLabels.Position = opt.Value; break;
                        case "COLOR": vm.DataLabels.Color = opt.Value; break;
                        case "FONT_SIZE": if (int.TryParse(opt.Value, out var fs)) vm.DataLabels.FontSize = fs; break;
                        case "FONT_WEIGHT": vm.DataLabels.FontWeight = opt.Value; break;
                        case "FONT_FAMILY": vm.DataLabels.FontFamily = opt.Value; break;
                        case "FORMAT": vm.DataLabels.Format = opt.Value; break;
                    }
                }
            }

            // Styles
            var resolvedStyles = styleBuilder.ResolveStyles(vStmt.StyleName, vStmt.Styles);
            if (resolvedStyles.Count > 0)
                vm.Styles = resolvedStyles;

            // Typed series (COMBO)
            if (vStmt.TypedSeries.Count > 0)
                vm.SeriesDefs = vStmt.TypedSeries.Select(ts => new SeriesDefManifest { SeriesType = ts.SeriesType, Column = ts.Column }).ToList();

            // Overlay definitions
            if (vStmt.Overlays.Count > 0)
                vm.Overlays = vStmt.Overlays.Select(o => new OverlayManifest
                {
                    OverlayType = o.OverlayType.ToString(),
                    Parameter   = o.Parameter,
                    LineStyle   = o.LineStyle.ToString().ToLowerInvariant(),
                    Color       = o.Color,
                    Label       = o.Label
                }).ToList();

            // Conditional formatting rules (TABLE)
            if (vStmt.FormattingRules.Count > 0)
                vm.FormattingRules = vStmt.FormattingRules.Select(r => new FormattingRuleManifest
                {
                    Condition = r.Condition.ToSql(),
                    Color     = r.Color
                }).ToList();

            // Axis options
            foreach (var axis in vStmt.AxisOptions)
            {
                var prefix = "axis:" + axis.Axis.ToLowerInvariant() + ":";
                foreach (var opt in axis.Options)
                    vm.Options[prefix + opt.Key.ToLowerInvariant()] = opt.Value;
            }

            // Mappings
            foreach (var mapping in vStmt.Mappings)
                vm.Options["mapping:" + mapping.Role.ToLowerInvariant()] = mapping.Column;

            // Actions
            foreach (var action in vStmt.Actions)
            {
                vm.Actions.Add(action switch
                {
                    DrillDownAction dd => new VisualActionManifest
                    {
                        Type         = "DRILL_DOWN",
                        Trigger      = dd.Trigger,
                        TargetVisual = dd.TargetVisual,
                        KeyColumn    = dd.KeyColumn
                    },
                    SetParameterAction sp => new VisualActionManifest
                    {
                        Type            = "SET_PARAMETER",
                        Trigger         = sp.Trigger,
                        ParameterName   = sp.ParameterName,
                        ValueExpression = sp.ValueExpression
                    },
                    _ => throw new NotSupportedException($"Action type {action.GetType().Name} not supported in manifest.")
                });
            }

            try
            {
                await FetchDataAsync(vStmt, vm);
                CalculateSummaries(vStmt, vm);
                vm.ChartConfig = renderer.Render(vm);
            }
            catch (Exception ex)
            {
                vm.Error = ex.Message;
            }

            return vm;
        }

        private async Task FetchDataAsync(CreateVisualStatement vStmt, VisualManifest vm)
        {
            Statement queryStmt;
            if (vStmt.Source.IsInlineSelect && vStmt.Source.InlineSelect != null)
            {
                queryStmt = vStmt.Source.InlineSelect;
            }
            else if (vStmt.Source.TempTableName != null)
            {
                var tableRef = new TableReference(vStmt.Source.TempTableName);
                queryStmt = new SelectStatement(
                    new List<SelectColumn> { new SelectColumn(new IdentifierExpression("*")) },
                    null, tableRef, new List<JoinClause>(), null);
            }
            else return;

            bool firstBatch = true;
            await foreach (var batch in ctx.ExecuteQuery(queryStmt))
            {
                if (firstBatch)
                {
                    vm.Columns = batch.ColumnNames.ToList();
                    firstBatch = false;
                }
                foreach (var row in batch.Rows)
                {
                    vm.Rows.Add(vm.Columns.Select(c => row[c]?.ToString()).ToList());
                    
                    // Apply formatting rules row-by-row
                    if (vStmt.FormattingRules.Count > 0)
                    {
                        if (vm.RowStyles == null) vm.RowStyles = new List<string?>();
                        string? matchedColor = null;
                        foreach (var rule in vStmt.FormattingRules)
                        {
                            if (await ctx.EvaluateCondition(rule.Condition, row))
                            {
                                matchedColor = rule.Color;
                                break;
                            }
                        }
                        vm.RowStyles.Add(matchedColor);
                    }
                }
            }
        }

        private void CalculateSummaries(CreateVisualStatement vStmt, VisualManifest vm)
        {
            if (vm.Rows.Count == 0 || vm.Columns.Count == 0) return;
            if (vStmt.Summaries.Count == 0 && (vStmt.SummaryOptions == null || 
                (!vStmt.SummaryOptions.GrandTotalRow && !vStmt.SummaryOptions.GrandTotalColumn && 
                 !vStmt.SummaryOptions.SummarizeRow && !vStmt.SummaryOptions.SummarizeColumn)))
                return;

            var summaryData = new TableSummaryData();
            vm.SummaryData = summaryData;

            // 1. Specific Aggregates
            foreach (var item in vStmt.Summaries)
            {
                var colIndex = vm.Columns.FindIndex(c => c.Equals(item.Column, StringComparison.OrdinalIgnoreCase));
                if (colIndex < 0) continue;

                var value = ComputeAggregate(vm.Rows, colIndex, item.Aggregate);
                summaryData.Aggregates.Add(new SummaryItemData
                {
                    Column = item.Column,
                    Aggregate = item.Aggregate,
                    Value = value,
                    Alias = item.Alias
                });
            }

            // 2. Grand Totals (if enabled, compute SUM for all numeric columns or specific ones)
            if (vStmt.SummaryOptions != null && (vStmt.SummaryOptions.GrandTotalRow || vStmt.SummaryOptions.SummarizeColumn))
            {
                summaryData.GrandTotals = new Dictionary<string, string>();
                var colsToSummarize = vStmt.SummaryOptions.SpecificColumns ?? vm.Columns;

                foreach (var colName in colsToSummarize)
                {
                    var colIndex = vm.Columns.FindIndex(c => c.Equals(colName, StringComparison.OrdinalIgnoreCase));
                    if (colIndex < 0) continue;

                    // Only SUM numeric looking columns for Grand Totals automatically
                    var sum = ComputeAggregate(vm.Rows, colIndex, "SUM");
                    if (sum != "0" || IsNumericColumn(vm.Rows, colIndex))
                    {
                        summaryData.GrandTotals[colName] = sum;
                    }
                }
            }
        }

        private string ComputeAggregate(List<List<string?>> rows, int colIndex, string aggregate)
        {
            var values = rows.Select(r => r[colIndex]).Where(v => v != null).ToList();
            if (values.Count == 0) return "0";

            switch (aggregate.ToUpperInvariant())
            {
                case "COUNT":
                    return values.Count.ToString();
                case "SUM":
                case "AVG":
                case "MIN":
                case "MAX":
                    var decimals = values.Select(v => decimal.TryParse(v, out var d) ? d : (decimal?)null).Where(d => d != null).Select(d => d!.Value).ToList();
                    if (decimals.Count == 0) return values.Count > 0 && (aggregate == "MIN" || aggregate == "MAX") ? values.OrderBy(v => v).First()! : "0";

                    return aggregate.ToUpperInvariant() switch
                    {
                        "SUM" => decimals.Sum().ToString("G29"),
                        "AVG" => decimals.Average().ToString("G29"),
                        "MIN" => decimals.Min().ToString("G29"),
                        "MAX" => decimals.Max().ToString("G29"),
                        _ => "0"
                    };
                default:
                    return "0";
            }
        }

        private bool IsNumericColumn(List<List<string?>> rows, int colIndex)
        {
            var sample = rows.Take(10).Select(r => r[colIndex]).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            return decimal.TryParse(sample, out _);
        }

        private (string? Value, bool IsMarkdown) ResolveMarkdown(string? input, bool parserFlag) => styleBuilder.ResolveMarkdown(input, parserFlag);
    }
}
