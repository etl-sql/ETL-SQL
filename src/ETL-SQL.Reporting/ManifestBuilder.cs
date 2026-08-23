using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        private readonly int _maxVisualParallelism;

        public ManifestBuilder(IExecutionContext ctx, int? maxVisualParallelism = null)
        {
            _ctx = ctx;
            _maxVisualParallelism = ResolveMaxVisualParallelism(ctx, maxVisualParallelism);
            _styleBuilder = new StyleBuilder(ctx);
            _visualBuilder = new VisualBuilder(ctx, _styleBuilder);
            _pageBuilder = new PageBuilder(_styleBuilder);
            _datasetBuilder = new DatasetBuilder();
        }

        /// <summary>
        /// Builds the manifest by querying each visual's data source.
        /// Must be called after the script has been fully evaluated.
        /// </summary>
        public async Task<ReportManifest> BuildAsync(
            string scriptSource,
            Dictionary<string, string>? interactionValues = null,
            bool skipDeferredVisuals = false,
            IReadOnlySet<string>? runPages = null)
        {
            var manifest = new ReportManifest
            {
                Source = scriptSource,
                BuiltAt = DateTime.UtcNow,
                IsInteraction = (interactionValues != null && interactionValues.Count > 0) || (_ctx.ReportContext.BaselineParameters.Count > 0 && _ctx.VarContext.Variables.Any(v => _ctx.ReportContext.BaselineParameters.TryGetValue(v.Key, out var baseVal) && String.Compare(v.Value?.ToString() ?? "", baseVal ?? "", true) != 0)),
                Title = _ctx.ReportContext.ReportTitle,
                TitleIsMarkdown = _ctx.ReportContext.ReportTitleIsMarkdown,
                Description = _ctx.ReportContext.ReportDescription,
                Css = _ctx.ReportContext.ReportCss,
                Js = _ctx.ReportContext.ReportJs,
                HtmlHead = _ctx.ReportContext.ReportHtmlHead,
                HtmlBody = _ctx.ReportContext.ReportHtmlBody,
                HtmlFooter = _ctx.ReportContext.ReportHtmlFooter,
                Favicon = _ctx.ReportContext.ReportFavicon,
                Logo = _ctx.ReportContext.ReportLogo,
                Background = _ctx.ReportContext.ReportBackground,
                Theme = _ctx.ReportContext.ReportTheme,
                Navigation = _ctx.ReportContext.ReportNavigation,
            };
            RefreshTelemetry(manifest);
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

            var deferredVisuals = DetermineDeferredVisuals(runPages);

            // ── Visuals ──────────────────────────────────────────────────────
            await BuildVisualsAsync(manifest, interactionValues, deferredVisuals);
            BoundRowDetailVisuals(manifest);
            RefreshTelemetry(manifest);

            // ── Pages ────────────────────────────────────────────────────────
            foreach (var (name, pStmt) in _ctx.ReportContext.PageDefinitions)
            {
                manifest.Pages.Add(await _pageBuilder.BuildAsync(name, pStmt, _ctx, reportStyles));
            }

            var compiler = new PhysicalPageCompiler();
            foreach (var page in manifest.Pages)
            {
                if (page.Mode.Equals("PAGINATED", StringComparison.OrdinalIgnoreCase) || page.PrintLayout != null)
                {
                    page.PhysicalPages = compiler.Compile(page, manifest);
                }
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
                        Name = name,
                        ContainerType = cStmt.ContainerType,
                        Structure = cStmt.Structure,
                        SlotMap = cStmt.SlotMap.ToDictionary(kv => kv.Key, kv => kv.Value),
                        Title = title,
                        TitleIsMarkdown = titleMd,
                        Subtitle = subtitle,
                        SubtitleIsMarkdown = subtitleMd,
                        Tooltip = await _styleBuilder.BuildTooltipManifestAsync(cStmt.Tooltip),
                        IsCollapsible = cStmt.IsCollapsible,
                        Icon = cStmt.Icon,
                        IsPinnable = cStmt.IsPinnable,
                        IsHidden = ResolveVisibility(cStmt.Visibility),
                        Styles = resolvedStyles.Count > 0 ? resolvedStyles : null

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
                        Name = name,
                        NavType = nStmt.NavType.ToString().ToUpperInvariant(),
                        Orientation = nStmt.Orientation.ToString().ToUpperInvariant(),
                        DefaultPage = nStmt.DefaultPage,
                        Pages = visiblePages
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
                        Name = name,
                        ButtonType = bStmt.ButtonType,
                        Title = bTitle,
                        Tooltip = await _styleBuilder.BuildTooltipManifestAsync(bStmt.Tooltip),
                        Styles = resolvedStyles.Count > 0 ? resolvedStyles : new Dictionary<string, string>()
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

            // ── Bookmarks ────────────────────────────────────────────────────
            if (_ctx.ReportContext.BookmarkDefinitions.Count > 0)
            {
                manifest.Bookmarks = new List<BookmarkManifest>();
                foreach (var (_, bkStmt) in _ctx.ReportContext.BookmarkDefinitions)
                {
                    var (bkTitle, _) = await _styleBuilder.ResolveMarkdownAsync(bkStmt.Title);
                    var state = new ETL_SQL.Core.Reporting.ResolvedReportState
                    {
                        ActivePage = bkStmt.PageName
                    };
                    foreach (var p in bkStmt.Parameters)
                        state.Parameters[p.ParameterName] = await ResolveBookmarkValueAsync(p.Value);
                    foreach (var s in bkStmt.StateEntries)
                    {
                        if (s.Property == BookmarkStateProperty.Visible)
                            state.Visible[s.ObjectName] = s.On;
                        else
                            state.Collapsed[s.ObjectName] = s.On;
                    }
                    manifest.Bookmarks.Add(new BookmarkManifest
                    {
                        Name = bkStmt.Name,
                        Title = bkTitle,
                        IsDefault = bkStmt.IsDefault,
                        State = state
                    });
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
                    var themeJson = ThemeBuilder.BuildNativeTheme(themeStmt.Properties);
                    using var doc = JsonDocument.Parse(themeJson.ToJsonString());
                    manifest.CustomThemes.Add(new ThemeManifest
                    {
                        Name = themeName,
                        Config = doc.RootElement.Clone()
                    });
                }
            }

            // ── Messages ─────────────────────────────────────────────────────
            var manifestMessages = manifest.Messages;
            manifest.Messages = _ctx.Messages
                .Select(m => new LogEntryManifest(m.Message, m.Color.ToString().ToLowerInvariant(), m.Timestamp))
                .ToList();
            if (manifestMessages is { Count: > 0 })
                manifest.Messages.AddRange(manifestMessages);

            manifest.ExecutionTree = _ctx.Telemetry.ExecutionTree.ToSnapshot();

            return manifest;
        }

        private async Task BuildVisualsAsync(
            ReportManifest manifest,
            Dictionary<string, string>? interactionValues,
            HashSet<string> deferredVisuals)
        {
            var visuals = _ctx.ReportContext.VisualDefinitions
                .Select((entry, index) => new VisualBuildInput(index, entry.Key, entry.Value))
                .ToList();

            if (visuals.Count == 0)
                return;

            ValidateDependencyGraph(visuals);
            var cascadeGraph = CascadingFilterGraphCompiler.Compile(visuals.Select(v => v.Statement));
            if (cascadeGraph.OrderedNodes.Count > 0)
                manifest.CascadeGraph = cascadeGraph.ToManifest();

            if (!CanBuildVisualsInParallel(visuals.Count, interactionValues))
            {
                foreach (var visual in visuals)
                    await BuildVisualSequentialAsync(manifest, visual, interactionValues, deferredVisuals);
                return;
            }

            var firstFork = _ctx.Fork();
            if (ReferenceEquals(firstFork, _ctx))
            {
                foreach (var visual in visuals)
                    await BuildVisualSequentialAsync(manifest, visual, interactionValues, deferredVisuals);
                return;
            }

            using var throttler = new SemaphoreSlim(_maxVisualParallelism);
            var tasks = visuals
                .Select(visual => BuildVisualInForkAsync(
                    visual,
                    interactionValues,
                    deferredVisuals.Contains(visual.Name),
                    throttler,
                    visual.Index == 0 ? firstFork : null))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            foreach (var result in results.OrderBy(r => r.Index))
            {
                if (result.Context != null && !ReferenceEquals(result.Context, _ctx))
                    _ctx.Merge(result.Context);

                if (result.Visual != null)
                    manifest.Visuals.Add(result.Visual);

                if (result.Message != null)
                {
                    manifest.Messages ??= new();
                    manifest.Messages.Add(result.Message);
                    manifest.Error = "One or more visuals failed to build.";
                }
            }
        }

        private async Task BuildVisualSequentialAsync(
            ReportManifest manifest,
            VisualBuildInput input,
            Dictionary<string, string>? interactionValues,
            HashSet<string> deferredVisuals)
        {
            try
            {
                manifest.Visuals.Add(await _visualBuilder.BuildAsync(input.Name, input.Statement, interactionValues, deferredVisuals.Contains(input.Name)));
            }
            catch (Exception ex)
            {
                manifest.Messages ??= new();
                manifest.Messages.Add(new LogEntryManifest($"Failed to build visual '{input.Name}': {ex.Message}", "Red", DateTime.UtcNow));
                manifest.Error = "One or more visuals failed to build.";
            }
        }

        private async Task<VisualBuildResult> BuildVisualInForkAsync(
            VisualBuildInput input,
            Dictionary<string, string>? interactionValues,
            bool skipDeferredVisuals,
            SemaphoreSlim throttler,
            IExecutionContext? visualContext)
        {
            await throttler.WaitAsync();
            try
            {
                visualContext ??= _ctx.Fork();
                if (ReferenceEquals(visualContext, _ctx))
                    throw new InvalidOperationException("Execution context cannot be forked for parallel visual generation.");

                var styleBuilder = new StyleBuilder(visualContext);
                var visualBuilder = new VisualBuilder(visualContext, styleBuilder);
                var visual = await visualBuilder.BuildAsync(input.Name, input.Statement, interactionValues, skipDeferredVisuals);
                return new VisualBuildResult(input.Index, visual, null, visualContext);
            }
            catch (Exception ex)
            {
                return new VisualBuildResult(
                    input.Index,
                    null,
                    new LogEntryManifest($"Failed to build visual '{input.Name}': {ex.Message}", "Red", DateTime.UtcNow),
                    visualContext);
            }
            finally
            {
                throttler.Release();
            }
        }

        private bool CanBuildVisualsInParallel(int visualCount, Dictionary<string, string>? interactionValues)
            => visualCount > 1
               && _maxVisualParallelism > 1
               && (interactionValues == null || interactionValues.Count == 0);

        private static int ResolveMaxVisualParallelism(IExecutionContext ctx, int? requested)
        {
            var requestedValue = requested;
            if (!requestedValue.HasValue)
            {
                var env = Environment.GetEnvironmentVariable("ETLSQL_REPORT_VISUAL_PARALLELISM");
                if (int.TryParse(env, out var envValue))
                    requestedValue = envValue;
            }

            var contextLimit = ctx.MaxParallelDegree > 0 ? ctx.MaxParallelDegree : 1;
            var defaultLimit = Math.Min(4, contextLimit);
            return Math.Max(1, Math.Min(requestedValue ?? defaultLimit, contextLimit));
        }

        private void RefreshTelemetry(ReportManifest manifest)
        {
            manifest.Telemetry = new TelemetryManifest
            {
                RowsProcessed = _ctx.Telemetry.RowsProcessed,
                TotalSpilledBytes = _ctx.Telemetry.TotalSpilledBytes,
                SubqueryCacheHits = _ctx.Telemetry.SubqueryCacheHits,
                SubqueryCacheMisses = _ctx.Telemetry.SubqueryCacheMisses,
                SubquerySpillCount = _ctx.Telemetry.SubquerySpillCount,
                SubquerySpilledBytes = _ctx.Telemetry.SubquerySpilledBytes,
                ExecutionTimeMs = _ctx.Telemetry.LastExecutionTimeMs
            };
        }

        private sealed record VisualBuildInput(int Index, string Name, CreateVisualStatement Statement);

        private sealed record VisualBuildResult(
            int Index,
            VisualManifest? Visual,
            LogEntryManifest? Message,
            IExecutionContext? Context);

        private HashSet<string> DetermineDeferredVisuals(IReadOnlySet<string>? runPages)
        {
            var deferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var runSet = runPages ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (pageName, page) in _ctx.ReportContext.PageDefinitions)
            {
                if (page.PageMode != PageMode.Paginated || runSet.Contains(pageName))
                    continue;

                foreach (var visualName in ExpandPageVisuals(page))
                {
                    if (!_ctx.ReportContext.VisualDefinitions.TryGetValue(visualName, out var visual))
                        continue;

                    if (visual.FetchMode == VisualFetchMode.OnLoad)
                        continue;

                    if (visual.FetchMode == VisualFetchMode.OnRun || !IsPromptVisual(visual.VisualType))
                        deferred.Add(visualName);
                }
            }

            return deferred;
        }

        private IEnumerable<string> ExpandPageVisuals(CreatePageStatement page)
        {
            var seenContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in page.SlotMap.Values)
            {
                foreach (var visual in ExpandObject(target, seenContainers))
                    yield return visual;
            }
        }

        private IEnumerable<string> ExpandObject(string name, HashSet<string> seenContainers)
        {
            if (_ctx.ReportContext.VisualDefinitions.ContainsKey(name))
            {
                yield return name;
                yield break;
            }

            if (!_ctx.ReportContext.ContainerDefinitions.TryGetValue(name, out var container) || !seenContainers.Add(name))
                yield break;

            foreach (var child in container.SlotMap.Values)
            {
                foreach (var visual in ExpandObject(child, seenContainers))
                    yield return visual;
            }
        }

        private static bool IsPromptVisual(VisualType type) => type is
            VisualType.Slicer
            or VisualType.MultiSelect
            or VisualType.DatePicker
            or VisualType.RelDatePicker
            or VisualType.Slider
            or VisualType.Search
            or VisualType.Checkbox
            or VisualType.Textbox
            or VisualType.Numberbox
            or VisualType.Text
            or VisualType.Image;

        /// <summary>
        /// Clears HighlightRows from a visual and regenerates its native SVG without the ghost overlay.
        /// Does not re-query the data source — uses existing Rows.
        /// </summary>
        public void ClearHighlightRows(VisualManifest vm)
        {
            if (vm.HighlightRows == null) return;
            vm.HighlightRows = null;
            vm.ChartConfig = null;
            vm.NativeSvg = new SvgChartRenderer().Render(vm);
        }

        /// <summary>
        /// Re-queries the data for a specific visual and updates its Row/Column collections.
        /// Also regenerates the renderer-neutral plan and native SVG.
        /// </summary>
        public async Task RefreshVisualAsync(CreateVisualStatement vStmt, VisualManifest vm, Dictionary<string, string>? interactionValues = null, VisualDrillState? drillState = null)
        {
            // Refresh logic is now shared via VisualBuilder
            var newVm = await _visualBuilder.BuildAsync(vm.Name, vStmt, interactionValues, drillState: drillState);
            vm.Rows = newVm.Rows;
            vm.Columns = newVm.Columns;
            vm.Error = newVm.Error;
            vm.ChartConfig = newVm.ChartConfig;
            vm.NativeSvg = newVm.NativeSvg;
            vm.ChartSpec = newVm.ChartSpec;
            vm.ChartData = newVm.ChartData;
            vm.PlotPlan = newVm.PlotPlan;
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
            vm.Cascade = newVm.Cascade;
            vm.SemanticFallback = newVm.SemanticFallback;
            vm.MicroCharts = newVm.MicroCharts;
            vm.IsHidden = false; // refreshed visuals are always shown regardless of VISIBLE = OFF
        }

        /// <summary>Builds a detached visual so an interaction transaction can commit it atomically.</summary>
        public Task<VisualManifest> BuildVisualSnapshotAsync(CreateVisualStatement statement) =>
            _visualBuilder.BuildAsync(statement.Name, statement);

        /// <summary>
        /// Resolves a bookmark parameter expression to a typed value, preserving its declared kind.
        /// Literals resolve without evaluation; variable references and collapsed signed literals are
        /// evaluated against the current context.
        /// </summary>
        private async Task<ETL_SQL.Core.Reporting.ReportStateValue> ResolveBookmarkValueAsync(ETL_SQL.Core.Expression expr)
        {
            if (expr is ETL_SQL.Core.LiteralExpression lit)
                return ETL_SQL.Core.Reporting.ReportStateValue.FromLiteral(lit);
            try
            {
                var value = await _ctx.EvaluateValue(expr, null!);
                return ETL_SQL.Core.Reporting.ReportStateValue.FromObject(value);
            }
            catch
            {
                // A value that cannot be resolved at build time (e.g. an undeclared variable) is emitted
                // as null; static validation reports the undeclared reference separately.
                return ETL_SQL.Core.Reporting.ReportStateValue.Null;
            }
        }

        private VisualActionManifest TranslateAction(VisualAction action)
        {
            return action switch
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
                    ValueExpression = sp.ValueExpression,
                    ValueSource = "LITERAL",
                    LiteralValue = sp.ValueExpression
                },
                ClearFiltersAction cf => new VisualActionManifest
                {
                    Type = "CLEAR_FILTERS",
                    Trigger = cf.Trigger
                },
                RunScriptAction rs => new VisualActionManifest
                {
                    Type = "RUN_SCRIPT",
                    Trigger = rs.Trigger,
                    ScriptPath = rs.ScriptPath,
                    Parameters = rs.Parameters,
                    ParameterColumns = rs.Parameters.Where(p => !p.Value.StartsWith("'") && !p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value),
                    LiteralParameters = rs.Parameters.Where(p => p.Value.StartsWith("'") || p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value.Trim('\''))
                },
                DrillReportAction dr => new VisualActionManifest
                {
                    Type = "DRILL_REPORT",
                    Trigger = dr.Trigger,
                    TargetReport = dr.TargetReport,
                    Parameters = dr.Parameters,
                    ParameterColumns = dr.Parameters.Where(p => !p.Value.StartsWith("'") && !p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value),
                    LiteralParameters = dr.Parameters.Where(p => p.Value.StartsWith("'") || p.Value.StartsWith("@")).ToDictionary(p => p.Key, p => p.Value.Trim('\''))
                },
                NavigatePageAction np => new VisualActionManifest
                {
                    Type = "NAVIGATE_PAGE",
                    Trigger = np.Trigger,
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
                ApplyBookmarkAction ab => new VisualActionManifest { Type = "APPLY_BOOKMARK", Trigger = action.Trigger, BookmarkName = ab.BookmarkName },
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

        private void ValidateDependencyGraph(List<VisualBuildInput> visuals)
        {
            var graph = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in visuals)
            {
                if (v.Statement.RowDetail != null && !string.IsNullOrWhiteSpace(v.Statement.RowDetail.TargetName))
                {
                    graph[v.Name] = v.Statement.RowDetail.TargetName;
                }
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in graph.Keys)
            {
                if (HasCycle(node, graph, visited, path, out var cyclePath))
                {
                    var cycleStr = string.Join(" -> ", cyclePath);
                    throw new InvalidOperationException($"Cycle detected in ROW_DETAIL dependencies: {cycleStr}");
                }
            }
        }

        private void BoundRowDetailVisuals(ReportManifest manifest)
        {
            if (manifest.Visuals == null) return;
            var visualDict = manifest.Visuals.ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);

            foreach (var parent in manifest.Visuals)
            {
                if (parent.RowDetail == null || string.IsNullOrWhiteSpace(parent.RowDetail.TargetName) || parent.RowDetailKeys == null)
                    continue;

                if (!visualDict.TryGetValue(parent.RowDetail.TargetName, out var target) || target.Rows == null || target.Columns == null)
                    continue;

                var bindings = parent.RowDetail.Bindings;
                if (bindings == null || bindings.Count == 0) continue;

                int limit = (parent.RowDetail.Limit.HasValue && parent.RowDetail.Limit.Value > 0) ? parent.RowDetail.Limit.Value : 10000;
                var filteredTargetRows = new List<List<string?>>();

                var childColIndices = new int[bindings.Count];
                for (int b = 0; b < bindings.Count; b++)
                {
                    childColIndices[b] = target.Columns.FindIndex(c => c.Equals(bindings[b].ChildParameter, StringComparison.OrdinalIgnoreCase));
                }

                // If any binding column isn't found in target, we can't filter
                if (childColIndices.Any(idx => idx < 0)) continue;

                foreach (var parentKeys in parent.RowDetailKeys)
                {
                    int addedCount = 0;
                    foreach (var row in target.Rows)
                    {
                        bool matches = true;
                        for (int b = 0; b < bindings.Count; b++)
                        {
                            var pVal = parentKeys.TryGetValue(bindings[b].ChildParameter, out var pv) ? pv : null;
                            var cVal = row[childColIndices[b]];
                            if (Convert.ToString(pVal) != Convert.ToString(cVal))
                            {
                                matches = false;
                                break;
                            }
                        }

                        if (matches)
                        {
                            filteredTargetRows.Add(row);
                            addedCount++;
                            if (addedCount >= limit) break;
                        }
                    }
                }

                // Replace target rows with the bounded, reachable set
                target.Rows = filteredTargetRows.Distinct().ToList();
            }
        }

        private bool HasCycle(string current, Dictionary<string, string> graph, HashSet<string> visited, HashSet<string> path, out List<string> cyclePath)
        {
            cyclePath = new List<string>();
            if (path.Contains(current))
            {
                cyclePath.Add(current);
                return true;
            }
            if (visited.Contains(current))
            {
                return false;
            }

            visited.Add(current);
            path.Add(current);

            if (graph.TryGetValue(current, out var next))
            {
                if (HasCycle(next, graph, visited, path, out var subCycle))
                {
                    cyclePath.Add(current);
                    cyclePath.AddRange(subCycle);
                    return true;
                }
            }

            path.Remove(current);
            return false;
        }
    }
}
