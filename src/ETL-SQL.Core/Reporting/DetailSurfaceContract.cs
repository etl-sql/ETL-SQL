using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Reporting;

// ════════════════════════════════════════════════════════════════════════════
// Detail-surface contract — the single source of truth for how a TOOLTIP clause
// resolves, what it is allowed to contain, and which limits it must respect.
//
// Every consumer (manifest building, Analysis/lint, LSP, Report Builder, the
// browser runtime, and the static renderers) reads its budgets from
// DetailSurfaceLimits so a limit is enforced once and reported identically
// everywhere. Resolution is pure over AST dictionaries so it can be exercised
// without an execution context.
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Deterministic limits for detail surfaces. These are hard boundaries, not tuning
/// knobs: a report that exceeds one fails closed with an actionable diagnostic rather
/// than rendering a truncated or partially-refreshed surface.
/// </summary>
public static class DetailSurfaceLimits
{
    /// <summary>
    /// Maximum container nesting depth inside one detail surface. Depth 1 is the
    /// referenced container itself; depth 2 allows one level of nested container.
    /// </summary>
    public const int MaxNestingDepth = 3;

    /// <summary>Maximum number of visuals a single detail surface may render.</summary>
    public const int MaxVisuals = 8;

    /// <summary>
    /// Maximum number of graph nodes (containers plus visuals) expanded for one detail
    /// surface. Guards fan-out that stays within the depth limit but explodes in breadth.
    /// </summary>
    public const int MaxNodes = 32;

    /// <summary>
    /// Maximum number of source queries a single detail surface may re-evaluate on
    /// refresh. Each visual carrying its own SOURCE counts once.
    /// </summary>
    public const int MaxRefreshQueries = 8;

    /// <summary>Maximum serialized byte size of one detail surface's manifest projection.</summary>
    public const int MaxManifestBytes = 262_144;

    /// <summary>
    /// Maximum number of distinct detail surfaces one report may declare. Bounds the
    /// aggregate detail cost of a report independently of any single surface.
    /// </summary>
    public const int MaxSurfacesPerReport = 32;

    /// <summary>Maximum characters in a transient text tooltip.</summary>
    public const int MaxTransientTextLength = 1_024;
}

/// <summary>Severity of a <see cref="DetailSurfaceDiagnostic"/>.</summary>
public enum DetailSurfaceSeverity
{
    /// <summary>The report is rejected; the surface must not be emitted.</summary>
    Error,

    /// <summary>The surface is emitted but the author should act.</summary>
    Warning
}

