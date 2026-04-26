using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.ReportBuilder.Builders;

namespace ETL_SQL.ReportBuilder
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
        public async Task<ReportManifest> BuildAsync(string scriptSource)
        {
            var manifest = new ReportManifest
            {
                Source      = scriptSource,
                BuiltAt     = DateTime.UtcNow,
                Title           = _ctx.ReportTitle,
                TitleIsMarkdown  = _ctx.ReportTitleIsMarkdown,
                Description     = _ctx.ReportDescription,
                Css             = _ctx.ReportCss,
                Js              = _ctx.ReportJs,
                HtmlHead        = _ctx.ReportHtmlHead,
                HtmlBody        = _ctx.ReportHtmlBody,
                HtmlFooter      = _ctx.ReportHtmlFooter,
                Favicon         = _ctx.ReportFavicon,
                Logo            = _ctx.ReportLogo,
                Background      = _ctx.ReportBackground,
                Theme           = _ctx.ReportTheme,
                Navigation      = _ctx.ReportNavigation
            };

            // ── Visuals ──────────────────────────────────────────────────────
            foreach (var (name, vStmt) in _ctx.VisualDefinitions)
            {
                manifest.Visuals.Add(await _visualBuilder.BuildAsync(name, vStmt));
            }

            // ── Pages ────────────────────────────────────────────────────────
            foreach (var (name, pStmt) in _ctx.PageDefinitions)
            {
                manifest.Pages.Add(_pageBuilder.Build(name, pStmt));
            }

            // ── Containers ───────────────────────────────────────────────────
            if (_ctx.ContainerDefinitions.Count > 0)
            {
                manifest.Containers = new();
                foreach (var (name, cStmt) in _ctx.ContainerDefinitions)
                {
                    var resolvedStyles = _styleBuilder.ResolveStyles(cStmt.StyleName, cStmt.Styles);
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
                        Styles             = resolvedStyles.Count > 0 ? resolvedStyles : null
                    });
                }
            }

            // ── Navigations ──────────────────────────────────────────────────
            if (_ctx.NavigationDefinitions.Count > 0)
            {
                manifest.Navigations = new();
                foreach (var (name, nStmt) in _ctx.NavigationDefinitions)
                {
                    manifest.Navigations.Add(new NavigationManifest
                    {
                        Name        = name,
                        NavType     = nStmt.NavType.ToString().ToUpperInvariant(),
                        Orientation = nStmt.Orientation.ToString().ToUpperInvariant(),
                        DefaultPage = nStmt.DefaultPage,
                        Pages       = new List<string>(nStmt.Pages)
                    });
                }
            }

            // ── Buttons ──────────────────────────────────────────────────────
            if (_ctx.ButtonDefinitions.Count > 0)
            {
                manifest.Buttons = new();
                foreach (var (name, bStmt) in _ctx.ButtonDefinitions)
                {
                    var resolvedStyles = _styleBuilder.ResolveStyles(bStmt.StyleName, bStmt.Styles);
                    var bm = new ButtonManifest
                    {
                        Name       = name,
                        ButtonType = bStmt.ButtonType,
                        Title      = bStmt.Title,
                        Tooltip    = _styleBuilder.BuildTooltipManifest(bStmt.Tooltip),
                        Styles     = resolvedStyles.Count > 0 ? resolvedStyles : null
                    };

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
            foreach (var (tableName, dStmt) in _ctx.DatasetDefinitions)
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

            // ── Parameters ──────────────────────────────────────────────────
            if (_ctx is IVariableContext vctx)
            {
                foreach (var (name, varMeta) in vctx.VariableMetadata)
                {
                    if (varMeta.IsInput)
                        manifest.Parameters[name] = vctx.Variables.TryGetValue(name, out var val) ? val?.ToString() ?? "" : "";
                }
            }

            return manifest;
        }

        /// <summary>
        /// Re-queries the data for a specific visual and updates its Row/Column collections.
        /// Also regenerates the ChartConfig.
        /// </summary>
        public async Task RefreshVisualAsync(CreateVisualStatement vStmt, VisualManifest vm)
        {
            // Refresh logic is now shared via VisualBuilder
            var newVm = await _visualBuilder.BuildAsync(vm.Name, vStmt);
            vm.Rows = newVm.Rows;
            vm.Columns = newVm.Columns;
            vm.Error = newVm.Error;
            vm.ChartConfig = newVm.ChartConfig;
            vm.Options = newVm.Options;
            vm.Actions = newVm.Actions;
            vm.Styles = newVm.Styles;
            vm.SeriesDefs = newVm.SeriesDefs;
            vm.FormattingRules = newVm.FormattingRules;
            vm.Overlays = newVm.Overlays;
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
                    ValueExpression = sp.ValueExpression
                },
                _ => new VisualActionManifest { Type = "UNKNOWN", Trigger = action.Trigger }
            };
        }
    }
}
