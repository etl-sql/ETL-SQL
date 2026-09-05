using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Reporting.Builders
{
    public class VisualBuilder(IExecutionContext ctx, StyleBuilder styleBuilder)
    {
        public async Task<VisualManifest> BuildAsync(string name, CreateVisualStatement vStmt, Dictionary<string, string>? interactionValues = null, bool skipDeferredVisuals = false, VisualDrillState? drillState = null, ImmutableArray<string> inheritedPalette = default, IReadOnlyDictionary<string, string>? inheritedStyles = null)
        {
            var expressionBackups = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            string? title;
            bool titleMd;
            string? subtitle;
            bool subtitleMd;
            string? defVal;
            string? placeholder;
            try
            {
                foreach (var pair in interactionValues ?? [])
                {
                    var variableName = pair.Key.StartsWith('@') ? pair.Key : '@' + pair.Key;
                    if (!ctx.VarContext.ContainsVariable(variableName)) continue;
                    expressionBackups[variableName] = ctx.VarContext.GetVariable(variableName);
                    ctx.VarContext.SetVariable(variableName, pair.Value);
                }

                var titleExpr = vStmt.TitleDefinition?.Text ?? vStmt.Title;
                var titleIsMd = vStmt.TitleDefinition?.IsMarkdown ?? vStmt.TitleIsMarkdown;
                (title, titleMd) = await styleBuilder.ResolveMarkdownAsync(titleExpr, titleIsMd);

                var subtitleExpr = vStmt.SubtitleDefinition?.Text ?? vStmt.Subtitle;
                var subtitleIsMd = vStmt.SubtitleDefinition?.IsMarkdown ?? vStmt.SubtitleIsMarkdown;
                (subtitle, subtitleMd) = await styleBuilder.ResolveMarkdownAsync(subtitleExpr, subtitleIsMd);
                (defVal, _) = await styleBuilder.ResolveMarkdownAsync(vStmt.DefaultValue);
                if (defVal == null && vStmt.Options.Any(o => o.Key.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase)))
                {
                    defVal = vStmt.Options.First(o => o.Key.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase)).Value;
                }
                (placeholder, _) = await styleBuilder.ResolveMarkdownAsync(vStmt.Placeholder);
            }
            finally
            {
                foreach (var pair in expressionBackups)
                    ctx.VarContext.SetVariable(pair.Key, pair.Value);
            }

            return await BuildResolvedAsync(name, vStmt, interactionValues, skipDeferredVisuals, drillState,
                title, titleMd, subtitle, subtitleMd, defVal, placeholder, inheritedPalette, inheritedStyles);
        }

        private async Task<VisualManifest> BuildResolvedAsync(string name, CreateVisualStatement vStmt,
            Dictionary<string, string>? interactionValues, bool skipDeferredVisuals, VisualDrillState? drillState,
            string? title, bool titleMd, string? subtitle, bool subtitleMd, string? defVal, string? placeholder,
            ImmutableArray<string> inheritedPalette = default, IReadOnlyDictionary<string, string>? inheritedStyles = null)
        {

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
                Tooltip = await styleBuilder.BuildTooltipManifestAsync(vStmt.Tooltip, vStmt.Name),
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
                },
                Cascade = BuildCascadeManifest(vStmt)
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
                        case "LABEL_BACKGROUND": vm.DataLabels.LabelBackground = opt.Value; break;
                        case "LABEL_BORDER": vm.DataLabels.LabelBorder = opt.Value; break;
                        case "LEADER_LINE": vm.DataLabels.LeaderLine = opt.Value.Equals("ON", StringComparison.OrdinalIgnoreCase); break;
                        case "LEADER_LINE:COLOR": vm.DataLabels.LeaderLineColor = opt.Value; break;
                        case "LEADER_LINE:STYLE": vm.DataLabels.LeaderLineStyle = opt.Value; break;
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

            // Title & Subtitle block typography overrides
            if (vStmt.TitleDefinition != null)
            {
                if (vStmt.TitleDefinition.Color != null) resolvedStyles["TITLE_COLOR"] = vStmt.TitleDefinition.Color;
                if (vStmt.TitleDefinition.Size != null) resolvedStyles["TITLE_SIZE"] = vStmt.TitleDefinition.Size;
                if (vStmt.TitleDefinition.Weight != null) resolvedStyles["TITLE_WEIGHT"] = vStmt.TitleDefinition.Weight;
                if (vStmt.TitleDefinition.Font != null) resolvedStyles["TITLE_FONT"] = vStmt.TitleDefinition.Font;
                if (vStmt.TitleDefinition.Align != null) resolvedStyles["TITLE_ALIGN"] = vStmt.TitleDefinition.Align;
            }
            if (vStmt.SubtitleDefinition != null)
            {
                if (vStmt.SubtitleDefinition.Color != null) resolvedStyles["SUBTITLE_COLOR"] = vStmt.SubtitleDefinition.Color;
                if (vStmt.SubtitleDefinition.Size != null) resolvedStyles["SUBTITLE_SIZE"] = vStmt.SubtitleDefinition.Size;
                if (vStmt.SubtitleDefinition.Weight != null) resolvedStyles["SUBTITLE_WEIGHT"] = vStmt.SubtitleDefinition.Weight;
                if (vStmt.SubtitleDefinition.Font != null) resolvedStyles["SUBTITLE_FONT"] = vStmt.SubtitleDefinition.Font;
                if (vStmt.SubtitleDefinition.Align != null) resolvedStyles["SUBTITLE_ALIGN"] = vStmt.SubtitleDefinition.Align;
            }

            var visualExplicitTheme = vStmt.Options.FirstOrDefault(o => o.Key.Equals("THEME", StringComparison.OrdinalIgnoreCase))?.Value
                ?? resolvedStyles.GetValueOrDefault("THEME");

            if (!string.IsNullOrEmpty(visualExplicitTheme))
                resolvedStyles["THEME"] = visualExplicitTheme;

            var effectiveTheme = visualExplicitTheme
                ?? (inheritedStyles != null && inheritedStyles.TryGetValue("THEME", out var inhTheme) ? inhTheme : null);

            if (resolvedStyles.Count > 0)
                vm.Styles = resolvedStyles;

            var resolvedPalette = styleBuilder.ResolvePalette(vStmt.StyleName, vStmt.Palette, inheritedPalette, vStmt.VisualType.ToString(), effectiveTheme);

            // Effective background resolution across visual and inherited styles
            var isDark = string.Equals(resolvedStyles.GetValueOrDefault("THEME"), "DARK", StringComparison.OrdinalIgnoreCase) ||
                         (inheritedStyles != null && inheritedStyles.TryGetValue("THEME", out var ith) && string.Equals(ith, "DARK", StringComparison.OrdinalIgnoreCase));

            string? inhBg = null;
            if (inheritedStyles != null)
            {
                if (!inheritedStyles.TryGetValue("BACKGROUND", out inhBg) &&
                    !inheritedStyles.TryGetValue("BACKGROUND_COLOR", out inhBg))
                {
                    inheritedStyles.TryGetValue("BG", out inhBg);
                }
            }

            var bgCandidate = resolvedStyles.GetValueOrDefault("BACKGROUND")
                ?? resolvedStyles.GetValueOrDefault("BACKGROUND_COLOR")
                ?? resolvedStyles.GetValueOrDefault("BG")
                ?? inhBg
                ?? (isDark ? "#1e1e1e" : "#ffffff");

            var effectiveBg = ColorContrast.TryParseHexColor(bgCandidate, out _, out _, out _, out _)
                ? bgCandidate
                : (isDark ? "#1e1e1e" : "#ffffff");

            var validPalette = new List<string>();
            if (!resolvedPalette.IsDefaultOrEmpty)
            {
                foreach (var color in resolvedPalette)
                {
                    if (!ColorContrast.TryParseHexColor(color, out _, out _, out _, out _) || !DesignTokens.IsSafeCssValue(color))
                    {
                        vm.Diagnostics ??= new List<VisualDiagnosticManifest>();
                        vm.Diagnostics.Add(new VisualDiagnosticManifest
                        {
                            Code = "PALETTE_COLOR_INVALID",
                            Message = $"Palette color '{color}' is not a valid hex color.",
                            Line = vStmt.Line
                        });
                    }
                    else
                    {
                        var eval = ColorContrast.Evaluate(color, effectiveBg, minRatio: 3.0);
                        if (!eval.Passed)
                        {
                            vm.Diagnostics ??= new List<VisualDiagnosticManifest>();
                            vm.Diagnostics.Add(new VisualDiagnosticManifest
                            {
                                Code = "PALETTE_CONTRAST_LOW",
                                Message = $"Palette color '{color}' has insufficient contrast ({eval.Ratio:F2}:1) against background '{effectiveBg}'.",
                                Line = vStmt.Line
                            });
                        }
                        else
                        {
                            validPalette.Add(color);
                        }
                    }
                }
            }

            if (validPalette.Count > 0)
                vm.Palette = validPalette;
            else
                vm.Palette = null;

            // Collect series assignments for --etl-series-* tokens
            var seriesAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in resolvedStyles)
            {
                if (k.StartsWith("COLOR:", StringComparison.OrdinalIgnoreCase))
                {
                    var sName = k.Substring("COLOR:".Length).Trim();
                    if (!string.IsNullOrEmpty(sName))
                    {
                        if (!ColorContrast.TryParseHexColor(v, out _, out _, out _, out _) || !DesignTokens.IsSafeCssValue(v))
                        {
                            vm.Diagnostics ??= new List<VisualDiagnosticManifest>();
                            vm.Diagnostics.Add(new VisualDiagnosticManifest
                            {
                                Code = "COLOR_INVALID",
                                Message = $"Explicit series color '{v}' for series '{sName}' is not a valid hex color.",
                                Line = vStmt.Line
                            });
                        }
                        else
                        {
                            var eval = ColorContrast.Evaluate(v, effectiveBg, minRatio: 3.0);
                            if (!eval.Passed)
                            {
                                vm.Diagnostics ??= new List<VisualDiagnosticManifest>();
                                vm.Diagnostics.Add(new VisualDiagnosticManifest
                                {
                                    Code = "SERIES_CONTRAST_LOW",
                                    Message = $"Explicit series color '{v}' for series '{sName}' has insufficient contrast ({eval.Ratio:F2}:1) against background '{effectiveBg}'.",
                                    Line = vStmt.Line
                                });
                            }
                            else
                            {
                                seriesAssignments[sName] = v;
                            }
                        }
                    }
                }
            }

            if (vStmt.TypedSeries is { Count: > 0 })
            {
                var mappedSeries = vStmt.TypedSeries.Select(s => s.Column).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                for (var i = 0; i < mappedSeries.Count; i++)
                {
                    var sName = mappedSeries[i];
                    if (!seriesAssignments.ContainsKey(sName))
                    {
                        var color = ChartPalette.Resolve(ChartStyleTokens.Build(vm), sName, i);
                        if (ColorContrast.TryParseHexColor(color, out _, out _, out _, out _))
                        {
                            seriesAssignments[sName] = color;
                        }
                    }
                }
            }

            var visualTokens = styleBuilder.ResolveDesignTokens(resolvedStyles, isPageOrReportLevel: false, vm.Palette, seriesAssignments);
            if (visualTokens.Count > 0)
                vm.DesignTokens = visualTokens;

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
                    Label = o.Label,
                    ForecastField = o.ForecastField,
                    ConfidenceLowField = o.ConfidenceLowField,
                    ConfidenceHighField = o.ConfidenceHighField,
                    AnomalyField = o.AnomalyField,
                    BandLow = o.BandLow,
                    BandHigh = o.BandHigh,
                    TableCalculationField = o.TableCalculationField
                }).ToList();

            // Conditional formatting rules
            if (vStmt.FormattingRules.Count > 0)
            {
                if (vStmt.VisualType != VisualType.Table)
                {
                    vm.FormattingRules = vStmt.FormattingRules.Select(r => new FormattingRuleManifest
                    {
                        Condition = r.Condition.ToSql(),
                        Color = r.Color,
                        FontColor = r.FontColor
                    }).ToList();
                }
                vm.RowStyles = new List<string?>();
                if (vStmt.FormattingRules.Any(r => r.FontColor != null))
                    vm.RowFontStyles = new List<string?>();
            }
            if (vStmt.SegmentStyles.Count > 0)
            {
                vm.SegmentRowStyles = new List<SegmentStyleManifest?>();
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
            {
                vm.Options["mapping:" + mapping.Role.ToLowerInvariant()] = mapping.Column;
                if (mapping.DataBar)
                    vm.Options["mapping:" + mapping.Role.ToLowerInvariant() + ":data_bar"] = "true";
                if (!string.IsNullOrWhiteSpace(mapping.DataBarColor))
                    vm.Options["mapping:" + mapping.Role.ToLowerInvariant() + ":data_bar_color"] = mapping.DataBarColor;
            }

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
                        SecondaryParameterName = sp.SecondaryParameterName,
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
                    ApplyParametersAction ap => new VisualActionManifest { Type = "APPLY_PARAMETERS", Trigger = action.Trigger },
                    ApplyBookmarkAction ab => new VisualActionManifest { Type = "APPLY_BOOKMARK", Trigger = action.Trigger, BookmarkName = ab.BookmarkName },
                    SetUiStateAction su => new VisualActionManifest
                    {
                        Type = "SET_UI_STATE",
                        Trigger = action.Trigger,
                        Targets = su.Targets,
                        Key = su.Key,
                        Value = su.Value
                    },
                    NavigatePageAction np => new VisualActionManifest
                    {
                        Type = "NAVIGATE_PAGE",
                        Trigger = action.Trigger,
                        TargetPage = np.TargetPage
                    },
                    ResetParametersAction rp => new VisualActionManifest
                    {
                        Type = "RESET_PARAMETERS",
                        Trigger = action.Trigger,
                        ResetParameters = rp.Parameters.Count > 0 ? rp.Parameters : null
                    },
                    OpenUrlAction ou => new VisualActionManifest
                    {
                        Type = "OPEN_URL",
                        Trigger = action.Trigger,
                        Url = ou.Url,
                        Target = ou.Target
                    },
                    ShowModalAction sm => new VisualActionManifest
                    {
                        Type = "SHOW_MODAL",
                        Trigger = action.Trigger,
                        ModalName = sm.ModalName
                    },
                    HideModalAction hm => new VisualActionManifest
                    {
                        Type = "HIDE_MODAL",
                        Trigger = action.Trigger,
                        ModalName = hm.ModalName
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
                    else
                        vm.ResolvedMapFile = resolved;
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
                    ResolveDynamicMinMax(vm);
                    if (vStmt.VisualType == VisualType.Slider)
                    {
                        ResolveSliderDataTicks(vm);
                    }
                    var chartStatement = vStmt;

                    if (vm.Cascade is { Mode: "LOCAL" } cascade)
                    {
                        cascade.SourceColumns = vm.Columns.ToList();
                        cascade.SourceRows = vm.Rows.Select(row => row.ToList()).ToList();
                        var parameters = ctx.VarContext.Variables.ToDictionary(
                            pair => CascadingFilterGraphCompiler.Normalize(pair.Key),
                            pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase);
                        vm.Rows = CascadingFilterState.FilterRows(cascade, parameters);
                    }

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
                        chartStatement = vStmt with
                        {
                            Mappings = vStmt.Mappings.Select(mapping =>
                                mapping.Role.Equals("X", StringComparison.OrdinalIgnoreCase)
                                    ? mapping with { Column = currentLevel }
                                    : mapping).ToList()
                        };
                    }

                    ResolveActionValues(vm);
                    CalculateSummaries(vStmt, vm);
                    if (vStmt.VisualType == VisualType.Table && vStmt.Mappings.Count > 0)
                        ApplyTableMappings(vStmt, vm);
                    if (vStmt.VisualType == VisualType.Table)
                        ApplyTableSort(vStmt, vm);
                    await BuildMicroChartsAsync(vStmt, vm);
                    if (vStmt.VisualType == VisualType.Html && vStmt.HtmlTemplate != null)
                        BuildHtmlVisual(vStmt, vm);
                    if (vStmt.VisualType == VisualType.Text && vm.Rows.Count > 0 && vm.Columns.Count > 0)
                        InterpolateTextVisualContent(vStmt, vm);
                    if (chartStatement.AdvancedChart is not null || NamedVisualChartLowerer.Supports(chartStatement.VisualType))
                    {
                        vm.ChartSpec = chartStatement.AdvancedChart is not null
                            ? new AdvancedChartLowerer(ctx).Lower(chartStatement, vm)
                            : new NamedVisualChartLowerer(ctx).Lower(chartStatement, vm);
                        if (vm.ChartSpec.Coordinate.Geography is { } geographic)
                        {
                            if (geographic.SourceKind == GeographicMapSourceKind.File)
                                vm.ResolvedMapFile = GeographicGeometryResolver.ResolveMapFile(ctx, geographic);
                            vm.GeographicGeometry = GeographicGeometryResolver.Resolve(geographic, vm.ResolvedMapFile);
                        }
                        vm.ChartData = new VisualChartDataBuilder().Build(vm.ChartSpec, vm);
                        vm.PlotPlan = new PlotPlanResolver().Resolve(vm.ChartSpec, vm.ChartData, geography: vm.GeographicGeometry);
                        if (chartStatement.AdvancedChart is null && vm.RowStyles is { Count: > 0 })
                            vm.PlotPlan = ApplyChartFormatting(vm.PlotPlan, vm.RowStyles);
                        if (chartStatement.AdvancedChart is null && vm.SegmentRowStyles is { Count: > 0 })
                            vm.PlotPlan = ApplySegmentStyles(vm.PlotPlan, vm.SegmentRowStyles);
                    }
                    if (vm.PlotPlan?.Series is { Length: > 0 } planSeries &&
                        (vm.ChartSpec?.Bindings.Any(b => b.Channel == FieldChannel.Color) == true ||
                         (vStmt.TypedSeries != null && vStmt.TypedSeries.Count > 0) ||
                         resolvedStyles.Keys.Any(k => k.StartsWith("COLOR:", StringComparison.OrdinalIgnoreCase))))
                    {
                        var additionalSeriesTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var s in planSeries)
                        {
                            if (!string.IsNullOrWhiteSpace(s.Key) && !string.IsNullOrWhiteSpace(s.Color) && DesignTokens.IsSafeCssValue(s.Color))
                            {
                                additionalSeriesTokens[s.Key] = s.Color;
                            }
                        }
                        if (additionalSeriesTokens.Count > 0)
                        {
                            var resolvedSeriesTokens = DesignTokenResolver.ResolveScopedTokens(
                                vm.Styles, isPageOrReportLevel: false, vm.Palette, additionalSeriesTokens);
                            if (resolvedSeriesTokens.Count > 0)
                            {
                                vm.DesignTokens ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var (tk, tv) in resolvedSeriesTokens)
                                {
                                    vm.DesignTokens[tk] = tv;
                                }
                            }
                        }
                    }
                    // One resolved interaction contract per visual: from the plan when the visual has
                    // one, from the authored clauses when it does not (TABLE, SLICER, focused layouts).
                    // Nothing downstream re-derives a filter column.
                    vm.Interaction = InteractionManifest.From(vm.PlotPlan?.Interaction
                        ?? ChartInteractionResolver.ResolveTabular(chartStatement, vm.Columns));
                    vm.SemanticFallback = VisualSemanticFallbackBuilder.Build(vm);
                    vm.ChartConfig = vStmt.VisualType == VisualType.Matrix
                        ? MatrixPivotBuilder.Build(vm)
                        : null;
                    vm.NativeSvg = new SvgChartRenderer().Render(vm);
                }
                catch (AdvancedChartSemanticException ex)
                {
                    // A CUSTOM authoring failure. Keep the safe visual error state, but also publish the
                    // positioned diagnostics so preview and the editor agree on what failed and where.
                    vm.Error = ex.Message;
                    vm.Diagnostics = ex.Diagnostics.Select(diagnostic => new VisualDiagnosticManifest
                    {
                        Code = diagnostic.Code ?? AdvancedChartSemanticValidator.DiagnosticCode,
                        Message = diagnostic.Message,
                        Line = diagnostic.Line,
                        Column = diagnostic.Column,
                        Severity = diagnostic.Severity.ToString().ToUpperInvariant()
                    }).ToList();
                }
                catch (Exception ex)
                {
                    vm.Error = ex.Message;
                }
            }

            return vm;
        }

        private static CascadeVisualManifest? BuildCascadeManifest(CreateVisualStatement visual)
        {
            if (visual.Cascade == null) return null;
            var produced = CascadingFilterGraphCompiler.ProducedParameter(visual) ?? string.Empty;
            return new CascadeVisualManifest
            {
                Mode = visual.Cascade.Mode.ToString().ToUpperInvariant(),
                ProducedParameter = produced,
                ValueColumn = visual.Mappings.FirstOrDefault(mapping =>
                    mapping.Role.Equals("VALUE", StringComparison.OrdinalIgnoreCase))?.Column,
                Parents = visual.Cascade.Parents.Select(parent => new CascadeParentManifest(
                    CascadingFilterGraphCompiler.Normalize(parent.ParameterName), parent.ColumnName)).ToList(),
                Invalid = visual.Cascade.InvalidSelection.ToString().ToUpperInvariant(),
                Null = visual.Cascade.NullSelection.ToString().ToUpperInvariant(),
                AllValue = visual.Cascade.AllValue,
                MultiSelect = visual.Cascade.MultiSelect.ToString().ToUpperInvariant()
            };
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
            if (interactionValues?.Keys.Any(name =>
                    name.TrimStart('@').Equals("hover_value", StringComparison.OrdinalIgnoreCase)
                    && InteractionVariableApplies(queryStmt, name)) == true)
                actionType = "FILTER";
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
                        if (vStmt.VisualType == VisualType.Card && row["VALUE"] == null)
                        {
                            var valueMapping = vStmt.Mappings.FirstOrDefault(m => string.Equals(m.Role, "VALUE", StringComparison.OrdinalIgnoreCase))
                                ?? vStmt.Mappings.FirstOrDefault(m => string.Equals(m.Column, "VALUE", StringComparison.OrdinalIgnoreCase));
                            var valueCol = valueMapping?.Column ?? valueMapping?.Role;
                            if (valueCol != null && row[valueCol] != null)
                            {
                                row["VALUE"] = row[valueCol];
                            }
                            else if (vm.Columns.Count > 0)
                            {
                                row["VALUE"] = row[vm.Columns[0]];
                            }
                        }
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

                    if (vm.SegmentRowStyles != null && vStmt.SegmentStyles.Count > 0)
                    {
                        SegmentStyleManifest? matchedSegment = null;
                        foreach (var rule in vStmt.SegmentStyles)
                        {
                            if (await ctx.EvaluateCondition(rule.Condition, row))
                            {
                                matchedSegment = new SegmentStyleManifest
                                {
                                    LineDash = rule.LineDash,
                                    Color = rule.Color
                                };
                                break;
                            }
                        }
                        vm.SegmentRowStyles.Add(matchedSegment);
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
                var hasAny = m.Format != null || m.Align != null || m.DataBar || m.ColorScaleFrom != null || m.CellRenderer != null || m.SparklineColumns != null || m.ProgressBar || m.Hidden || m.Freeze != null || m.Width != null;
                if (!hasAny) return (ColumnMetaManifest?)null;
                var meta = new ColumnMetaManifest { Format = m.Format, Align = m.Align, Hidden = m.Hidden, Freeze = m.Freeze?.ToLowerInvariant(), Width = m.Width };
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
                sparkline.SparklineType ?? "line",
                color: sparkline.SparklineColor,
                referenceLine: sparkline.SparklineReferenceLine,
                labels: labels);
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

            var grandTotalOpt = vStmt.Options.FirstOrDefault(o => o.Key.Equals("GRAND_TOTAL", StringComparison.OrdinalIgnoreCase))?.Value;
            bool hasGrandTotalInOptions = !string.IsNullOrWhiteSpace(grandTotalOpt) &&
                !grandTotalOpt.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
                !grandTotalOpt.Equals("FALSE", StringComparison.OrdinalIgnoreCase);

            if (vStmt.Summaries.Count == 0 && !hasGrandTotalInOptions && (vStmt.SummaryOptions == null ||
                (!vStmt.SummaryOptions.GrandTotalRow && !vStmt.SummaryOptions.GrandTotalColumn &&
                 !vStmt.SummaryOptions.SummarizeRow && !vStmt.SummaryOptions.SummarizeColumn)))
                return;

            var summaryData = new TableSummaryData();
            vm.SummaryData = summaryData;

            var totalPos = vStmt.SummaryOptions?.TotalPosition
                ?? vStmt.Options.FirstOrDefault(o => o.Key.Equals("TOTAL_POSITION", StringComparison.OrdinalIgnoreCase))?.Value
                ?? "BOTTOM";
            summaryData.TotalPosition = totalPos.ToUpperInvariant();

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

            // 2. Grand Totals (if enabled, compute SUM or specified aggregate for numeric columns or specific ones)
            if ((vStmt.SummaryOptions != null && (vStmt.SummaryOptions.GrandTotalRow || vStmt.SummaryOptions.SummarizeColumn)) || hasGrandTotalInOptions)
            {
                summaryData.GrandTotals = new Dictionary<string, string>();
                var colsToSummarize = vStmt.SummaryOptions?.SpecificColumns ?? vm.Columns;
                var agg = "SUM";
                if (hasGrandTotalInOptions && !grandTotalOpt!.Equals("ON", StringComparison.OrdinalIgnoreCase) && !grandTotalOpt.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    agg = grandTotalOpt.ToUpperInvariant();
                }

                foreach (var colName in colsToSummarize)
                {
                    var colIndex = vm.Columns.FindIndex(c => c.Equals(colName, StringComparison.OrdinalIgnoreCase));
                    if (colIndex < 0) continue;

                    var sum = ComputeAggregate(vm.Rows, colIndex, agg);
                    if (sum != "0" || IsNumericColumn(vm.Rows, colIndex) || agg == "COUNT")
                    {
                        summaryData.GrandTotals[colName] = sum;
                    }
                }
            }
        }

        private void ApplyTableSort(CreateVisualStatement vStmt, VisualManifest vm)
        {
            var defaultSort = vStmt.Options.FirstOrDefault(o => o.Key.Equals("DEFAULT_SORT", StringComparison.OrdinalIgnoreCase))?.Value;
            if (string.IsNullOrWhiteSpace(defaultSort) || vm.Rows.Count <= 1 || vm.Columns.Count == 0) return;

            var rawSpecs = defaultSort.Trim().TrimStart('(').TrimEnd(')').Split(',', StringSplitOptions.RemoveEmptyEntries);
            var sortSpecs = new List<(int ColumnIndex, bool Descending)>();
            foreach (var rawSpec in rawSpecs)
            {
                var parts = rawSpec.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                var colName = parts[0].Trim('\'', '"', '[', ']');
                var desc = parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

                var idx = vm.Columns.FindIndex(c => c.Equals(colName, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                {
                    var mapping = vStmt.Mappings.FirstOrDefault(m =>
                        m.Column.Equals(colName, StringComparison.OrdinalIgnoreCase) ||
                        (m.DisplayName != null && m.DisplayName.Equals(colName, StringComparison.OrdinalIgnoreCase)));
                    if (mapping != null)
                    {
                        var targetName = mapping.DisplayName ?? mapping.Column;
                        idx = vm.Columns.FindIndex(c => c.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (idx >= 0)
                {
                    sortSpecs.Add((idx, desc));
                }
            }

            if (sortSpecs.Count == 0) return;

            var indexedRows = vm.Rows.Select((row, originalIndex) => (row, originalIndex)).ToList();
            indexedRows.Sort((a, b) =>
            {
                foreach (var (ci, desc) in sortSpecs)
                {
                    var va = ci < a.row.Count ? a.row[ci] : null;
                    var vb = ci < b.row.Count ? b.row[ci] : null;
                    int cmp;
                    if (va == null && vb == null) cmp = 0;
                    else if (va == null) cmp = -1;
                    else if (vb == null) cmp = 1;
                    else if (decimal.TryParse(va, NumberStyles.Any, CultureInfo.InvariantCulture, out var na) &&
                             decimal.TryParse(vb, NumberStyles.Any, CultureInfo.InvariantCulture, out var nb))
                    {
                        cmp = na.CompareTo(nb);
                    }
                    else
                    {
                        cmp = string.Compare(va, vb, StringComparison.OrdinalIgnoreCase);
                    }

                    if (cmp != 0)
                        return desc ? -cmp : cmp;
                }
                return a.originalIndex.CompareTo(b.originalIndex);
            });

            vm.Rows = indexedRows.Select(x => x.row).ToList();
            if (vm.RawRows.Count == indexedRows.Count)
            {
                var newRaw = indexedRows.Select(x => vm.RawRows[x.originalIndex]).ToList();
                vm.RawRows.Clear();
                vm.RawRows.AddRange(newRaw);
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

        private static void ResolveDynamicMinMax(VisualManifest vm)
        {
            if (vm.Rows == null || vm.Rows.Count == 0 || vm.Columns == null || vm.Columns.Count == 0) return;

            foreach (var key in new[] { "MIN", "MAX" })
            {
                if (vm.Options.TryGetValue(key, out var expr) && !string.IsNullOrWhiteSpace(expr))
                {
                    var isMin = expr.IndexOf("SOURCE_MIN(", StringComparison.OrdinalIgnoreCase) >= 0;
                    var isMax = expr.IndexOf("SOURCE_MAX(", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isMin || isMax)
                    {
                        var startParen = expr.IndexOf('(');
                        var endParen = expr.IndexOf(')', startParen + 1);
                        if (startParen >= 0 && endParen > startParen)
                        {
                            var colName = expr.Substring(startParen + 1, endParen - startParen - 1).Trim().Trim('\'', '"', '[', ']');
                            var colIdx = vm.Columns.FindIndex(c => c.Equals(colName, StringComparison.OrdinalIgnoreCase));
                            if (colIdx >= 0)
                            {
                                var nonNullValues = vm.Rows
                                    .Select(r => colIdx < r.Count ? r[colIdx] : null)
                                    .Where(v => v != null && !string.IsNullOrWhiteSpace(v.ToString()))
                                    .Select(v => v!.ToString()!)
                                    .ToList();

                                if (nonNullValues.Count > 0)
                                {
                                    if (DateTime.TryParse(nonNullValues[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                                    {
                                        var dateVals = nonNullValues
                                            .Select(v => DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? (DateTime?)d : null)
                                            .Where(d => d.HasValue)
                                            .Select(d => d!.Value)
                                            .OrderBy(d => d)
                                            .ToList();
                                        if (dateVals.Count > 0)
                                        {
                                            var target = isMin ? dateVals.First() : dateVals.Last();
                                            vm.Options[key] = target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                                        }
                                    }
                                    else if (decimal.TryParse(nonNullValues[0], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                                    {
                                        var numVals = nonNullValues
                                            .Select(v => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? (decimal?)n : null)
                                            .Where(n => n.HasValue)
                                            .Select(n => n!.Value)
                                            .OrderBy(n => n)
                                            .ToList();
                                        if (numVals.Count > 0)
                                        {
                                            var target = isMin ? numVals.First() : numVals.Last();
                                            vm.Options[key] = target.ToString(CultureInfo.InvariantCulture);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void ResolveSliderDataTicks(VisualManifest vm)
        {
            if (vm.Rows == null || vm.Rows.Count == 0 || vm.Columns == null || vm.Columns.Count == 0) return;
            var valCol = vm.Options.GetValueOrDefault("mapping:value") ?? vm.Columns[0];
            var colIdx = vm.Columns.FindIndex(c => c.Equals(valCol, StringComparison.OrdinalIgnoreCase));
            if (colIdx < 0) colIdx = 0;

            var numericValues = vm.Rows
                .Select(r => colIdx < r.Count ? r[colIdx] : null)
                .Where(v => v != null && decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .Select(v => decimal.Parse(v!.ToString()!, NumberStyles.Any, CultureInfo.InvariantCulture))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (numericValues.Count > 0)
            {
                vm.Options["DATA_TICKS"] = JsonSerializer.Serialize(numericValues);
                if (!vm.Options.ContainsKey("MIN")) vm.Options["MIN"] = numericValues.First().ToString(CultureInfo.InvariantCulture);
                if (!vm.Options.ContainsKey("MAX")) vm.Options["MAX"] = numericValues.Last().ToString(CultureInfo.InvariantCulture);
            }
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

        private void BuildHtmlVisual(CreateVisualStatement vStmt, VisualManifest vm)
        {
            var htmlDef = vStmt.HtmlTemplate!;
            var evaluator = new HtmlVisual.HtmlTemplateEvaluator();
            var sanitizer = new HtmlVisual.HtmlSanitizer();

            var templateViolations = sanitizer.ValidateTemplate(htmlDef.Template);
            if (templateViolations.Count > 0)
            {
                vm.Error = "HTML template sanitization failed: " +
                    string.Join("; ", templateViolations.Select(v => v.Message));
                return;
            }

            if (htmlDef.Css != null)
            {
                var cssViolations = sanitizer.ValidateCss(htmlDef.Css);
                if (cssViolations.Count > 0)
                {
                    vm.Error = "CSS sanitization failed: " +
                        string.Join("; ", cssViolations.Select(v => v.Message));
                    return;
                }
            }

            foreach (var parameter in Reporting.Semantics.ConstrainedHtmlPolicy.Bindings(htmlDef.Template)
                .Concat(htmlDef.Fallback is null ? [] : Reporting.Semantics.ConstrainedHtmlPolicy.Bindings(htmlDef.Fallback))
                .Concat(Reporting.Semantics.ConstrainedHtmlPolicy.EmbeddedParameters(htmlDef.Template))
                .Where(binding => binding.StartsWith('@'))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var metadata = ctx.VarContext.VariableMetadata.FirstOrDefault(pair =>
                    pair.Key.TrimStart('@').Equals(parameter.TrimStart('@'), StringComparison.OrdinalIgnoreCase)).Value;
                if (metadata is { IsSecret: true } or { IsSensitive: true })
                {
                    vm.Error = $"RPT3014: HTML visual '{vStmt.Name}' cannot disclose sensitive parameter '{parameter}'.";
                    return;
                }
            }

            var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in ctx.VarContext.Variables)
                parameters[v.Key] = v.Value;
            var formatting = ctx.ReportContext.EffectiveFormatting;
            string FormatHtmlValue(object? value, string format) => value switch
            {
                null => formatting.NullLabel,
                DateTimeOffset offset => TimeZoneInfo.ConvertTime(offset, formatting.Zone).ToString(format, formatting.Culture),
                IFormattable formattable => formattable.ToString(format, formatting.Culture) ?? string.Empty,
                _ => value.ToString() ?? string.Empty
            };

            vm.HtmlMode = htmlDef.Mode.ToString().ToUpperInvariant();
            var maxRows = HtmlVisual.HtmlVisualBudgets.DefaultMaxRows;
            if (vm.Options.TryGetValue("MAX_ROWS", out var configuredMaxRows)
                && int.TryParse(configuredMaxRows, out var parsedMaxRows))
                maxRows = parsedMaxRows;
            var authoredCost = HtmlVisual.HtmlVisualBudgets.ValidateAuthored(
                htmlDef.Template,
                htmlDef.Css,
                vm.RawRows.Count,
                maxRows,
                htmlDef.Mode == HtmlVisualMode.Repeater);
            var embedIndex = 0;
            var microChartIndex = 0;
            var microChartFallbacks = new List<string>();
            var inlineMicroCharts = new List<MicroChartManifest>();
            string RenderEmbed(HtmlVisual.HtmlVisualEmbedRequest request)
            {
                foreach (var sourceParameter in request.SourceParameters)
                {
                    var metadata = ctx.VarContext.VariableMetadata.FirstOrDefault(pair =>
                        pair.Key.TrimStart('@').Equals(sourceParameter.TrimStart('@'), StringComparison.OrdinalIgnoreCase)).Value;
                    if (metadata is { IsSecret: true } or { IsSensitive: true })
                        throw new HtmlVisual.HtmlTemplateException($"HTML visual embedding cannot disclose sensitive parameter '{sourceParameter}'.");
                }
                var id = $"{vStmt.Name}-embed-{embedIndex++}";
                vm.HtmlEmbeds ??= [];
                vm.HtmlEmbeds.Add(new HtmlVisualEmbedManifest
                {
                    Id = id,
                    TargetName = request.TargetName,
                    Parameters = request.Parameters.Count == 0
                        ? null : new Dictionary<string, string>(request.Parameters, StringComparer.OrdinalIgnoreCase)
                });
                return $"<div data-etl-embed-id=\"{id}\"></div>";
            }
            string RenderMicroChart(HtmlVisual.HtmlMicroChartRequest request)
            {
                var expression = request.Expression;
                var id = $"{vStmt.Name}-micro-{microChartIndex++}";
                var factory = new MicroChartPlanFactory();
                MicroChartSemanticBundle bundle;
                string kind;
                if (expression.Helper == "SPARKLINE")
                {
                    var values = ParseHtmlSparklineValues(request.Value, expression.Field);
                    bundle = factory.CreateSparkline(id, values, expression.Type, expression.Color,
                        width: expression.Width, height: expression.Height);
                    kind = "sparkline";
                }
                else
                {
                    if (!decimal.TryParse(Convert.ToString(request.Value, CultureInfo.InvariantCulture),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                        throw new HtmlVisual.HtmlTemplateException(
                            $"RPT3015: PROGRESS_BAR field '{expression.Field}' must resolve to a decimal value.");
                    bundle = factory.CreateProgress(id, value, expression.Minimum, expression.Maximum,
                        expression.Color, expression.Width, expression.Height);
                    kind = "progress";
                }
                inlineMicroCharts.Add(factory.ToManifest(bundle, kind, "html.inline"));
                microChartFallbacks.Add(bundle.PlainText);
                return $"<span data-etl-microchart-id=\"{id}\"></span>";
            }

            if (htmlDef.Mode == HtmlVisualMode.Repeater)
            {
                vm.HtmlContent = evaluator.EvaluateRepeater(
                    htmlDef.Template, vm.RawRows, parameters, maxRows, FormatHtmlValue, RenderEmbed, RenderMicroChart);

                if (htmlDef.Fallback != null)
                {
                    var fallbacks = new List<string>();
                    var count = Math.Min(vm.RawRows.Count, 20);
                    for (var i = 0; i < count; i++)
                        fallbacks.Add(evaluator.EvaluateFallback(htmlDef.Fallback, vm.RawRows[i], parameters, FormatHtmlValue));
                    vm.HtmlFallback = string.Join("\n", fallbacks);
                    if (vm.RawRows.Count > 20)
                        vm.HtmlFallback += $"\n... and {vm.RawRows.Count - 20} more";
                }
                else
                {
                    vm.HtmlFallback = $"{vStmt.Name}: {vm.RawRows.Count} items";
                }
            }
            else
            {
                var row = vm.RawRows.Count > 0 ? vm.RawRows[0] : null;
                vm.HtmlContent = evaluator.Evaluate(htmlDef.Template, row, parameters, FormatHtmlValue, RenderEmbed, RenderMicroChart);

                if (htmlDef.Fallback != null)
                    vm.HtmlFallback = evaluator.EvaluateFallback(htmlDef.Fallback, row, parameters, FormatHtmlValue);
                else if (row != null)
                    vm.HtmlFallback = $"{vStmt.Name}: " +
                        string.Join(", ", row.Take(5).Select(kvp => $"{kvp.Key} {kvp.Value}"));
                else
                    vm.HtmlFallback = vStmt.Name;
            }

            if (microChartFallbacks.Count > 0)
            {
                var chartSummary = string.Join("; ", microChartFallbacks.Take(20));
                if (microChartFallbacks.Count > 20)
                    chartSummary += $"; ... and {microChartFallbacks.Count - 20} more indicators";
                vm.HtmlFallback = string.IsNullOrWhiteSpace(vm.HtmlFallback)
                    ? chartSummary
                    : $"{vm.HtmlFallback}\n{chartSummary}";
            }

            if (htmlDef.Css != null)
            {
                var containerId = $"etl-v-{vStmt.Name.ToLowerInvariant().Replace(' ', '-')}";
                vm.HtmlCss = sanitizer.ScopeCss(htmlDef.Css, containerId);
            }
            var resolvedCost = HtmlVisual.HtmlVisualBudgets.ValidateRendered(
                authoredCost,
                vm.HtmlContent ?? string.Empty,
                inlineMicroCharts.Select(chart => chart.Svg));
            vm.HtmlCost = new HtmlVisualCostManifest
            {
                TemplateBytes = resolvedCost.TemplateBytes,
                CssBytes = resolvedCost.CssBytes,
                TemplateNodes = resolvedCost.TemplateNodes,
                OutputNodes = resolvedCost.OutputNodes,
                OutputBytes = resolvedCost.OutputBytes,
                RenderWork = resolvedCost.RenderWork
            };
            if (inlineMicroCharts.Count > 0)
            {
                vm.MicroCharts ??= [];
                vm.MicroCharts.AddRange(inlineMicroCharts);
            }
        }

        internal static PlotPlan ApplyChartFormatting(PlotPlan plan, IReadOnlyList<string?> rowColors) => plan with
        {
            Layers = plan.Layers.Select(layer => layer with
            {
                Data = layer.Data.Select(datum =>
                {
                    if (datum.RowIndex < 0 || datum.RowIndex >= rowColors.Count ||
                        string.IsNullOrWhiteSpace(rowColors[datum.RowIndex]))
                        return datum;
                    return datum with
                    {
                        Encodings = datum.Encodings
                            .Where(encoding => encoding.Channel != ConditionalEncodingChannel.Color)
                            .Append(new ResolvedEncodingValue(
                                ConditionalEncodingChannel.Color,
                                ChartValue.From(rowColors[datum.RowIndex]!)))
                            .ToImmutableArray()
                    };
                }).ToImmutableArray()
            }).ToImmutableArray()
        };

        internal static PlotPlan ApplySegmentStyles(PlotPlan plan, IReadOnlyList<SegmentStyleManifest?> segmentStyles) => plan with
        {
            Layers = plan.Layers.Select(layer =>
            {
                if (layer.Mark != MarkKind.Line) return layer;
                return layer with
                {
                    Data = layer.Data.Select(datum =>
                    {
                        if (datum.RowIndex < 0 || datum.RowIndex >= segmentStyles.Count) return datum;
                        var style = segmentStyles[datum.RowIndex];
                        if (style == null) return datum;
                        return datum with
                        {
                            SegmentLineDash = style.LineDash,
                            SegmentColor = style.Color
                        };
                    }).ToImmutableArray()
                };
            }).ToImmutableArray()
        };

        private static List<decimal?> ParseHtmlSparklineValues(object? source, string field)
        {
            var raw = Convert.ToString(source, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw)) return [];
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    throw new HtmlVisual.HtmlTemplateException(
                        $"RPT3015: SPARKLINE field '{field}' must contain a JSON numeric array.");
                var items = document.RootElement.EnumerateArray().ToList();
                if (items.Count > ConstrainedHtmlPolicy.MaxMicroChartPoints)
                    throw new HtmlVisual.HtmlTemplateException(
                        $"RPT3015: SPARKLINE field '{field}' exceeds the {ConstrainedHtmlPolicy.MaxMicroChartPoints}-point limit.");
                return items.Select(item => item.ValueKind switch
                {
                    JsonValueKind.Null => (decimal?)null,
                    JsonValueKind.Number when item.TryGetDecimal(out var number) => number,
                    JsonValueKind.String when decimal.TryParse(item.GetString(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var number) => number,
                    _ => throw new HtmlVisual.HtmlTemplateException(
                        $"RPT3015: SPARKLINE field '{field}' must contain only numbers or nulls.")
                }).ToList();
            }
            catch (JsonException)
            {
                throw new HtmlVisual.HtmlTemplateException(
                    $"RPT3015: SPARKLINE field '{field}' must contain a JSON numeric array.");
            }
        }

        private static void InterpolateTextVisualContent(CreateVisualStatement vStmt, VisualManifest vm)
        {
            var content = vm.DefaultValue;
            if (string.IsNullOrEmpty(content) || !content.Contains('{')) return;

            var result = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"\{([a-zA-Z0-9_@#]+)(?:\s+FORMAT\s+[""']([^""']+)[""'])?\}",
                match =>
                {
                    var colName = match.Groups[1].Value;
                    var format = match.Groups[2].Success ? match.Groups[2].Value : null;

                    object? raw = null;
                    bool found = false;
                    if (vm.RawRows.Count > 0)
                    {
                        var dict = vm.RawRows[0];
                        foreach (var kv in dict)
                        {
                            if (string.Equals(kv.Key, colName, StringComparison.OrdinalIgnoreCase))
                            {
                                raw = kv.Value;
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found && vm.Rows.Count > 0)
                    {
                        for (int i = 0; i < vm.Columns.Count; i++)
                        {
                            if (string.Equals(vm.Columns[i], colName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (i < vm.Rows[0].Count)
                                {
                                    raw = vm.Rows[0][i];
                                    found = true;
                                }
                                break;
                            }
                        }
                    }
                    if (!found) return match.Value;
                    if (raw == null || raw is DBNull) return string.Empty;

                    if (!string.IsNullOrEmpty(format))
                    {
                        if (raw is IFormattable formattable)
                        {
                            return formattable.ToString(format, CultureInfo.InvariantCulture);
                        }
                        if (decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                        {
                            return num.ToString(format, CultureInfo.InvariantCulture);
                        }
                    }
                    return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                });

            vm.DefaultValue = result;
        }
    }
}
