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
            var vm = new VisualManifest
            {
                Name         = name,
                VisualType   = vStmt.VisualType.ToString(),
                DefaultValue = vStmt.DefaultValue,
                Tooltip      = styleBuilder.BuildTooltipManifest(vStmt.Tooltip)
            };

            // Copy flat options
            foreach (var opt in vStmt.Options)
                vm.Options[opt.Key] = opt.Value;

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
                    Column    = r.Column,
                    Operator  = r.Operator,
                    Threshold = r.Threshold,
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
                }
            }
        }

    }
}