/// <summary>
/// One actionable finding produced while resolving a detail surface. <see cref="Code"/>
/// is stable and may be asserted by tests and surfaced by the LSP.
/// </summary>
public sealed record DetailSurfaceDiagnostic(
    string Code,
    DetailSurfaceSeverity Severity,
    string OwnerObject,
    string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Stable diagnostic codes for the detail-surface contract.</summary>
public static class DetailSurfaceDiagnostics
{
    public const string MissingContainer = "RPT2101";
    public const string MissingVisual = "RPT2102";
    public const string Cycle = "RPT2103";
    public const string NestedDetailSurface = "RPT2104";
    public const string DepthExceeded = "RPT2105";
    public const string VisualCountExceeded = "RPT2106";
    public const string NodeCountExceeded = "RPT2107";
    public const string RefreshQueryCountExceeded = "RPT2108";
    public const string ManifestBytesExceeded = "RPT2109";
    public const string SurfaceCountExceeded = "RPT2110";
    public const string SecretDisclosure = "RPT2111";
    public const string TransientTextTooLong = "RPT2112";
    public const string EmptyInlineSurface = "RPT2113";
}

/// <summary>
/// The statically resolved shape of one detail surface: which visuals it renders, how
/// deep it nests, and what it costs. Produced by <see cref="DetailSurfaceResolver"/>.
/// </summary>
public sealed record ResolvedDetailSurface
{
    /// <summary>The object that declared the TOOLTIP clause (visual, container, or button).</summary>
    public required string OwnerObject { get; init; }

    /// <summary>Transient text tooltip, or persistent detail popover.</summary>
    public required DetailSurfaceKind Kind { get; init; }

    /// <summary>Container names reached, outermost first. Empty for inline and text forms.</summary>
    public IReadOnlyList<string> Containers { get; init; } = Array.Empty<string>();

    /// <summary>Visual names rendered by this surface, in resolution order.</summary>
    public IReadOnlyList<string> Visuals { get; init; } = Array.Empty<string>();

    /// <summary>Deepest container nesting level reached (0 for text and inline forms).</summary>
    public int Depth { get; init; }

    /// <summary>Total graph nodes expanded (containers plus visuals).</summary>
    public int NodeCount => Containers.Count + Visuals.Count;

    /// <summary>
    /// Number of visual sources re-evaluated when this surface opens. An inline SELECT
    /// re-runs; a <c>#temp</c> or <c>&amp;dataset</c> reference is re-read. Both count.
    /// </summary>
    public int RefreshQueryCount { get; init; }

    /// <summary>True when resolution produced no <see cref="DetailSurfaceSeverity.Error"/>.</summary>
    public bool IsValid { get; init; } = true;
}

/// <summary>
/// Resolves TOOLTIP clauses to their concrete visual/container graph and enforces the
/// detail-surface contract statically. Pure and allocation-light: callers pass the report's
/// object dictionaries and receive the resolved shape plus any diagnostics.
/// </summary>
public static class DetailSurfaceResolver
{
    /// <summary>
    /// Resolves every detail surface declared by the supplied report objects and returns
    /// the resolved surfaces together with all diagnostics, including the aggregate
    /// per-report surface-count budget.
    /// </summary>
    public static (IReadOnlyList<ResolvedDetailSurface> Surfaces, IReadOnlyList<DetailSurfaceDiagnostic> Diagnostics) ResolveReport(
        IReadOnlyDictionary<string, CreateVisualStatement> visuals,
        IReadOnlyDictionary<string, CreateContainerStatement> containers)
    {
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(containers);

        var surfaces = new List<ResolvedDetailSurface>();
        var diagnostics = new List<DetailSurfaceDiagnostic>();

        foreach (var (name, visual) in Ordered(visuals))
            AddSurface(name, visual.Tooltip, visuals, containers, surfaces, diagnostics);

        foreach (var (name, container) in Ordered(containers))
            AddSurface(name, container.Tooltip, visuals, containers, surfaces, diagnostics);

        if (surfaces.Count > DetailSurfaceLimits.MaxSurfacesPerReport)
        {
            diagnostics.Add(new DetailSurfaceDiagnostic(
                DetailSurfaceDiagnostics.SurfaceCountExceeded,
                DetailSurfaceSeverity.Error,
                "<report>",
                $"This report declares {surfaces.Count} detail surfaces, exceeding the limit of " +
                $"{DetailSurfaceLimits.MaxSurfacesPerReport}. Remove TOOLTIP clauses or share one " +
                "referenced container between visuals."));
        }

        return (surfaces, diagnostics);
    }

    /// <summary>
    /// Resolves a single TOOLTIP clause declared by <paramref name="ownerObject"/>.
    /// Returns the resolved surface; <see cref="ResolvedDetailSurface.IsValid"/> is false
    /// when any error diagnostic was produced.
    /// </summary>
    public static ResolvedDetailSurface Resolve(
        string ownerObject,
        TooltipDefinition tooltip,
        IReadOnlyDictionary<string, CreateVisualStatement> visuals,
        IReadOnlyDictionary<string, CreateContainerStatement> containers,
        ICollection<DetailSurfaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(diagnostics);

        int errorsBefore = diagnostics.Count(d => d.Severity == DetailSurfaceSeverity.Error);

        var resolvedContainers = new List<string>();
        var resolvedVisuals = new List<string>();
        int maxDepth = 0;

        // Branch on the authored form, not on Kind: an inline block carrying only markdown
        // projects to a transient tooltip but must still be validated as an inline surface.
        if (tooltip.ContainerRef is { } containerRef)
        {
            // The referenced container is depth 1; walking its slots increases depth.
            Walk(ownerObject, containerRef, 1, visuals, containers,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                resolvedContainers, resolvedVisuals, ref maxDepth, diagnostics);
        }
        else if (tooltip.IsInline)
        {
            // Inline form: the listed visuals are rendered directly, with no container level.
            var inline = tooltip.InlineVisuals ?? new List<string>();
            if (inline.Count == 0 && string.IsNullOrWhiteSpace(tooltip.InlineMarkdown))
            {
                diagnostics.Add(new DetailSurfaceDiagnostic(
                    DetailSurfaceDiagnostics.EmptyInlineSurface,
                    DetailSurfaceSeverity.Error,
                    ownerObject,
                    $"'{ownerObject}' declares an inline TOOLTIP with neither markdown text nor a " +
                    "VISUALS list. Supply markdown, one or more visuals, or remove the clause."));
            }

            foreach (var visualName in inline)
                ResolveVisual(ownerObject, visualName, visuals, resolvedVisuals, diagnostics);
        }
        else
        {
            ValidateTransientText(ownerObject, tooltip, diagnostics);
        }

        EnforceBudgets(ownerObject, resolvedContainers, resolvedVisuals, visuals, diagnostics,
            out int refreshQueries);

        int errorsAfter = diagnostics.Count(d => d.Severity == DetailSurfaceSeverity.Error);

        return new ResolvedDetailSurface
        {
            OwnerObject = ownerObject,
            Kind = tooltip.Kind,
            Containers = resolvedContainers,
            Visuals = resolvedVisuals,
            Depth = maxDepth,
            RefreshQueryCount = refreshQueries,
            IsValid = errorsAfter == errorsBefore
        };
    }

    private static void AddSurface(
        string ownerObject,
        TooltipDefinition? tooltip,
        IReadOnlyDictionary<string, CreateVisualStatement> visuals,
        IReadOnlyDictionary<string, CreateContainerStatement> containers,
        List<ResolvedDetailSurface> surfaces,
        List<DetailSurfaceDiagnostic> diagnostics)
    {
        if (tooltip == null) return;
        surfaces.Add(Resolve(ownerObject, tooltip, visuals, containers, diagnostics));
    }

    private static void ValidateTransientText(
        string ownerObject,
        TooltipDefinition tooltip,
        ICollection<DetailSurfaceDiagnostic> diagnostics)
    {
        // Only a literal is measurable statically; expression-valued tooltips are bounded at
        // manifest build time against the same limit.
        if (tooltip.PlainText is not LiteralExpression { Value: string text }) return;
        if (text.Length <= DetailSurfaceLimits.MaxTransientTextLength) return;

        diagnostics.Add(new DetailSurfaceDiagnostic(
            DetailSurfaceDiagnostics.TransientTextTooLong,
            DetailSurfaceSeverity.Error,
            ownerObject,
            $"The transient tooltip on '{ownerObject}' is {text.Length} characters, exceeding the " +
            $"limit of {DetailSurfaceLimits.MaxTransientTextLength}. Shorten the text, or use a " +
            "referenced container to present long-form detail as a focusable popover."));
    }

    private static void Walk(
        string ownerObject,
        string containerName,
        int depth,
        IReadOnlyDictionary<string, CreateVisualStatement> visuals,
        IReadOnlyDictionary<string, CreateContainerStatement> containers,
        HashSet<string> path,
        List<string> resolvedContainers,
        List<string> resolvedVisuals,
        ref int maxDepth,
        ICollection<DetailSurfaceDiagnostic> diagnostics)
    {
        if (!path.Add(containerName))
        {
            diagnostics.Add(new DetailSurfaceDiagnostic(
                DetailSurfaceDiagnostics.Cycle,
                DetailSurfaceSeverity.Error,
                ownerObject,
                $"The detail surface on '{ownerObject}' is cyclic: container '{containerName}' is " +
                $"reached from itself via {string.Join(" -> ", path)}. Break the cycle by removing " +
                "the self-reference from the container's MAP."));
            return;
        }

        try
        {
            if (!containers.TryGetValue(containerName, out var container))
            {
                diagnostics.Add(new DetailSurfaceDiagnostic(
                    DetailSurfaceDiagnostics.MissingContainer,
                    DetailSurfaceSeverity.Error,
                    ownerObject,
                    $"'{ownerObject}' references container '{containerName}' for its detail surface, " +
                    "but no CREATE CONTAINER with that name exists. Create it, or correct the name."));
                return;
            }

            resolvedContainers.Add(containerName);
            maxDepth = Math.Max(maxDepth, depth);

            // A detail surface must not open another detail surface.
            if (container.Tooltip != null)
            {
                diagnostics.Add(new DetailSurfaceDiagnostic(
                    DetailSurfaceDiagnostics.NestedDetailSurface,
                    DetailSurfaceSeverity.Error,
                    ownerObject,
                    $"Container '{containerName}' is used as the detail surface for '{ownerObject}' " +
                    "but declares its own TOOLTIP. A detail surface cannot open another detail " +
                    "surface; remove the nested TOOLTIP clause."));
            }

            bool atDepthLimit = depth >= DetailSurfaceLimits.MaxNestingDepth;
            if (atDepthLimit && container.SlotMap.Values.Any(containers.ContainsKey))
            {
                diagnostics.Add(new DetailSurfaceDiagnostic(
                    DetailSurfaceDiagnostics.DepthExceeded,
                    DetailSurfaceSeverity.Error,
                    ownerObject,
                    $"The detail surface on '{ownerObject}' nests containers more than " +
                    $"{DetailSurfaceLimits.MaxNestingDepth} levels deep at '{containerName}'. " +
                    "Flatten the container graph."));
            }

            foreach (var child in container.SlotMap.Values)
            {
                if (containers.ContainsKey(child))
                {
                    if (!atDepthLimit)
                    {
                        Walk(ownerObject, child, depth + 1, visuals, containers, path,
                            resolvedContainers, resolvedVisuals, ref maxDepth, diagnostics);
                    }
                }
                else
                {
                    ResolveVisual(ownerObject, child, visuals, resolvedVisuals, diagnostics);
                }
            }
        }
        finally
        {
            path.Remove(containerName);
        }
    }

    private static void ResolveVisual(
        string ownerObject,
        string visualName,
        IReadOnlyDictionary<string, CreateVisualStatement> visuals,
        List<string> resolvedVisuals,
        ICollection<DetailSurfaceDiagnostic> diagnostics)
    {
        if (!visuals.TryGetValue(visualName, out var visual))
        {
            diagnostics.Add(new DetailSurfaceDiagnostic(
                DetailSurfaceDiagnostics.MissingVisual,
                DetailSurfaceSeverity.Error,
                ownerObject,
                $"The detail surface on '{ownerObject}' renders visual '{visualName}', but no " +
                "CREATE VISUAL with that name exists. Create it, or correct the name."));
            return;
        }

        // A visual inside a detail surface must not itself open one.
        if (visual.Tooltip != null)
        {
            diagnostics.Add(new DetailSurfaceDiagnostic(
                DetailSurfaceDiagnostics.NestedDetailSurface,
                DetailSurfaceSeverity.Error,
                ownerObject,
                $"Visual '{visualName}' is rendered inside the detail surface for '{ownerObject}' " +
                "but declares its own TOOLTIP. A detail surface cannot open another detail " +
                "surface; remove the nested TOOLTIP clause."));
        }

        if (!resolvedVisuals.Contains(visualName, StringComparer.OrdinalIgnoreCase))
            resolvedVisuals.Add(visualName);
    }

    private static void EnforceBudgets(
        string ownerObject,
        List<string> resolvedContainers,
        List<string> resolvedVisuals,
        IReadOnlyDictionary<string, CreateVisualStatement> visuals,
        ICollection<DetailSurfaceDiagnostic> diagnostics,
        out int refreshQueries)
    {
        // Every visual in a detail surface is re-evaluated when the surface opens: an inline
        // SELECT re-runs, a #temp/&dataset reference is re-read. Both are refresh work.
        refreshQueries = resolvedVisuals.Count(visuals.ContainsKey);

        if (resolvedVisuals.Count > DetailSurfaceLimits.MaxVisuals)
        {
            diagnostics.Add(new DetailSurfaceDiagnostic(
                DetailSurfaceDiagnostics.VisualCountExceeded,
                DetailSurfaceSeverity.Error,
                ownerObject,
                $"The detail surface on '{ownerObject}' renders {resolvedVisuals.Count} visuals, " +
                $"exceeding the limit of {DetailSurfaceLimits.MaxVisuals}. Show fewer visuals in " +
                "the popover and link to a full page for the rest."));
        }

        int nodeCount = resolvedContainers.Count + resolvedVisuals.Count;
        if (nodeCount > DetailSurfaceLimits.MaxNodes)
        {
            diagnostics.Add(new DetailSurfaceDiagnostic(
                DetailSurfaceDiagnostics.NodeCountExceeded,
                DetailSurfaceSeverity.Error,
                ownerObject,
                $"The detail surface on '{ownerObject}' expands to {nodeCount} containers and " +
                $"visuals, exceeding the limit of {DetailSurfaceLimits.MaxNodes}. Simplify the " +
                "container graph."));
        }

        if (refreshQueries > DetailSurfaceLimits.MaxRefreshQueries)
        {
            diagnostics.Add(new DetailSurfaceDiagnostic(
                DetailSurfaceDiagnostics.RefreshQueryCountExceeded,
                DetailSurfaceSeverity.Error,
                ownerObject,
                $"Opening the detail surface on '{ownerObject}' would re-evaluate {refreshQueries} " +
                $"source queries, exceeding the limit of {DetailSurfaceLimits.MaxRefreshQueries}. " +
                "Stage the detail into a single #temp table and render fewer sourced visuals."));
        }
    }

    private static IEnumerable<KeyValuePair<string, T>> Ordered<T>(IReadOnlyDictionary<string, T> source)
        => source.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);
}
