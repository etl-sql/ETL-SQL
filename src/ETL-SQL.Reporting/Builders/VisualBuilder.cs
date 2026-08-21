using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Reporting.Builders
{
    public class VisualBuilder(IExecutionContext ctx, EChartsRenderer renderer, StyleBuilder styleBuilder)
    {
        public async Task<VisualManifest> BuildAsync(string name, CreateVisualStatement vStmt, Dictionary<string, string>? interactionValues = null, bool skipDeferredVisuals = false, VisualDrillState? drillState = null)
        {
            var (title, titleMd) = await styleBuilder.ResolveMarkdownAsync(vStmt.Title, vStmt.TitleIsMarkdown);
            var (subtitle, subtitleMd) = await styleBuilder.ResolveMarkdownAsync(vStmt.Subtitle, vStmt.SubtitleIsMarkdown);
            var (defVal, _) = await styleBuilder.ResolveMarkdownAsync(vStmt.DefaultValue);
            var (placeholder, _) = await styleBuilder.ResolveMarkdownAsync(vStmt.Placeholder);

            var vm = new VisualManifest
            {
                Name = name,
                VisualType = vStmt.VisualType.ToString().ToUpperInvariant(),
                Fetch = vStmt.FetchMode switch
                {
                    VisualFetchMode.OnLoad => "ON_LOAD",
                    VisualFetchMode.OnRun => "ON_RUN",
                    _ => "AUTO"
                },
                DefaultValue = defVal,
                LabelPosition = vStmt.LabelPosition,
                Min = vStmt.Min,
                Max = vStmt.Max,
                Decimals = vStmt.Decimals,
                Placeholder = placeholder,
                TitleIsMarkdown = titleMd,
                SubtitleIsMarkdown = subtitleMd,
                IsMarkdown = vStmt.VisualType == VisualType.Text || vStmt.VisualType == VisualType.Textbox,
                Tooltip = await styleBuilder.BuildTooltipManifestAsync(vStmt.Tooltip),
                PrintLayout = vStmt.PrintLayout == null ? null : new PrintLayoutOverrideManifest
                {
                    PageBreakBefore = vStmt.PrintLayout.PageBreakBefore,
                    PageBreakAfter = vStmt.PrintLayout.PageBreakAfter,
                    KeepTogether = vStmt.PrintLayout.KeepTogether,
                    ExcludeFromPrint = vStmt.PrintLayout.ExcludeFromPrint
                },
                RowDetail = vStmt.RowDetail == null ? null : new RowDetailManifest
                {
                    TargetName = vStmt.RowDetail.TargetName,
                    Limit = vStmt.RowDetail.Limit,
                    Bindings = vStmt.RowDetail.Bindings.Select(b => new RowDetailBindingManifest
                    {
                        ParentColumn = b.ParentColumn,
                        ChildParameter = b.ChildParameter
                    }).ToList()
                }
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

            if (vStmt.Interactions.Count > 0)
            {
                vm.Interactions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var interaction in vStmt.Interactions)
                    vm.Interactions[interaction.Key] = interaction.Value;
            }

            // Styles
            var resolvedStyles = styleBuilder.ResolveStyles(vStmt.StyleName, vStmt.Styles);
            if (resolvedStyles.Count > 0)
                vm.Styles = resolvedStyles;

            // Extract from styles if missing
            vm.Styles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (vm.LabelPosition == null && vm.Styles.TryGetValue("LABEL_POSITION", out var lp))
                vm.LabelPosition = lp;

            // Default EXPORT=OFF for controls
            if (!vm.Styles.ContainsKey("EXPORT") && IsControlVisual(vStmt.VisualType))
                vm.Styles["EXPORT"] = "OFF";

            // Typed series (COMBO)
            if (vStmt.TypedSeries.Count > 0)
                vm.SeriesDefs = vStmt.TypedSeries.Select(ts => new SeriesDefManifest { SeriesType = ts.SeriesType, Column = ts.Column }).ToList();

            // Overlay definitions
            if (vStmt.Overlays.Count > 0)
                vm.Overlays = vStmt.Overlays.Select(o => new OverlayManifest
                {
                    OverlayType = o.OverlayType.ToString(),
                    Parameter = o.Parameter,
                    LineStyle = o.LineStyle.ToString().ToLowerInvariant(),
                    Color = o.Color,
                    Label = o.Label
                }).ToList();

            // Conditional formatting rules (TABLE)
            if (vStmt.FormattingRules.Count > 0)
            {
                vm.RowStyles = new List<string?>();
                if (vStmt.FormattingRules.Any(r => r.FontColor != null))
                    vm.RowFontStyles = new List<string?>();
            }

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
                        Type = "DRILL_DOWN",
                        Trigger = dd.Trigger,
                        TargetVisual = dd.TargetVisual,
                        KeyColumns = dd.KeyColumns
                    },
                    SetParameterAction sp => new VisualActionManifest
                    {
                        Type = "SET_PARAMETER",
                        Trigger = sp.Trigger,
                        ParameterName = sp.ParameterName,
                        ValueExpression = sp.ValueExpression
                    },
                    RunScriptAction rs => new VisualActionManifest
                    {
                        Type = "RUN_SCRIPT",
                        Trigger = rs.Trigger,
                        ScriptPath = rs.ScriptPath,
                        Parameters = rs.Parameters
                    },
                    ClearFiltersAction cf => new VisualActionManifest
                    {
                        Type = "CLEAR_FILTERS",
                        Trigger = cf.Trigger
                    },
                    ReportCommandAction command => new VisualActionManifest
                    {
                        Type = command.Command,
                        Trigger = command.Trigger
                    },
                    DrillInAction di => new VisualActionManifest
                    {
                        Type = "DRILL_IN",
                        Trigger = di.Trigger,
                        Hierarchy = di.Hierarchy
                    },
                    DrillReportAction dr => new VisualActionManifest
                    {
                        Type = "DRILL_REPORT",
                        Trigger = dr.Trigger,
                        TargetReport = dr.TargetReport,
                        Parameters = dr.Parameters
                    },
                    RefreshVisualsAction rv => new VisualActionManifest
                    {
                        Type = "REFRESH_VISUALS",
                        Trigger = rv.Trigger,
                        Targets = rv.Targets
                    },
                    _ => throw new NotSupportedException($"Action type {action.GetType().Name} not supported in manifest.")
                });
            }

            // MAP_FILE validation: resolve and verify the file exists at build time.
            if (vStmt.VisualType == VisualType.Map &&
                vStmt.Options.FirstOrDefault(o => o.Key.Equals("MAP_FILE", StringComparison.OrdinalIgnoreCase)) is { } mapFileOpt &&
                !string.IsNullOrWhiteSpace(mapFileOpt.Value))
            {
                try
                {
                    var resolved = ctx.ResolvePath(mapFileOpt.Value);
                    if (!File.Exists(resolved))
                        vm.Error = $"MAP_FILE not found: {mapFileOpt.Value}";
                }
                catch (Exception ex)
                {
                    vm.Error = $"MAP_FILE path error: {ex.Message}";
                }
            }

            var deferredHidden = skipDeferredVisuals;
            vm.IsHidden = deferredHidden;

            if (vm.Error == null && !deferredHidden)
            {
                try
                {
                    await FetchDataAsync(vStmt, vm, interactionValues);

                    if (drillState != null && drillState.Path.Count < drillState.Hierarchy.Length)
                    {
                        var currentLevel = drillState.Hierarchy[drillState.Path.Count];
                        (vm.Rows, vm.Columns) = ApplyDrillAggregation(vm.Rows, vm.Columns, drillState);
                        vm.Options["mapping:x"] = currentLevel;
                        vm.DrillState = new VisualDrillStateManifest
                        {
                            Hierarchy = drillState.Hierarchy,
                            Path = drillState.Path.Select(s => new DrillPathSegment { Column = s.Column, Value = s.Value }).ToList(),
                            CurrentLevel = currentLevel,
                            CanDrillUp = drillState.Path.Count > 0
                        };
                        // Aggregation changed row shape; rebuild typed values from the resolved display rows.
                        vm.RawRows.Clear();
                    }

                    ResolveActionValues(vm);
                    CalculateSummaries(vStmt, vm);
                    if (vStmt.VisualType == VisualType.Table && vStmt.Mappings.Count > 0)
                        ApplyTableMappings(vStmt, vm);
                    await BuildMicroChartsAsync(vStmt, vm);
                    if (NamedVisualChartLowerer.Supports(vStmt.VisualType))
                    {
                        vm.ChartSpec = new NamedVisualChartLowerer().Lower(vStmt, vm);
                        vm.ChartData = new VisualChartDataBuilder().Build(vm.ChartSpec, vm);
                        vm.PlotPlan = new PlotPlanResolver().Resolve(vm.ChartSpec, vm.ChartData);
                    }
                    vm.SemanticFallback = VisualSemanticFallbackBuilder.Build(vm);
                    vm.ChartConfig = renderer.Render(vm);
                }
                catch (Exception ex)
                {
                    vm.Error = ex.Message;
                }
            }

            return vm;
        }

        private void ResolveActionValues(VisualManifest vm)
        {
            foreach (var action in vm.Actions)
            {
                if (action.Type == "SET_PARAMETER")
                {
                    ResolveSetParameterAction(action, vm.Columns);
                }
                else if (action.Type == "DRILL_REPORT")
                {
                    var matchingColumn = FindColumn(vm.Columns, action.TargetReport ?? string.Empty);
                    if (matchingColumn != null)
                    {
                        action.ValueSource = "COLUMN";
                        action.ValueColumn = matchingColumn;
                    }
                    else
                    {
                        action.ValueSource = "LITERAL";
                        action.LiteralValue = action.TargetReport;
                    }

                    if (action.Parameters is { Count: > 0 })
                    {
                        action.ParameterColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        action.LiteralParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var (name, expression) in action.Parameters)
                        {
                            var matchingParamColumn = FindColumn(vm.Columns, expression);
                            if (matchingParamColumn != null)
                                action.ParameterColumns[name] = matchingParamColumn;
                            else
                            {
                                // Remove quotes if present for literal values
                                var val = expression;
                                if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                                    val = val.Substring(1, val.Length - 2);
                                action.LiteralParameters[name] = val;
                            }
                        }
                    }
                }
                else if (action.Type == "RUN_SCRIPT" && action.Parameters is { Count: > 0 })
                {
                    action.ParameterColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    action.LiteralParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var (name, expression) in action.Parameters)
                    {
                        var matchingColumn = FindColumn(vm.Columns, expression);
                        if (matchingColumn != null)
                            action.ParameterColumns[name] = matchingColumn;
                        else
                        {
                            // Remove quotes if present for literal values
                            var val = expression;
                            if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                                val = val.Substring(1, val.Length - 2);
                            action.LiteralParameters[name] = val;
                        }
                    }
                }
            }
        }

        private static (List<List<string?>> rows, List<string> columns) ApplyDrillAggregation(
            List<List<string?>> rows, List<string> columns, VisualDrillState state)
        {
            var hierarchy = state.Hierarchy;
            var path = state.Path;
            var currentLevel = hierarchy[path.Count];

            // Filter: keep only rows matching the full drill path
            var filtered = rows.Where(r => path.All(seg =>
            {
                var idx = columns.FindIndex(c => c.Equals(seg.Column, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 && (r[idx] ?? "") == seg.Value;
            })).ToList();

            // Group by the current level column
            var levelIdx = columns.FindIndex(c => c.Equals(currentLevel, StringComparison.OrdinalIgnoreCase));
            if (levelIdx < 0) return (filtered, columns);

            var hierSet = new HashSet<string>(hierarchy, StringComparer.OrdinalIgnoreCase);
            var measureIdx = columns.Select((c, i) => (c, i))
                                    .Where(t => !hierSet.Contains(t.c))
                                    .Select(t => t.i)
                                    .ToList();

            var grouped = filtered
                .GroupBy(r => r[levelIdx] ?? "")
                .Select(g =>
                {
                    var row = new List<string?>(new string?[columns.Count]);
                    row[levelIdx] = g.Key;
                    foreach (var mi in measureIdx)
                    {
                        var sum = g.Sum(r =>
                        {
                            var v = r[mi];
                            return decimal.TryParse(v, out var d) ? d : 0m;
                        });
                        row[mi] = sum.ToString();
                    }
                    return row;
                })
                .ToList();

            return (grouped, columns);
        }

        private static void ResolveSetParameterAction(VisualActionManifest action, List<string> columns)
        {
            var expression = action.ValueExpression ?? string.Empty;
            if (expression.Equals("VALUE", StringComparison.OrdinalIgnoreCase))
            {
                action.ValueSource = "CONTROL_VALUE";
                return;
            }

            var matchingColumn = FindColumn(columns, expression);
            if (matchingColumn != null)
            {
                action.ValueSource = "COLUMN";
                action.ValueColumn = matchingColumn;
                return;
            }

            action.ValueSource = "LITERAL";
            action.LiteralValue = expression;
        }

        private static string? FindColumn(List<string> columns, string columnName)
            => columns.FirstOrDefault(c => c.Equals(columnName, StringComparison.OrdinalIgnoreCase));

        private async Task FetchDataAsync(CreateVisualStatement vStmt, VisualManifest vm, Dictionary<string, string>? interactionValues)
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

            var actionType = vm.Interactions != null && vm.Interactions.TryGetValue("ON_SELECT", out var action)
                ? action.ToUpperInvariant()
                : null;
            if (vm.VisualType == "TABLE" || vm.VisualType == "SLICER") actionType ??= "FILTER";
            actionType ??= "HIGHLIGHT";

            if (interactionValues != null && interactionValues.Count > 0)
            {
                if (actionType == "HIGHLIGHT")
                {
                    // 1. Fetch Universe (Current Context / Slicer State)
                    await ExecuteAndPopulateRowsAsync(queryStmt, vStmt, vm.Rows, vm.RowStyles, vm);

                    // 2. Temporarily set interaction variables and fetch Selection
                    var backup = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (var kvp in interactionValues)
                        {
                            if (InteractionVariableApplies(queryStmt, kvp.Key) && ctx.VarContext.ContainsVariable(kvp.Key))
                            {
                                backup[kvp.Key] = ctx.VarContext.GetVariable(kvp.Key);
                                ctx.VarContext.SetVariable(kvp.Key, kvp.Value);
                            }
                        }
                        // Only compute HighlightRows when at least one interaction variable was
                        // actually injected. If none matched, the query would run unchanged and
                        // return all rows — client would see everything "selected" and apply no
                        // ghosting. Leaving HighlightRows null signals the client to ghost all
                        // bars instead (cross-filter active, dimension mismatch).
                        if (backup.Count > 0)
                        {
                            vm.HighlightRows = new List<List<string?>>();
                            await ExecuteAndPopulateRowsAsync(queryStmt, vStmt, vm.HighlightRows, null, vm);
                        }
                    }
                    finally
                    {
                        foreach (var kvp in backup)
                            ctx.VarContext.SetVariable(kvp.Key, kvp.Value);
                    }
                }
                else if (actionType == "FILTER")
                {
                    // Filter Mode: Temporarily set variables and do a single fetch
                    var backup = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (var kvp in interactionValues)
                        {
                            if (InteractionVariableApplies(queryStmt, kvp.Key) && ctx.VarContext.ContainsVariable(kvp.Key))
                            {
                                backup[kvp.Key] = ctx.VarContext.GetVariable(kvp.Key);
                                ctx.VarContext.SetVariable(kvp.Key, kvp.Value);
                            }
                        }
                        await ExecuteAndPopulateRowsAsync(queryStmt, vStmt, vm.Rows, vm.RowStyles, vm);
                    }
                    finally
                    {
                        foreach (var kvp in backup)
                            ctx.VarContext.SetVariable(kvp.Key, kvp.Value);
                    }
                }
                else
                {
                    // NONE: Ignore interaction, use original state
                    await ExecuteAndPopulateRowsAsync(queryStmt, vStmt, vm.Rows, vm.RowStyles, vm);
                }
            }
            else
            {
                // Standard fetch (no interaction context provided)
                await ExecuteAndPopulateRowsAsync(queryStmt, vStmt, vm.Rows, vm.RowStyles, vm);
            }
        }

        private static bool InteractionVariableApplies(Statement queryStmt, string variableName)
        {
            if (!variableName.StartsWith("@", StringComparison.Ordinal))
                variableName = "@" + variableName;

            return ParameterScanner.Scan(queryStmt).Contains(variableName);
        }

        private async Task ExecuteAndPopulateRowsAsync(Statement queryStmt, CreateVisualStatement vStmt, List<List<string?>> targetRows, List<string?>? targetStyles, VisualManifest vm)
        {
            bool firstBatch = true;
            await foreach (var batch in ctx.ExecuteQuery(queryStmt))
            {
                if (firstBatch)
                {
                    vm.Columns = batch.ColumnNames.ToList();
                    if (targetRows == vm.Rows && vStmt.RowDetail != null && vStmt.RowDetail.Bindings.Count > 0)
                    {
                        vm.RowDetailKeys = new List<Dictionary<string, object?>>();
                    }
                    firstBatch = false;
                }
                foreach (var row in batch.Rows)
                {
                    targetRows.Add(vm.Columns.Select(c => row[c]?.ToString()).ToList());
                    if (targetRows == vm.Rows)
                    {
                        vm.RawRows.Add(vm.Columns.ToDictionary(
                            column => column,
                            column => row[column],
                            StringComparer.OrdinalIgnoreCase));
                    }

                    if (targetRows == vm.Rows && vm.RowDetailKeys != null && vStmt.RowDetail != null)
                    {
                        var keys = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var b in vStmt.RowDetail.Bindings)
                        {
                            keys[b.ChildParameter] = batch.ColumnNames.Contains(b.ParentColumn, StringComparer.OrdinalIgnoreCase)
                                ? row[b.ParentColumn]
                                : null;
                        }
                        vm.RowDetailKeys.Add(keys);
                    }

                    // Apply formatting rules row-by-row (only if styles list is provided)
                    if (targetStyles != null && vStmt.FormattingRules.Count > 0)
                    {
                        string? matchedColor = null;
                        string? matchedFontColor = null;
                        foreach (var rule in vStmt.FormattingRules)
                        {
                            if (await ctx.EvaluateCondition(rule.Condition, row))
                            {
                                matchedColor = rule.Color;
                                matchedFontColor = rule.FontColor;
                                break;
                            }
                        }
                        targetStyles.Add(matchedColor);
                        vm.RowFontStyles?.Add(matchedFontColor);
                    }
                }
            }
        }


        private void ApplyTableMappings(CreateVisualStatement vStmt, VisualManifest vm)
        {
            // Build case-insensitive source column index
            var colIdx = vm.Columns
                .Select((c, i) => (c, i))
                .ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase);

            // Build selected list: regular column mappings + sparkline virtual columns.
            // Each entry carries a row-value extractor to keep the row rewrite uniform.
            var selected = new List<(int srcIdx, string display, VisualMapping m, Func<List<string?>, string?> extract)>();
            foreach (var m in vStmt.Mappings)
            {
                if (m.SparklineColumns is { Count: > 0 })
                {
                    var indices = m.SparklineColumns
                        .Select(sc => colIdx.TryGetValue(sc, out var i) ? i : -1)
                        .ToArray();
                    var capturedIndices = indices;
                    selected.Add((-1, m.DisplayName ?? "Trend", m,
                        row => "[" + string.Join(",", capturedIndices.Select(
                            i => i >= 0 && i < row.Count ? (row[i] ?? "null") : "null")) + "]"));
                }
                else if (colIdx.TryGetValue(m.Column, out var si))
                {
                    var capturedSi = si;
                    selected.Add((si, m.DisplayName ?? m.Column, m,
                        row => capturedSi < row.Count ? row[capturedSi] : null));
                }
            }

            if (selected.Count == 0) return;

            // Build rename map for summary keys (regular columns only)
            var rename = selected
                .Where(x => x.srcIdx >= 0)
                .ToDictionary(
                    x => vm.Columns[x.srcIdx],
                    x => x.display,
                    StringComparer.OrdinalIgnoreCase);

            // Rewrite rows (filter + reorder + inject sparkline values)
            for (int r = 0; r < vm.Rows.Count; r++)
            {
                var old = vm.Rows[r];
                vm.Rows[r] = selected.Select(x => x.extract(old)).ToList();
            }
            if (vm.HighlightRows != null)
            {
                for (int r = 0; r < vm.HighlightRows.Count; r++)
                {
                    var old = vm.HighlightRows[r];
                    vm.HighlightRows[r] = selected.Select(x => x.extract(old)).ToList();
                }
            }

            // Rename columns
            vm.Columns = selected.Select(x => x.display).ToList();

            // Rename summary grand-total keys to match new display names
            if (vm.SummaryData?.GrandTotals != null)
            {
                var newTotals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in vm.SummaryData.GrandTotals)
                    if (rename.TryGetValue(k, out var nk)) newTotals[nk] = v;
                vm.SummaryData.GrandTotals = newTotals;
            }

            // Precompute per-column numeric min/max for DATA_BAR and COLOR_SCALE
            var colMinMax = new Dictionary<int, (double Min, double Max)>(); // key = selected index
            for (int si = 0; si < selected.Count; si++)
            {
                var m = selected[si].m;
                if (!m.DataBar && m.ColorScaleFrom == null) continue;
                if (m.SparklineColumns != null) continue; // sparkline columns carry JSON, not numeric

                double cmin = double.MaxValue, cmax = double.MinValue;
                foreach (var row in vm.Rows)
                {
                    if (si < row.Count && row[si] != null && double.TryParse(row[si], out var d))
                    {
                        if (d < cmin) cmin = d;
                        if (d > cmax) cmax = d;
                    }
                }
                if (cmin <= cmax) colMinMax[si] = (cmin, cmax);
            }

            // Build column meta (format, align, data bar, color scale, cell renderer)
            var metas = selected.Select((x, si) =>
            {
                var m = x.m;
                var hasAny = m.Format != null || m.Align != null || m.DataBar || m.ColorScaleFrom != null || m.CellRenderer != null || m.SparklineColumns != null || m.ProgressBar || m.Hidden;
                if (!hasAny) return (ColumnMetaManifest?)null;
                var meta = new ColumnMetaManifest { Format = m.Format, Align = m.Align, Hidden = m.Hidden };
                if (m.DataBar)
                {
                    meta.DataBar = true;
                    meta.DataBarColor = m.DataBarColor;
                    if (colMinMax.TryGetValue(si, out var mm)) { meta.DataBarMin = mm.Min; meta.DataBarMax = mm.Max; }
                }
                if (m.ColorScaleFrom != null)
                {
                    meta.ColorScaleFrom = m.ColorScaleFrom;
                    meta.ColorScaleTo = m.ColorScaleTo;
                    if (colMinMax.TryGetValue(si, out var mm)) { meta.ColorScaleMin = mm.Min; meta.ColorScaleMax = mm.Max; }
                }
                if (m.CellRenderer != null)
                {
                    meta.CellRenderer = m.CellRenderer;
                    meta.ImageWidth = m.ImageWidth;
                    meta.HyperlinkLabel = m.HyperlinkLabel;
                }
                if (m.SparklineColumns != null)
                {
                    meta.CellRenderer = "sparkline";
                    meta.SparklineType = m.SparklineType ?? "line";
                }
                if (m.ProgressBar)
                    meta.CellRenderer = "progress";
                return (ColumnMetaManifest?)meta;
            }).ToList();

            if (metas.Any(m => m != null))
                vm.ColumnMeta = metas;
        }

        private async Task BuildMicroChartsAsync(CreateVisualStatement statement, VisualManifest manifest)
        {
            var factory = new MicroChartPlanFactory();
            if (statement.VisualType == VisualType.Table)
            {
                for (var columnIndex = 0; columnIndex < manifest.Columns.Count; columnIndex++)
                {
                    var mapping = statement.Mappings.FirstOrDefault(candidate =>
                        (candidate.DisplayName ?? (candidate.SparklineColumns is { Count: > 0 } ? "Trend" : candidate.Column))
                            .Equals(manifest.Columns[columnIndex], StringComparison.OrdinalIgnoreCase));
                    if (mapping is null) continue;
                    for (var rowIndex = 0; rowIndex < manifest.Rows.Count; rowIndex++)
                    {
                        var source = manifest.Rows[rowIndex].ElementAtOrDefault(columnIndex);
                        MicroChartSemanticBundle? bundle = null;
                        var kind = string.Empty;
                        if (mapping.SparklineColumns is { Count: > 0 })
                        {
                            var vectorValues = ParseVector(source);
                            bundle = factory.CreateSparkline($"{manifest.Name}-r{rowIndex}-c{columnIndex}", vectorValues,
                                mapping.SparklineType ?? "line");
                            kind = "sparkline";
                        }
                        else if (mapping.ProgressBar && decimal.TryParse(source, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                        {
                            bundle = factory.CreateProgress($"{manifest.Name}-r{rowIndex}-c{columnIndex}", value,
                                mapping.ProgressMinimum ?? 0m, mapping.ProgressMaximum ?? 1m, mapping.ProgressColor);
                            kind = "progress";
                        }
                        if (bundle is null) continue;
                        manifest.MicroCharts ??= [];
                        manifest.MicroCharts.Add(factory.ToManifest(bundle, kind, "table.cell", rowIndex, columnIndex, source));
                    }
                }
            }

            if (statement.VisualType != VisualType.Card) return;
            var sparkline = statement.Mappings.FirstOrDefault(mapping => mapping.SparklineSource is not null);
            if (sparkline?.SparklineSource is null || sparkline.SparklineYColumn is null) return;
            var sourceStatement = new SelectStatement(
                [new SelectColumn(new IdentifierExpression("*"))], null,
                new TableReference(sparkline.SparklineSource), [], null);
            var values = new List<decimal?>();
            var labels = new List<string?>();
            await foreach (var batch in ctx.ExecuteQuery(sourceStatement))
            {
                foreach (var row in batch.Rows)
                {
                    values.Add(row.TryGetValue(sparkline.SparklineYColumn, out var raw) &&
                        decimal.TryParse(raw?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null);
                    labels.Add(sparkline.SparklineXColumn is not null && row.TryGetValue(sparkline.SparklineXColumn, out var label)
                        ? label?.ToString() : null);
                }
            }
            var cardBundle = factory.CreateSparkline($"{manifest.Name}-sparkline", values,
                sparkline.SparklineType ?? "line", labels: labels);
            manifest.MicroCharts ??= [];
            manifest.MicroCharts.Add(factory.ToManifest(cardBundle, "sparkline", "card.sparkline"));
        }

        private static List<decimal?> ParseVector(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return [];
            try
            {
                using var document = JsonDocument.Parse(source);
                return document.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var value)
                    ? (decimal?)value
                    : decimal.TryParse(item.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null).ToList();
            }
            catch (JsonException)
            {
                return [];
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

        private bool IsControlVisual(VisualType type)
        {
            return type == VisualType.Slicer ||
                   type == VisualType.DatePicker ||
                   type == VisualType.RelDatePicker ||
                   type == VisualType.Slider ||
                   type == VisualType.MultiSelect ||
                   type == VisualType.Search ||
                   type == VisualType.Checkbox ||
                   type == VisualType.Textbox ||
                   type == VisualType.Numberbox;
        }

    }
}
