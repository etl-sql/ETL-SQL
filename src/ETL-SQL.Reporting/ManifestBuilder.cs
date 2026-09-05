using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Reporting.Builders;
using ETL_SQL.Reporting.Contracts;
using ETL_SQL.Reporting.HtmlVisual;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

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
        /// <param name="deferPaginatedPages">
        /// Screen default: a paginated page waits for the reader to fill in its prompts and click
        /// Run, so its visuals are built without data. An export has no such moment — the file is
        /// the finished document — so an exporter passes false and every page is fetched.
        /// </param>
        public async Task<ReportManifest> BuildAsync(
            string scriptSource,
            Dictionary<string, string>? interactionValues = null,
            bool skipDeferredVisuals = false,
            IReadOnlySet<string>? runPages = null,
            bool deferPaginatedPages = true)
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
                Formatting = Formatting(_ctx.ReportContext.EffectiveFormatting),
            };
            RefreshTelemetry(manifest);
            var reportStyles = _styleBuilder.ResolveReportStyles();
            var reportPalette = _styleBuilder.ResolvePalette(null, ImmutableArray<string>.Empty);
            if (!reportPalette.IsDefaultOrEmpty)
                manifest.Palette = reportPalette.ToList();

            if (reportStyles.Count > 0 || !reportPalette.IsDefaultOrEmpty)
            {
                if (reportStyles.Count > 0) manifest.Styles = reportStyles;
                var reportTokens = _styleBuilder.ResolveDesignTokens(reportStyles, isPageOrReportLevel: true, manifest.Palette);
                if (reportTokens.Count > 0)
                    manifest.DesignTokens = reportTokens;
            }

            // Seed common interaction variables if they don't exist, to prevent expression evaluation errors during manifest generation
            var interactionVars = new[] { "@hover_value", "@click_value", "@selected_value", "@drill_value", "@param_value" };
            foreach (var v in interactionVars)
            {
                if (!_ctx.VarContext.ContainsVariable(v))
                    _ctx.VarContext.DeclareVariable(v, null);
            }

            var deferredVisuals = deferPaginatedPages
                ? DetermineDeferredVisuals(runPages)
                : [];

            // ── Visuals ──────────────────────────────────────────────────────
            await BuildVisualsAsync(manifest, interactionValues, deferredVisuals, reportPalette);
            await ResolveHtmlVisualEmbedsAsync(manifest);
            EnforceHtmlAggregateBudgets(manifest);
            BoundRowDetailVisuals(manifest);
            RefreshTelemetry(manifest);

            // ── Pages ────────────────────────────────────────────────────────
            foreach (var (name, pStmt) in _ctx.ReportContext.PageDefinitions)
            {
                manifest.Pages.Add(await _pageBuilder.BuildAsync(name, pStmt, _ctx, reportStyles, reportPalette));
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
                    var scopedTokenStyles = _styleBuilder.ResolveStyles(cStmt.StyleName, cStmt.Styles);
                    if (cStmt.TitleDefinition != null)
                    {
                        if (cStmt.TitleDefinition.Color != null) resolvedStyles["TITLE_COLOR"] = cStmt.TitleDefinition.Color;
                        if (cStmt.TitleDefinition.Size != null) resolvedStyles["TITLE_SIZE"] = cStmt.TitleDefinition.Size;
                        if (cStmt.TitleDefinition.Weight != null) resolvedStyles["TITLE_WEIGHT"] = cStmt.TitleDefinition.Weight;
                        if (cStmt.TitleDefinition.Font != null) resolvedStyles["TITLE_FONT"] = cStmt.TitleDefinition.Font;
                        if (cStmt.TitleDefinition.Align != null) resolvedStyles["TITLE_ALIGN"] = cStmt.TitleDefinition.Align;
                    }
                    if (cStmt.SubtitleDefinition != null)
                    {
                        if (cStmt.SubtitleDefinition.Color != null)
                        {
                            resolvedStyles["SUBTITLE_COLOR"] = cStmt.SubtitleDefinition.Color;
                            scopedTokenStyles["SUBTITLE_COLOR"] = cStmt.SubtitleDefinition.Color;
                        }
                        if (cStmt.SubtitleDefinition.Size != null) resolvedStyles["SUBTITLE_SIZE"] = cStmt.SubtitleDefinition.Size;
                        if (cStmt.SubtitleDefinition.Weight != null) resolvedStyles["SUBTITLE_WEIGHT"] = cStmt.SubtitleDefinition.Weight;
                        if (cStmt.SubtitleDefinition.Font != null) resolvedStyles["SUBTITLE_FONT"] = cStmt.SubtitleDefinition.Font;
                        if (cStmt.SubtitleDefinition.Align != null) resolvedStyles["SUBTITLE_ALIGN"] = cStmt.SubtitleDefinition.Align;
                    }

                    var titleExpr = cStmt.TitleDefinition?.Text ?? cStmt.Title;
                    var titleIsMd = cStmt.TitleDefinition?.IsMarkdown ?? cStmt.TitleIsMarkdown;
                    var (title, titleMd) = await _styleBuilder.ResolveMarkdownAsync(titleExpr, titleIsMd);

                    var subtitleExpr = cStmt.SubtitleDefinition?.Text ?? cStmt.Subtitle;
                    var subtitleIsMd = cStmt.SubtitleDefinition?.IsMarkdown ?? cStmt.SubtitleIsMarkdown;
                    var (subtitle, subtitleMd) = await _styleBuilder.ResolveMarkdownAsync(subtitleExpr, subtitleIsMd);

                    var inhPalette = ResolveContainerInheritedPalette(name, reportPalette);
                    var resolvedContainerPalette = _styleBuilder.ResolvePalette(cStmt.StyleName, cStmt.Palette, inhPalette);

                    var isDarkCont = string.Equals(resolvedStyles.GetValueOrDefault("THEME"), "DARK", StringComparison.OrdinalIgnoreCase);
                    var bgCandidateCont = resolvedStyles.GetValueOrDefault("BACKGROUND")
                        ?? resolvedStyles.GetValueOrDefault("BACKGROUND_COLOR")
                        ?? resolvedStyles.GetValueOrDefault("BG")
                        ?? (isDarkCont ? "#1e1e1e" : "#ffffff");

                    var effectiveBgCont = ColorContrast.TryParseHexColor(bgCandidateCont, out _, out _, out _, out _)
                        ? bgCandidateCont
                        : (isDarkCont ? "#1e1e1e" : "#ffffff");

                    var validContainerPalette = new List<string>();
                    if (!resolvedContainerPalette.IsDefaultOrEmpty)
                    {
                        foreach (var color in resolvedContainerPalette)
                        {
                            if (ColorContrast.TryParseHexColor(color, out _, out _, out _, out _) && DesignTokens.IsSafeCssValue(color))
                            {
                                var eval = ColorContrast.Evaluate(color, effectiveBgCont, minRatio: 3.0);
                                if (eval.Passed)
                                {
                                    validContainerPalette.Add(color);
                                }
                            }
                        }
                    }
                    var containerPaletteList = validContainerPalette.Count > 0 ? validContainerPalette : null;

                    int? containerRefresh = null;
                    if (cStmt.Options.TryGetValue("REFRESH", out var refStr) && int.TryParse(refStr, out var refSecs))
                        containerRefresh = refSecs;

                    Dictionary<string, ContainerSlotManifest>? slotDetails = null;
                    if (cStmt.SlotDefinitions.Count > 0)
                    {
                        slotDetails = cStmt.SlotDefinitions.ToDictionary(
                            kv => kv.Key,
                            kv => new ContainerSlotManifest
                            {
                                Visual = kv.Value.Visual,
                                Icon = kv.Value.Icon,
                                Badge = kv.Value.Badge
                            });
                    }

                    bool isCol = cStmt.IsCollapsible || (cStmt.Options.TryGetValue("COLLAPSIBLE", out var colVal) && string.Equals(colVal, "ON", StringComparison.OrdinalIgnoreCase));

                    manifest.Containers.Add(new ContainerManifest
                    {
                        Name = name,
                        ContainerType = cStmt.ContainerType,
                        Structure = cStmt.Structure,
                        SlotMap = cStmt.SlotMap.ToDictionary(kv => kv.Key, kv => kv.Value),
                        SlotDetails = slotDetails,
                        Options = cStmt.Options.Count > 0 ? cStmt.Options : null,
                        Refresh = containerRefresh,
                        Title = title,
                        TitleIsMarkdown = titleMd,
                        Subtitle = subtitle,
                        SubtitleIsMarkdown = subtitleMd,
                        Tooltip = await _styleBuilder.BuildTooltipManifestAsync(cStmt.Tooltip, cStmt.Name),
                        IsCollapsible = isCol,
                        Icon = cStmt.Icon,
                        IsPinnable = cStmt.IsPinnable,
                        IsHidden = ResolveVisibility(cStmt.Visibility),
                        Styles = resolvedStyles.Count > 0 ? resolvedStyles : null,
                        Palette = containerPaletteList,
                        DesignTokens = _styleBuilder.ResolveDesignTokens(scopedTokenStyles, isPageOrReportLevel: false, palette: containerPaletteList) is { Count: > 0 } cTokens ? cTokens : null
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

                    var navItems = nStmt.Items.Select(item => new NavigationItemManifest
                    {
                        PageName = item.PageName,
                        Label = item.Label,
                        Icon = item.Icon,
                        Badge = item.Badge,
                        ExternalUrl = item.ExternalUrl,
                        Target = item.Target,
                        IsExternalLink = item.IsExternalLink
                    }).ToList();

                    var navGroups = nStmt.Groups.Select(g => new NavigationGroupManifest
                    {
                        Title = g.Title,
                        Items = g.Items.Select(item => new NavigationItemManifest
                        {
                            PageName = item.PageName,
                            Label = item.Label,
                            Icon = item.Icon,
                            Badge = item.Badge,
                            ExternalUrl = item.ExternalUrl,
                            Target = item.Target,
                            IsExternalLink = item.IsExternalLink
                        }).ToList()
                    }).ToList();

                    var resolvedNavStyles = _styleBuilder.ResolveStyles(null, nStmt.Styles, reportStyles);
                    var resolvedNavActiveStyles = _styleBuilder.ResolveStyles(null, nStmt.ActiveStyles, reportStyles);

                    manifest.Navigations.Add(new NavigationManifest
                    {
                        Name = name,
                        NavType = nStmt.NavType.ToString().ToUpperInvariant(),
                        Orientation = nStmt.Orientation.ToString().ToUpperInvariant(),
                        DefaultPage = nStmt.DefaultPage,
                        Pages = visiblePages,
                        HideInvisible = nStmt.HideInvisible || (nStmt.Options.TryGetValue("HIDE_INVISIBLE", out var hi) && hi.Equals("ON", StringComparison.OrdinalIgnoreCase)),
                        Items = navItems.Count > 0 ? navItems : null,
                        Groups = navGroups.Count > 0 ? navGroups : null,
                        Options = nStmt.Options.Count > 0 ? new Dictionary<string, string>(nStmt.Options) : null,
                        Styles = resolvedNavStyles.Count > 0 ? resolvedNavStyles : null,
                        ActiveStyles = resolvedNavActiveStyles.Count > 0 ? resolvedNavActiveStyles : null
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
                        Tooltip = await _styleBuilder.BuildTooltipManifestAsync(bStmt.Tooltip, bStmt.Name),
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
                    var themeJson = ThemeBuilder.BuildNativeTheme(themeStmt.Properties, themeStmt.VisualOverrides);
                    using var doc = JsonDocument.Parse(themeJson.ToJsonString());
                    var themeTokens = ETL_SQL.Reporting.Semantics.DesignTokenResolver.ResolveScopedTokens(themeStmt.Properties, isPageOrReportLevel: true);
                    manifest.CustomThemes.Add(new ThemeManifest
                    {
                        Name = themeName,
                        Config = doc.RootElement.Clone(),
                        DesignTokens = themeTokens.Count > 0 ? themeTokens : null
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

            // Payload size is the one detail-surface budget that cannot be checked while
            // resolving the AST: it depends on the rows the surface's visuals actually
            // returned, which only exist now.
            DetailSurfacePayloadGuard.Enforce(manifest);

            return manifest;
        }

        private async Task BuildVisualsAsync(
            ReportManifest manifest,
            Dictionary<string, string>? interactionValues,
            HashSet<string> deferredVisuals,
            ImmutableArray<string> reportPalette)
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

            var (visualContexts, conflicts) = ResolveVisualInheritedContexts(reportPalette);

            if (!CanBuildVisualsInParallel(visuals.Count, interactionValues))
            {
                foreach (var visual in visuals)
                {
                    var (inhPalette, inhStyles) = visualContexts.GetValueOrDefault(visual.Name, (reportPalette, _styleBuilder.ResolveReportStyles()));
                    await BuildVisualSequentialAsync(manifest, visual, interactionValues, deferredVisuals, inhPalette, inhStyles);
                }
                AttachConflicts(manifest, conflicts);
                return;
            }

            var firstFork = _ctx.Fork();
            if (ReferenceEquals(firstFork, _ctx))
            {
                foreach (var visual in visuals)
                {
                    var (inhPalette, inhStyles) = visualContexts.GetValueOrDefault(visual.Name, (reportPalette, _styleBuilder.ResolveReportStyles()));
                    await BuildVisualSequentialAsync(manifest, visual, interactionValues, deferredVisuals, inhPalette, inhStyles);
                }
                AttachConflicts(manifest, conflicts);
                return;
            }

            using var throttler = new SemaphoreSlim(_maxVisualParallelism);
            var tasks = visuals
                .Select(visual =>
                {
                    var (inhPalette, inhStyles) = visualContexts.GetValueOrDefault(visual.Name, (reportPalette, _styleBuilder.ResolveReportStyles()));
                    return BuildVisualInForkAsync(
                        visual,
                        interactionValues,
                        deferredVisuals.Contains(visual.Name),
                        throttler,
                        visual.Index == 0 ? firstFork : null,
                        inhPalette,
                        inhStyles);
                })
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

            AttachConflicts(manifest, conflicts);
        }

        private static void AttachConflicts(ReportManifest manifest, List<string> conflicts)
        {
            if (conflicts.Count == 0) return;
            foreach (var conflict in conflicts)
            {
                manifest.Messages ??= new();
                manifest.Messages.Add(new LogEntryManifest(conflict, "warning", DateTime.UtcNow));
                foreach (var vm in manifest.Visuals)
                {
                    if (conflict.Contains($"Visual '{vm.Name}'"))
                    {
                        vm.Diagnostics ??= new List<VisualDiagnosticManifest>();
                        vm.Diagnostics.Add(new VisualDiagnosticManifest
                        {
                            Code = "PALETTE_INHERITANCE_CONFLICT",
                            Message = conflict
                        });
                    }
                }
            }
        }

        private ImmutableArray<string> ResolveContainerInheritedPalette(string containerName, ImmutableArray<string> reportPalette)
        {
            foreach (var (_, page) in _ctx.ReportContext.PageDefinitions)
            {
                var pagePalette = _styleBuilder.ResolvePalette(page.StyleName, page.Palette, reportPalette);
                if (page.SlotMap.Values.Any(v => v.Equals(containerName, StringComparison.OrdinalIgnoreCase)))
                {
                    return pagePalette;
                }
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in page.SlotMap.Values)
                {
                    if (_ctx.ReportContext.ContainerDefinitions.TryGetValue(target, out var parentContainer))
                    {
                        var found = FindParentContainerPalette(parentContainer, containerName, pagePalette, seen);
                        if (found.HasValue) return found.Value;
                    }
                }
            }
            return reportPalette;
        }

        private ImmutableArray<string>? FindParentContainerPalette(
            CreateContainerStatement parentContainer,
            string targetName,
            ImmutableArray<string> currentPalette,
            HashSet<string> seen)
        {
            if (!seen.Add(parentContainer.Name)) return null;
            var containerPalette = _styleBuilder.ResolvePalette(parentContainer.StyleName, parentContainer.Palette, currentPalette);
            if (parentContainer.SlotMap.Values.Any(v => v.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
            {
                return containerPalette;
            }
            foreach (var child in parentContainer.SlotMap.Values)
            {
                if (_ctx.ReportContext.ContainerDefinitions.TryGetValue(child, out var subContainer))
                {
                    var found = FindParentContainerPalette(subContainer, targetName, containerPalette, seen);
                    if (found.HasValue) return found;
                }
            }
            return null;
        }

        private (Dictionary<string, (ImmutableArray<string> Palette, IReadOnlyDictionary<string, string> Styles)> Contexts, List<string> Conflicts)
            ResolveVisualInheritedContexts(ImmutableArray<string> reportPalette)
        {
            var reportStyles = _styleBuilder.ResolveReportStyles();
            var contexts = new Dictionary<string, (ImmutableArray<string> Palette, IReadOnlyDictionary<string, string> Styles)>(StringComparer.OrdinalIgnoreCase);
            var pageOrigins = new Dictionary<string, (string PageName, ImmutableArray<string> Palette)>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new List<string>();

            foreach (var (pageName, page) in _ctx.ReportContext.PageDefinitions)
            {
                var pagePalette = _styleBuilder.ResolvePalette(page.StyleName, page.Palette, reportPalette);
                var pageStyles = _styleBuilder.ResolveStyles(page.StyleName, page.Styles, reportStyles);

                var seenContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in page.SlotMap.Values)
                {
                    PropagateVisualContext(target, pageName, pagePalette, pageStyles, contexts, pageOrigins, conflicts, seenContainers);
                }
            }

            return (contexts, conflicts);
        }

        private void PropagateVisualContext(
            string objectName,
            string pageName,
            ImmutableArray<string> currentPalette,
            IReadOnlyDictionary<string, string> currentStyles,
            Dictionary<string, (ImmutableArray<string> Palette, IReadOnlyDictionary<string, string> Styles)> contexts,
            Dictionary<string, (string PageName, ImmutableArray<string> Palette)> pageOrigins,
            List<string> conflicts,
            HashSet<string> seenContainers)
        {
            if (_ctx.ReportContext.VisualDefinitions.TryGetValue(objectName, out var vStmt))
            {
                var hasExplicitPalette = !vStmt.Palette.IsDefaultOrEmpty ||
                    (!string.IsNullOrEmpty(vStmt.StyleName) && _ctx.ReportContext.StyleDefinitions.TryGetValue(vStmt.StyleName, out var sDef) && (!sDef.Palette.IsDefaultOrEmpty || !string.IsNullOrEmpty(sDef.StyleName)));

                if (!hasExplicitPalette && pageOrigins.TryGetValue(objectName, out var prior))
                {
                    if (prior.PageName != pageName && !PalettesEqual(prior.Palette, currentPalette))
                    {
                        conflicts.Add($"Visual '{objectName}' is referenced on multiple pages with conflicting inherited palettes ('{prior.PageName}' and '{pageName}'). Define a dedicated visual or assign an explicit STYLE on the visual.");
                    }
                }
                else
                {
                    pageOrigins[objectName] = (pageName, currentPalette);
                }

                if (!contexts.ContainsKey(objectName))
                {
                    contexts[objectName] = (currentPalette, currentStyles);
                }
                return;
            }

            if (!_ctx.ReportContext.ContainerDefinitions.TryGetValue(objectName, out var container) || !seenContainers.Add(objectName))
                return;

            var containerPalette = _styleBuilder.ResolvePalette(container.StyleName, container.Palette, currentPalette);
            var containerStyles = _styleBuilder.ResolveStyles(container.StyleName, container.Styles, currentStyles);

            foreach (var child in container.SlotMap.Values)
            {
                PropagateVisualContext(child, pageName, containerPalette, containerStyles, contexts, pageOrigins, conflicts, seenContainers);
            }
        }

        private static bool PalettesEqual(ImmutableArray<string> p1, ImmutableArray<string> p2)
        {
            if (p1.IsDefaultOrEmpty && p2.IsDefaultOrEmpty) return true;
            if (p1.IsDefaultOrEmpty || p2.IsDefaultOrEmpty) return false;
            if (p1.Length != p2.Length) return false;
            for (var i = 0; i < p1.Length; i++)
            {
                if (!string.Equals(p1[i], p2[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private (ImmutableArray<string> Palette, IReadOnlyDictionary<string, string> Styles) ResolveVisualInheritance(string visualName)
        {
            var reportStyles = _styleBuilder.ResolveReportStyles();
            var reportPalette = _styleBuilder.ResolvePalette(null, ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
            foreach (var (_, page) in _ctx.ReportContext.PageDefinitions)
            {
                var pagePalette = _styleBuilder.ResolvePalette(page.StyleName, page.Palette, reportPalette);
                var pageStyles = _styleBuilder.ResolveStyles(page.StyleName, page.Styles, reportStyles);

                if (page.SlotMap.Values.Any(v => v.Equals(visualName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (pagePalette, pageStyles);
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in page.SlotMap.Values)
                {
                    if (_ctx.ReportContext.ContainerDefinitions.TryGetValue(target, out var container))
                    {
                        var res = SearchContainerForVisual(container, visualName, pagePalette, pageStyles, seen);
                        if (res != null) return res.Value;
                    }
                }
            }

            return (reportPalette, reportStyles);
        }

        private (ImmutableArray<string> Palette, IReadOnlyDictionary<string, string> Styles)? SearchContainerForVisual(
            CreateContainerStatement container,
            string visualName,
            ImmutableArray<string> parentPalette,
            IReadOnlyDictionary<string, string> parentStyles,
            HashSet<string> seen)
        {
            if (!seen.Add(container.Name)) return null;

            var containerPalette = _styleBuilder.ResolvePalette(container.StyleName, container.Palette, parentPalette);
            var containerStyles = _styleBuilder.ResolveStyles(container.StyleName, container.Styles, parentStyles);

            if (container.SlotMap.Values.Any(v => v.Equals(visualName, StringComparison.OrdinalIgnoreCase)))
            {
                return (containerPalette, containerStyles);
            }

            foreach (var child in container.SlotMap.Values)
            {
                if (_ctx.ReportContext.ContainerDefinitions.TryGetValue(child, out var subContainer))
                {
                    var res = SearchContainerForVisual(subContainer, visualName, containerPalette, containerStyles, seen);
                    if (res != null) return res;
                }
            }

            return null;
        }
        private async Task BuildVisualSequentialAsync(
            ReportManifest manifest,
            VisualBuildInput input,
            Dictionary<string, string>? interactionValues,
            HashSet<string> deferredVisuals,
            ImmutableArray<string> inheritedPalette,
            IReadOnlyDictionary<string, string>? inheritedStyles = null)
        {
            try
            {
                manifest.Visuals.Add(await _visualBuilder.BuildAsync(input.Name, input.Statement, interactionValues, deferredVisuals.Contains(input.Name), inheritedPalette: inheritedPalette, inheritedStyles: inheritedStyles));
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
            IExecutionContext? visualContext,
            ImmutableArray<string> inheritedPalette,
            IReadOnlyDictionary<string, string>? inheritedStyles = null)
        {
            await throttler.WaitAsync();
            try
            {
                visualContext ??= _ctx.Fork();
                if (ReferenceEquals(visualContext, _ctx))
                    throw new InvalidOperationException("Execution context cannot be forked for parallel visual generation.");

                var styleBuilder = new StyleBuilder(visualContext);
                var visualBuilder = new VisualBuilder(visualContext, styleBuilder);
                var visual = await visualBuilder.BuildAsync(input.Name, input.Statement, interactionValues, skipDeferredVisuals, inheritedPalette: inheritedPalette, inheritedStyles: inheritedStyles);
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

        private static ReportFormattingManifest Formatting(ETL_SQL.Core.Reporting.ReportFormattingSettings settings) =>
            new() { Locale = settings.Locale, TimeZone = settings.TimeZone, NullLabel = settings.NullLabel };

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
            var (inhPalette, inhStyles) = ResolveVisualInheritance(vm.Name);
            var newVm = await _visualBuilder.BuildAsync(vm.Name, vStmt, interactionValues, drillState: drillState, inheritedPalette: inhPalette, inheritedStyles: inhStyles);
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
            vm.Interaction = newVm.Interaction;
            vm.Styles = newVm.Styles;
            vm.DesignTokens = newVm.DesignTokens;
            vm.Palette = newVm.Palette;
            vm.SeriesDefs = newVm.SeriesDefs;
            vm.FormattingRules = newVm.FormattingRules;
            vm.RowStyles = newVm.RowStyles;
            vm.Overlays = newVm.Overlays;
            vm.HighlightRows = newVm.HighlightRows;
            vm.DrillState = newVm.DrillState;
            vm.Cascade = newVm.Cascade;
            vm.SemanticFallback = newVm.SemanticFallback;
            vm.MicroCharts = newVm.MicroCharts;
            vm.HtmlContent = newVm.HtmlContent;
            vm.HtmlCss = newVm.HtmlCss;
            vm.HtmlFallback = newVm.HtmlFallback;
            vm.HtmlMode = newVm.HtmlMode;
            vm.HtmlCost = newVm.HtmlCost;
            vm.HtmlEmbeds = newVm.HtmlEmbeds;
            vm.IsHidden = false; // refreshed visuals are always shown regardless of VISIBLE = OFF
        }

        private async Task ResolveHtmlVisualEmbedsAsync(ReportManifest manifest)
        {
            var definitions = _ctx.ReportContext.VisualDefinitions
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
            var manifests = manifest.Visuals
                .ToDictionary(visual => visual.Name, StringComparer.OrdinalIgnoreCase);
            var graph = definitions.Values
                .Where(statement => statement.VisualType == VisualType.Html && statement.HtmlTemplate is not null)
                .ToDictionary(
                    statement => statement.Name,
                    statement => ConstrainedHtmlPolicy.EmbeddedVisuals(statement.HtmlTemplate!.Template),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var root in graph.Keys)
            {
                var error = ValidateHtmlEmbedGraph(root, root, 0, [], graph, definitions);
                if (error is not null && manifests.TryGetValue(root, out var visual))
                    FailHtmlVisual(manifest, visual, error);
            }

            var queryCount = 0;
            foreach (var visual in manifest.Visuals.Where(visual => visual.HtmlEmbeds is { Count: > 0 }).ToList())
            {
                if (visual.Error is not null) continue;
                await ResolveEmbedsAsync(visual, 0);
            }

            async Task ResolveEmbedsAsync(VisualManifest owner, int depth)
            {
                if (owner.HtmlEmbeds is not { Count: > 0 }) return;
                if (depth >= ConstrainedHtmlPolicy.MaxEmbedDepth)
                {
                    FailHtmlVisual(manifest, owner,
                        $"RPT3011: HTML visual '{owner.Name}' embed depth exceeds {ConstrainedHtmlPolicy.MaxEmbedDepth}.");
                    return;
                }

                foreach (var embed in owner.HtmlEmbeds)
                {
                    if (!definitions.TryGetValue(embed.TargetName, out var targetDefinition)
                        || !manifests.TryGetValue(embed.TargetName, out var targetManifest))
                    {
                        FailHtmlVisual(manifest, owner,
                            $"RPT3010: HTML visual '{owner.Name}' embeds missing visual '{embed.TargetName}'.");
                        continue;
                    }

                    if (embed.Parameters is null || embed.Parameters.Count == 0)
                    {
                        // A target-name reference lets every host reuse the already-built manifest.
                        if (targetManifest.Error is not null)
                            FailHtmlVisual(manifest, owner,
                                $"RPT3010: Embedded visual '{embed.TargetName}' failed to build: {targetManifest.Error}");
                        continue;
                    }

                    queryCount++;
                    if (queryCount > ConstrainedHtmlPolicy.MaxEmbeddedVisualQueries)
                    {
                        FailHtmlVisual(manifest, owner,
                            $"RPT3029: HTML embedded visual query budget exceeded: {queryCount} > {ConstrainedHtmlPolicy.MaxEmbeddedVisualQueries}.");
                        continue;
                    }

                    var parameterBackups = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    string? disclosureError = null;
                    foreach (var parameter in embed.Parameters)
                    {
                        var name = parameter.Key.StartsWith('@') ? parameter.Key : '@' + parameter.Key;
                        var metadata = _ctx.VarContext.VariableMetadata.FirstOrDefault(pair =>
                            pair.Key.TrimStart('@').Equals(name.TrimStart('@'), StringComparison.OrdinalIgnoreCase)).Value;
                        if (metadata is { IsSecret: true } or { IsSensitive: true })
                        {
                            disclosureError = $"RPT3014: HTML visual embedding cannot bind sensitive parameter '{name}'.";
                            break;
                        }
                        if (!_ctx.VarContext.ContainsVariable(name))
                        {
                            disclosureError = $"RPT3002: HTML visual embedding references undeclared parameter '{name}'.";
                            break;
                        }
                        parameterBackups[name] = _ctx.VarContext.GetVariable(name);
                    }
                    if (disclosureError is not null)
                    {
                        FailHtmlVisual(manifest, owner, disclosureError);
                        continue;
                    }

                    VisualManifest resolved;
                    try
                    {
                        foreach (var parameter in embed.Parameters)
                        {
                            var name = parameter.Key.StartsWith('@') ? parameter.Key : '@' + parameter.Key;
                            _ctx.VarContext.SetVariable(name, parameter.Value);
                        }
                        resolved = await _visualBuilder.BuildAsync(embed.TargetName, targetDefinition);
                    }
                    finally
                    {
                        foreach (var parameter in parameterBackups)
                            _ctx.VarContext.SetVariable(parameter.Key, parameter.Value);
                    }
                    embed.Visual = resolved;
                    if (resolved.Error is not null)
                    {
                        FailHtmlVisual(manifest, owner,
                            $"RPT3010: Embedded visual '{embed.TargetName}' failed to build: {resolved.Error}");
                        continue;
                    }
                    await ResolveEmbedsAsync(resolved, depth + 1);
                }

                if (owner.Error is null)
                {
                    var summaries = owner.HtmlEmbeds.Take(20).Select(embed =>
                    {
                        var target = embed.Visual;
                        if (target is null) manifests.TryGetValue(embed.TargetName, out target);
                        var summary = target?.SemanticFallback?.Summary ?? target?.HtmlFallback ?? target?.Name;
                        return string.IsNullOrWhiteSpace(summary) ? null : $"{embed.TargetName}: {summary}";
                    }).Where(summary => summary is not null).Cast<string>().ToList();
                    if (owner.HtmlEmbeds.Count > summaries.Count)
                        summaries.Add($"... and {owner.HtmlEmbeds.Count - summaries.Count} more embedded visuals");
                    if (summaries.Count > 0)
                    {
                        owner.HtmlFallback = string.Join("\n", new[] { owner.HtmlFallback }
                            .Where(summary => !string.IsNullOrWhiteSpace(summary))
                            .Concat(summaries));
                        owner.SemanticFallback = VisualSemanticFallbackBuilder.Build(owner);
                    }
                }
            }
        }

        private static string? ValidateHtmlEmbedGraph(
            string root,
            string current,
            int depth,
            HashSet<string> path,
            IReadOnlyDictionary<string, IReadOnlyList<string>> graph,
            IReadOnlyDictionary<string, CreateVisualStatement> definitions)
        {
            if (!path.Add(current))
                return $"RPT3010: HTML visual embed cycle detected from '{root}' through '{current}'.";
            if (!graph.TryGetValue(current, out var targets))
            {
                path.Remove(current);
                return null;
            }

            foreach (var target in targets)
            {
                if (!definitions.ContainsKey(target))
                    return $"RPT3010: HTML visual '{root}' embeds missing visual '{target}'.";
                if (path.Contains(target))
                    return $"RPT3010: HTML visual embed cycle detected from '{root}' through '{target}'.";
                var nextDepth = depth + 1;
                if (nextDepth > ConstrainedHtmlPolicy.MaxEmbedDepth)
                    return $"RPT3011: HTML visual '{root}' embed depth exceeds {ConstrainedHtmlPolicy.MaxEmbedDepth}.";
                var nested = ValidateHtmlEmbedGraph(root, target, nextDepth, path, graph, definitions);
                if (nested is not null) return nested;
            }
            path.Remove(current);
            return null;
        }

        private static void EnforceHtmlAggregateBudgets(ReportManifest manifest)
        {
            var nodes = 0;
            var bytes = 0;
            var work = 0;
            foreach (var visual in EnumerateRenderedVisuals(manifest))
            {
                if (visual.HtmlCost is null) continue;
                nodes = checked(nodes + visual.HtmlCost.OutputNodes);
                bytes = checked(bytes + visual.HtmlCost.OutputBytes);
                work = checked(work + visual.HtmlCost.RenderWork);
            }

            string? error = null;
            if (nodes > ConstrainedHtmlPolicy.MaxReportOutputNodes)
                error = $"RPT3027: HTML report output node budget exceeded: {nodes} > {ConstrainedHtmlPolicy.MaxReportOutputNodes}.";
            else if (bytes > ConstrainedHtmlPolicy.MaxReportOutputBytes)
                error = $"RPT3028: HTML report output byte budget exceeded: {bytes} > {ConstrainedHtmlPolicy.MaxReportOutputBytes}.";
            else if (work > ConstrainedHtmlPolicy.MaxReportRenderWork)
                error = $"RPT3029: HTML report render-work budget exceeded: {work} > {ConstrainedHtmlPolicy.MaxReportRenderWork}.";

            if (error is null) return;
            foreach (var visual in manifest.Visuals.Where(visual => visual.VisualType.Equals("HTML", StringComparison.OrdinalIgnoreCase)))
                FailHtmlVisual(manifest, visual, error);
        }

        private static IEnumerable<VisualManifest> EnumerateRenderedVisuals(ReportManifest manifest)
        {
            var byName = manifest.Visuals.ToDictionary(visual => visual.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var visual in manifest.Visuals)
            {
                yield return visual;
                foreach (var embedded in EnumerateEmbeds(visual, byName, 0))
                    yield return embedded;
            }
        }

        private static IEnumerable<VisualManifest> EnumerateEmbeds(
            VisualManifest owner,
            IReadOnlyDictionary<string, VisualManifest> byName,
            int depth)
        {
            if (depth >= ConstrainedHtmlPolicy.MaxEmbedDepth || owner.HtmlEmbeds is null) yield break;
            foreach (var embed in owner.HtmlEmbeds)
            {
                var target = embed.Visual;
                if (target is null && !byName.TryGetValue(embed.TargetName, out target)) continue;
                yield return target;
                foreach (var nested in EnumerateEmbeds(target, byName, depth + 1))
                    yield return nested;
            }
        }

        private static void FailHtmlVisual(ReportManifest manifest, VisualManifest visual, string error)
        {
            visual.Error = error;
            visual.HtmlContent = null;
            visual.HtmlCss = null;
            visual.HtmlEmbeds = null;
            manifest.Error = "One or more HTML visuals failed to build.";
            manifest.Messages ??= [];
            if (!manifest.Messages.Any(message => message.Message.Equals(error, StringComparison.Ordinal)))
                manifest.Messages.Add(new LogEntryManifest(error, "Red", DateTime.UtcNow));
        }

        /// <summary>Builds a detached visual so an interaction transaction can commit it atomically.</summary>
        public Task<VisualManifest> BuildVisualSnapshotAsync(CreateVisualStatement statement) =>
            _visualBuilder.BuildAsync(statement.Name, statement);

        /// <summary>Resolves HTML embeds and aggregate budgets on a detached manifest before commit.</summary>
        public async Task PrepareHtmlVisualsAsync(ReportManifest manifest)
        {
            await ResolveHtmlVisualEmbedsAsync(manifest);
            EnforceHtmlAggregateBudgets(manifest);
        }

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

        internal static VisualActionManifest TranslateAction(VisualAction action)
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
