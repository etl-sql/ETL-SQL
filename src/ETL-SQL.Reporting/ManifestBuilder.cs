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
        public async Task<ReportManifest> BuildAsync(string scriptSource, Dictionary<string, string>? interactionValues = null)
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

            // ── Visuals ──────────────────────────────────────────────────────
            foreach (var (name, vStmt) in _ctx.ReportContext.VisualDefinitions)
            {
                manifest.Visuals.Add(await _visualBuilder.BuildAsync(name, vStmt, interactionValues));
            }

            // ── Pages ────────────────────────────────────────────────────────
            foreach (var (name, pStmt) in _ctx.ReportContext.PageDefinitions)
            {
                manifest.Pages.Add(_pageBuilder.Build(name, pStmt, reportStyles));
            }

            // ── Containers ───────────────────────────────────────────────────
            if (_ctx.ReportContext.ContainerDefinitions.Count > 0)
            {
                manifest.Containers = new();
                foreach (var (name, cStmt) in _ctx.ReportContext.ContainerDefinitions)
                {
                    var resolvedStyles = _styleBuilder.ResolveStyles(cStmt.StyleName, cStmt.Styles, reportStyles);
                    var (title, titleMd) = _styleBuilder.ResolveMarkdown(cStmt.Title, cStmt.TitleIsMarkdown);
                    var (subtitle, subtitleMd) = _styleBuilder.ResolveMarkdown(cStmt.Subtitle, cStmt.SubtitleIsMarkdown);

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
                        Tooltip            = _styleBuilder.BuildTooltipManifest(cStmt.Tooltip),
                        IsCollapsible      = cStmt.IsCollapsible,
                        Icon               = cStmt.Icon,
                        IsPinnable         = cStmt.IsPinnable,
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
                        .Where(p => !(_ctx.ReportContext.PageDefinitions.TryGetValue(p, out var pd) && pd.IsHidden))
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
                    var bm = new ButtonManifest
                    {
                        Name       = name,
                        ButtonType = bStmt.ButtonType,
                        Title      = bStmt.Title,
                        Tooltip    = _styleBuilder.BuildTooltipManifest(bStmt.Tooltip),
                        Styles     = resolvedStyles.Count > 0 ? resolvedStyles : new Dictionary<string, string>()
                    };

                    if (!bm.Styles!.ContainsKey("EXPORT"))
                        bm.Styles["EXPORT"] = "OFF";

                    foreach (var opt in bStmt.Options)
                        bm.Options[opt.Key] = opt.Value;

                    // Implied actions for special types if none specified
                    if (bStmt.Actions.Count == 0)
                    {
                        var type = bStmt.ButtonType.ToUpperInvariant();
                        if (type == "CLEAR_FILTERS")
                            bm.Actions.Add(new VisualActionManifest { Type = "CLEAR_FILTERS", Trigger = "ON_CLICK" });
                        else if (type == "REFRESH")
                            bm.Actions.Add(new VisualActionManifest { Type = "REFRESH", Trigger = "ON_CLICK" });
                        else if (type == "BACK")
                            bm.Actions.Add(new VisualActionManifest { Type = "BACK", Trigger = "ON_CLICK" });
                        else if (type == "RUN")
                            bm.Actions.Add(new VisualActionManifest { Type = "APPLY_PARAMETERS", Trigger = "ON_CLICK" });
                    }

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
        /// Re-queries the data for a specific visual and updates its Row/Column collections.
        /// Also regenerates the ChartConfig.
        /// </summary>
        public async Task RefreshVisualAsync(CreateVisualStatement vStmt, VisualManifest vm, Dictionary<string, string>? interactionValues = null)
        {
            // Refresh logic is now shared via VisualBuilder
            var newVm = await _visualBuilder.BuildAsync(vm.Name, vStmt, interactionValues);
            vm.Rows = newVm.Rows;
            vm.Columns = newVm.Columns;
            vm.Error = newVm.Error;
            vm.ChartConfig = newVm.ChartConfig;
            vm.Options = newVm.Options;
            vm.Actions = newVm.Actions;
            vm.Styles = newVm.Styles;
            vm.SeriesDefs = newVm.SeriesDefs;
            vm.FormattingRules = newVm.FormattingRules;
            vm.RowStyles = newVm.RowStyles;
            vm.Overlays = newVm.Overlays;
            vm.HighlightRows = newVm.HighlightRows;
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
                    KeyColumn    = dd.KeyColumn
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
                    Parameters = rs.Parameters
                },
                ApplyParametersAction ap => new VisualActionManifest
                {
                    Type    = "APPLY_PARAMETERS",
                    Trigger = ap.Trigger
                },
                _ => new VisualActionManifest { Type = "UNKNOWN", Trigger = action.Trigger }
            };
        }
    }
}
