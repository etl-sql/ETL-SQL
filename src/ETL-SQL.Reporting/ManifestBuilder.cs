using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Reporting.Builders;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Walks the post-execution context to collect visual/page/dataset definitions
    /// and materialise their data into a <see cref="ReportManifest"/>.
    /// Acts as a facade/orchestrator for specialized builders.
    /// </summary>
    public class ManifestBuilder
    {
        private readonly IExecutionContext _ctx;
        private readonly StyleBuilder _styleBuilder;
        private readonly VisualBuilder _visualBuilder;
        private readonly PageBuilder _pageBuilder;
        private readonly DatasetBuilder _datasetBuilder;

        public ManifestBuilder(IExecutionContext ctx)
        {
            _ctx = ctx;
            var renderer = new EChartsRenderer();
            _styleBuilder = new StyleBuilder(ctx);
            _visualBuilder = new VisualBuilder(ctx, renderer, _styleBuilder);
            _pageBuilder = new PageBuilder(_styleBuilder);
            _datasetBuilder = new DatasetBuilder();
        }

        /// <summary>
        /// Builds the manifest by querying each visual's data source.
        /// Must be called after the script has been fully evaluated.
        /// </summary>
        public async Task<ReportManifest> BuildAsync(string scriptSource, Dictionary<string, string>? interactionValues = null, bool skipDeferredVisuals = false)
        {
            var manifest = new ReportManifest
            {
                Source      = scriptSource,
                BuiltAt     = DateTime.UtcNow,
                IsInteraction = (interactionValues != null && interactionValues.Count > 0) || (_ctx.ReportContext.BaselineParameters.Count > 0 && _ctx.VarContext.Variables.Any(v => _ctx.ReportContext.BaselineParameters.TryGetValue(v.Key, out var baseVal) && String.Compare(v.Value?.ToString() ?? "", baseVal ?? "", true) != 0)),
                Title           = _ctx.ReportContext.ReportTitle,
                TitleIsMarkdown  = _ctx.ReportContext.ReportTitleIsMarkdown,
                Description     = _ctx.ReportContext.ReportDescription,
                Css             = _ctx.ReportContext.ReportCss,
                Js              = _ctx.ReportContext.ReportJs,
                HtmlHead        = _ctx.ReportContext.ReportHtmlHead,
                HtmlBody        = _ctx.ReportContext.ReportHtmlBody,
                HtmlFooter      = _ctx.ReportContext.ReportHtmlFooter,
                Favicon         = _ctx.ReportContext.ReportFavicon,
                Logo            = _ctx.ReportContext.ReportLogo,
                Background      = _ctx.ReportContext.ReportBackground,
                Theme           = _ctx.ReportContext.ReportTheme,
                Navigation      = _ctx.ReportContext.ReportNavigation,
                Telemetry       = new TelemetryManifest
                {
                    RowsProcessed       = _ctx.Telemetry.RowsProcessed,
                    TotalSpilledBytes   = _ctx.Telemetry.TotalSpilledBytes,
                    SubqueryCacheHits   = _ctx.Telemetry.SubqueryCacheHits,
                    SubqueryCacheMisses = _ctx.Telemetry.SubqueryCacheMisses,
                    SubquerySpillCount  = _ctx.Telemetry.SubquerySpillCount,
                    SubquerySpilledBytes = _ctx.Telemetry.SubquerySpilledBytes,
                    ExecutionTimeMs     = _ctx.Telemetry.LastExecutionTimeMs
                }
            };
            var reportStyles = _styleBuilder.ResolveReportStyles();
            if (reportStyles.Count > 0)
                manifest.Styles = reportStyles;

            // Seed common interaction variables if they don't exist, to prevent expression evaluation errors during manifest generation
            var interactionVars = new[] { "@hover_value", "@click_value", "@selected_value", "@drill_value", "@param_value" };
            foreach (var v in interactionVars)
            {
                if (!_ctx.VarContext.ContainsVariable(v))
                    _ctx.VarContext.DeclareVariable(v, null);
            }

            // ── Visuals ──────────────────────────────────────────────────────
            foreach (var (name, vStmt) in _ctx.ReportContext.VisualDefinitions)
            {
                try
                {
                    manifest.Visuals.Add(await _visualBuilder.BuildAsync(name, vStmt, interactionValues, skipDeferredVisuals));
                }
                catch (Exception ex)
                {
                    manifest.Messages ??= new();
                    manifest.Messages.Add(new LogEntryManifest($"Failed to build visual '{name}': {ex.Message}", "Red", DateTime.UtcNow));
                    manifest.Error = "One or more visuals failed to build.";
                }
            }

            // ── Pages ────────────────────────────────────────────────────────
            foreach (var (name, pStmt) in _ctx.ReportContext.PageDefinitions)
            {
                manifest.Pages.Add(await _pageBuilder.BuildAsync(name, pStmt, _ctx, reportStyles));
            }

            // ── Containers ───────────────────────────────────────────────────
            if (_ctx.ReportContext.ContainerDefinitions.Count > 0)
            {
                manifest.Containers = new();
                foreach (var (name, cStmt) in _ctx.ReportContext.ContainerDefinitions)
                {
                    var resolvedStyles = _styleBuilder.ResolveStyles(cStmt.StyleName, cStmt.Styles, reportStyles);
                    var (title, titleMd) = await _styleBuilder.ResolveMarkdownAsync(cStmt.Title, cStmt.TitleIsMarkdown);
                    var (subtitle, subtitleMd) = await _styleBuilder.ResolveMarkdownAsync(cStmt.Subtitle, cStmt.SubtitleIsMarkdown);

                    manifest.Containers.Add(new ContainerManifest
                    {
                        Name               = name,
                        ContainerType      = cStmt.ContainerType,
                        Structure          = cStmt.Structure,
                        SlotMap            = cStmt.SlotMap.ToDictionary(kv => kv.Key, kv => kv.Value),
                        Title              = title,
                        TitleIsMarkdown    = titleMd,
                        Subtitle           = subtitle,
                        SubtitleIsMarkdown = subtitleMd,
                        Tooltip            = await _styleBuilder.BuildTooltipManifestAsync(cStmt.Tooltip),
                        IsCollapsible      = cStmt.IsCollapsible,
                        Icon               = cStmt.Icon,
                        IsPinnable         = cStmt.IsPinnable,
                        IsHidden           = ResolveVisibility(cStmt.Visibility),
                        Styles             = resolvedStyles.Count > 0 ? resolvedStyles : null

                    });
                }
            }

            // ── Navigations ──────────────────────────────────────────────────
            if (_ctx.ReportContext.NavigationDefinitions.Count > 0)
            {
                manifest.Navigations = new();
                foreach (var (name, nStmt) in _ctx.ReportContext.NavigationDefinitions)
                {
                    // Hidden pages are not shown in the nav bar (they are still rendered
                    // as sections so DRILL_DOWN can navigate to them programmatically).
                    var visiblePages = nStmt.Pages
                        .Where(p => !(_ctx.ReportContext.PageDefinitions.TryGetValue(p, out var pd) && ResolveVisibility(pd.Visibility)))
                        .ToList();

                    manifest.Navigations.Add(new NavigationManifest
                    {
                        Name        = name,
                        NavType     = nStmt.NavType.ToString().ToUpperInvariant(),
                        Orientation = nStmt.Orientation.ToString().ToUpperInvariant(),
                        DefaultPage = nStmt.DefaultPage,
                        Pages       = visiblePages
                    });
                }
            }

            // ── Buttons ──────────────────────────────────────────────────────
            if (_ctx.ReportContext.ButtonDefinitions.Count > 0)
            {
                manifest.Buttons = new();
                foreach (var (name, bStmt) in _ctx.ReportContext.ButtonDefinitions)
                {
                    var resolvedStyles = _styleBuilder.ResolveStyles(bStmt.StyleName, bStmt.Styles, reportStyles);
                    var (bTitle, _) = await _styleBuilder.ResolveMarkdownAsync(bStmt.Title);
                    var bm = new ButtonManifest
                    {
                        Name       = name,
                        ButtonType = bStmt.ButtonType,
                        Title      = bTitle,
                        Tooltip    = await _styleBuilder.BuildTooltipManifestAsync(bStmt.Tooltip),
                        Styles     = resolvedStyles.Count > 0 ? resolvedStyles : new Dictionary<string, string>()
                    };

                    if (!bm.Styles!.ContainsKey("EXPORT"))
                        bm.Styles["EXPORT"] = "OFF";

                    foreach (var opt in bStmt.Options)
                        bm.Options[opt.Key] = opt.Value;

                    foreach (var action in bStmt.Actions)
                    {
                        bm.Actions.Add(TranslateAction(action));
                    }
                    manifest.Buttons.Add(bm);
                }
            }

            // ── Datasets ─────────────────────────────────────────────────────
            foreach (var (tableName, dStmt) in _ctx.ReportContext.DatasetDefinitions)
            {
                var rowCount = 0L;
                if (_ctx.Connections.TryGetValue(tableName, out var src))
                {
                    try
                    {
                        await foreach (var batch in src.ReadBatches())
                            rowCount += batch.Rows.Count;
                    }
                    catch { /* source may not support ReadBatches */ }
                }

                var dm = _datasetBuilder.Build(dStmt);
                dm.RowCount = rowCount;
                manifest.Datasets.Add(dm);
            }

            // ── Parameters & Metadata ────────────────────────────────────────
            var vctx = _ctx.VarContext;
            if (vctx != null)
            {
                // Capture all accessible variables with metadata (scope-aware)
                var variablesWithMetadata = vctx.GetVariablesWithMetadata();
                foreach (var kvp in variablesWithMetadata)
                {
                    var valStr = kvp.Value.Value?.ToString() ?? "";
                    manifest.Parameters[kvp.Key] = valStr;

                    // Phase 3: Capture metadata for INPUT variables
                    if (kvp.Value.Metadata.IsInput)
                    {
                        manifest.ParameterMetadata[kvp.Key] = new ParameterMetadataManifest
                        {
                            Name = kvp.Key,
                            Type = kvp.Value.Metadata.DataType ?? "STRING",
                            DefaultValue = valStr,
                            IsRequired = kvp.Value.Metadata.IsRequired
                        };
                    }
                }

                // Fallback for variables without metadata or if scope-aware fetching missed something
                foreach (var kvp in vctx.Variables)
                {
                    if (!manifest.Parameters.ContainsKey(kvp.Key))
                        manifest.Parameters[kvp.Key] = kvp.Value?.ToString() ?? "";
                }
            }

            // ── Custom Themes ────────────────────────────────────────────────
            if (_ctx.ReportContext.ThemeDefinitions.Count > 0)
            {
                manifest.CustomThemes = new();
                foreach (var (themeName, themeStmt) in _ctx.ReportContext.ThemeDefinitions)
                {
                    var themeJson = ThemeBuilder.BuildEChartsTheme(themeStmt.Properties);
                    using var doc = JsonDocument.Parse(themeJson.ToJsonString());
                    manifest.CustomThemes.Add(new ThemeManifest
                    {
                        Name   = themeName,
                        Config = doc.RootElement.Clone()
                    });
                }
            }
            
            // ── Messages ─────────────────────────────────────────────────────
            manifest.Messages = _ctx.Messages
                .Select(m => new LogEntryManifest(m.Message, m.Color.ToString().ToLowerInvariant(), m.Timestamp))
                .ToList();

            manifest.ExecutionTree = _ctx.Telemetry.ExecutionTree.ToSnapshot();

            return manifest;
        }

        /// <summary>
        /// Clears HighlightRows from a visual and regenerates its ChartConfig without the ghost overlay.
        /// Does not re-query the data source — uses existing Rows.
        /// </summary>
        public void ClearHighlightRows(VisualManifest vm)
        {
            if (vm.HighlightRows == null) return;
            vm.HighlightRows = null;
            vm.ChartConfig = new EChartsRenderer().Render(vm);
        }

        /// <summary>
        /// Re-queries the data for a specific visual and updates its Row/Column collections.
        /// Also regenerates the ChartConfig.
        /// </summary>
        public async Task RefreshVisualAsync(CreateVisualStatement vStmt, VisualManifest vm, Dictionary<string, string>? interactionValues = null, VisualDrillState? drillState = null)
        {
            // Refresh logic is now shared via VisualBuilder
            var newVm = await _visualBuilder.BuildAsync(vm.Name, vStmt, interactionValues, drillState: drillState);
            vm.Rows = newVm.Rows;
            vm.Columns = newVm.Columns;
            vm.Error = newVm.Error;
            vm.ChartConfig = newVm.ChartConfig;
            vm.Options = newVm.Options;
            vm.Actions = newVm.Actions;
            vm.Interactions = newVm.Interactions;
            vm.Styles = newVm.Styles;
            vm.SeriesDefs = newVm.SeriesDefs;
            vm.FormattingRules = newVm.FormattingRules;
            vm.RowStyles = newVm.RowStyles;
            vm.Overlays = newVm.Overlays;
            vm.HighlightRows = newVm.HighlightRows;
            vm.DrillState = newVm.DrillState;
            vm.IsHidden = false; // refreshed visuals are always shown regardless of VISIBLE = OFF
        }

        private VisualActionManifest TranslateAction(VisualAction action)
        {
            return action switch
            {
                DrillDownAction dd => new VisualActionManifest
                {
                    Type         = "DRILL_DOWN",
                    Trigger      = dd.Trigger,
                    TargetVisual = dd.TargetVisual,
                    KeyColumns   = dd.KeyColumns
                },
                SetParameterAction sp => new VisualActionManifest
                {
                    Type            = "SET_PARAMETER",
                    Trigger         = sp.Trigger,
                    ParameterName   = sp.ParameterName,
                    ValueExpression = sp.ValueExpression,
                    ValueSource     = "LITERAL",
                    LiteralValue    = sp.ValueExpression
                },
                ClearFiltersAction cf => new VisualActionManifest
                {
                    Type    = "CLEAR_FILTERS",
                    Trigger = cf.Trigger
                },
                RunScriptAction rs => new VisualActionManifest
                {
                    Type       = "RUN_SCRIPT",
                    Trigger    = rs.Trigger,
                    ScriptPath = rs.ScriptPath,
                    Parameters = rs.Parameters,
                    ParameterColumns = rs.Parameters.Where(p => !p.Value.StartsWith("'") && !p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value),
                    LiteralParameters = rs.Parameters.Where(p => p.Value.StartsWith("'") || p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value.Trim('\''))
                },
                DrillReportAction dr => new VisualActionManifest
                {
                    Type         = "DRILL_REPORT",
                    Trigger      = dr.Trigger,
                    TargetReport = dr.TargetReport,
                    Parameters   = dr.Parameters,
                    ParameterColumns = dr.Parameters.Where(p => !p.Value.StartsWith("'") && !p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value),
                    LiteralParameters = dr.Parameters.Where(p => p.Value.StartsWith("'") || p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value.Trim('\''))
                },
                NavigatePageAction np => new VisualActionManifest
                {
                    Type       = "NAVIGATE_PAGE",
                    Trigger    = np.Trigger,
                    TargetPage = np.TargetPage
                },
                RefreshVisualsAction rv => new VisualActionManifest
                {
                    Type = "REFRESH_VISUALS",
                    Trigger = action.Trigger,
                    Targets = rv.Targets
                },
                ApplyParametersAction ap => new VisualActionManifest { Type = "APPLY_PARAMETERS", Trigger = action.Trigger },
                ReportCommandAction command => new VisualActionManifest { Type = command.Command, Trigger = action.Trigger },
                DrillInAction di => new VisualActionManifest { Type = "DRILL_IN", Trigger = action.Trigger, Hierarchy = di.Hierarchy },
                SetUiStateAction su => new VisualActionManifest
                {
                    Type = "SET_UI_STATE",
                    Trigger = action.Trigger,
                    Targets = su.Targets,
                    Key = su.Key,
                    Value = su.Value
                },
                _ => new VisualActionManifest { Type = "UNKNOWN", Trigger = action.Trigger }
            };
        }
        private bool ResolveVisibility(string? visibility)
        {
            if (string.IsNullOrEmpty(visibility)) return false; // Default is visible (IsHidden=false)
            if (visibility.StartsWith("@"))
            {
                var val = _ctx.VarContext.GetVariable(visibility);
                if (val == null) return false;
                var s = val.ToString()?.ToUpperInvariant();
                return s is "OFF" or "FALSE" or "0";
            }
            return visibility.ToUpperInvariant() is "OFF" or "FALSE" or "0";
        }
    }
}
