using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Reporting.Renderers;

/// <summary>
/// Deterministic, dependency-free SVG layouts for the focused visual catalog.
///
/// The geometry is deliberately its own — squarified tiles, radial arcs, ranked flows — but every
/// presentation decision comes from <see cref="FocusedLayoutInputs"/>: the same theme tokens, the
/// same series-colour rule, the same resolved interaction key, and an explicit authored canvas.
/// Nothing here invents a palette or a colour of its own.
/// </summary>
internal sealed class SpecializedNativeSvgRenderer : RendererBase
{
    /// <summary>Vertical space the title band reserves above the plot area.</summary>
    private const double TitleBand = 38;

    public string? Render(VisualManifest visual, PlotBounds? bounds = null) => visual.VisualType.ToUpperInvariant() switch
    {
        "TREEMAP" => Treemap(visual, FocusedLayoutInputs.From(visual, TreemapKeys(visual), bounds)),
        "SUNBURST" => Sunburst(visual, bounds),
        "SANKEY" => Sankey(visual, bounds),
        "NETWORK" => Network(visual, bounds),
        "MAP" => Map(visual, FocusedLayoutInputs.From(visual, bounds: bounds)),
        "MATRIX" => Matrix(visual, FocusedLayoutInputs.From(visual, bounds: bounds)),
        _ => null
    };

    // ── TREEMAP ────────────────────────────────────────────────────────────────

    private static IEnumerable<string> TreemapKeys(VisualManifest v)
    {
        var label = Index(v, "name", "label", fallback: 0);
        return v.Rows.Select((row, i) => Cell(row, label, $"Item {i + 1}"));
    }

    private static string Treemap(VisualManifest v, FocusedLayoutInputs inputs)
    {
        var label = Index(v, "name", "label", fallback: 0);
        var parent = Index(v, "parent", fallback: -1);
        var value = Index(v, "value", fallback: Math.Min(1, v.Columns.Count - 1));
        var nodes = v.Rows.Select((row, i) => new Node(Cell(row, label, $"Item {i + 1}"), Cell(row, parent, ""), Math.Max(0, Number(row, value)), i)).ToList();
        var roots = nodes.Where(n => string.IsNullOrWhiteSpace(n.Parent) || nodes.All(p => !p.Id.Equals(n.Parent, StringComparison.OrdinalIgnoreCase))).ToList();
        if (roots.Count == 0) roots = nodes;
        var sb = Start(v, inputs);
        Squarify(sb, inputs, roots, nodes, 8, TitleBand, inputs.Width - 16, inputs.Height - TitleBand - 8, 0);
        return End(sb);
    }

    private static void Squarify(StringBuilder sb, FocusedLayoutInputs inputs, IReadOnlyList<Node> siblings,
        IReadOnlyList<Node> all, double x, double y, double w, double h, int depth)
    {
        if (siblings.Count == 0 || w <= 1 || h <= 1) return;
        var weighted = siblings
            .Select(node => (Node: node, Weight: Math.Max(node.Value, DescendantValue(node, all))))
            .OrderByDescending(item => item.Weight)
            .ThenBy(item => item.Node.Row)
            .ToList();
        var total = weighted.Sum(item => item.Weight);
        if (total <= 0d)
        {
            weighted = weighted.Select(item => (item.Node, Weight: 1d)).ToList();
            total = weighted.Count;
        }
        var remaining = weighted.Select(item => new TreemapItem(item.Node, item.Weight / total * w * h)).ToList();
        var layouts = new List<TreemapLayout>();
        var bounds = new TreemapBounds(x, y, w, h);
        while (remaining.Count > 0)
        {
            var row = new List<TreemapItem> { remaining[0] };
            remaining.RemoveAt(0);
            while (remaining.Count > 0 && WorstAspect(row.Append(remaining[0]), Math.Min(bounds.Width, bounds.Height)) <=
                WorstAspect(row, Math.Min(bounds.Width, bounds.Height)))
            {
                row.Add(remaining[0]);
                remaining.RemoveAt(0);
            }
            bounds = LayoutTreemapRow(row, bounds, layouts);
        }

        foreach (var layout in layouts)
        {
            var node = layout.Item.Node;
            var color = inputs.Color(node.Id, node.Row + depth);
            sb.Append($"<g data-row-index='{node.Row}'><rect class='treemap-tile' x='{F(layout.Bounds.X)}' y='{F(layout.Bounds.Y)}' width='{F(Math.Max(0, layout.Bounds.Width))}' height='{F(Math.Max(0, layout.Bounds.Height))}' fill='{color}' fill-opacity='{F(Math.Max(.55, .9 - depth * .1))}' stroke='{inputs.Divider}' stroke-width='2'/><title>{Xml(node.Id)}: {F(node.Value)}</title>");
            if (layout.Bounds.Width > 42 && layout.Bounds.Height > 20)
                sb.Append($"<text x='{F(layout.Bounds.X + 6)}' y='{F(layout.Bounds.Y + 16)}' font-size='11' font-weight='{(depth == 0 ? "600" : "400")}' fill='{inputs.OnAccent}'>{Xml(Trim(node.Id, (int)(layout.Bounds.Width / 7)))}</text>");
            sb.Append("</g>");
            var children = all.Where(candidate => candidate.Parent.Equals(node.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (children.Count > 0)
                Squarify(sb, inputs, children, all, layout.Bounds.X + 4, layout.Bounds.Y + 21,
                    Math.Max(0, layout.Bounds.Width - 8), Math.Max(0, layout.Bounds.Height - 25), depth + 1);
        }
    }

    private static double WorstAspect(IEnumerable<TreemapItem> source, double shortSide)
    {
        var row = source.ToList();
        if (row.Count == 0 || shortSide <= 0d) return double.PositiveInfinity;
        var sum = row.Sum(item => item.Area);
        var maximum = row.Max(item => item.Area);
        var minimum = row.Min(item => item.Area);
        if (sum <= 0d || minimum <= 0d) return double.PositiveInfinity;
        var sideSquared = shortSide * shortSide;
        return Math.Max(sideSquared * maximum / (sum * sum), sum * sum / (sideSquared * minimum));
    }

    private static TreemapBounds LayoutTreemapRow(IReadOnlyList<TreemapItem> row, TreemapBounds bounds,
        ICollection<TreemapLayout> output)
    {
        var area = row.Sum(item => item.Area);
        if (bounds.Width >= bounds.Height)
        {
            var columnWidth = bounds.Height <= 0d ? 0d : area / bounds.Height;
            var cursor = bounds.Y;
            for (var index = 0; index < row.Count; index++)
            {
                var height = columnWidth <= 0d ? 0d : row[index].Area / columnWidth;
                if (index == row.Count - 1) height = bounds.Y + bounds.Height - cursor;
                output.Add(new TreemapLayout(row[index], new TreemapBounds(bounds.X, cursor, columnWidth, height)));
                cursor += height;
            }
            return new TreemapBounds(bounds.X + columnWidth, bounds.Y, Math.Max(0d, bounds.Width - columnWidth), bounds.Height);
        }

        var rowHeight = bounds.Width <= 0d ? 0d : area / bounds.Width;
        var x = bounds.X;
        for (var index = 0; index < row.Count; index++)
        {
            var width = rowHeight <= 0d ? 0d : row[index].Area / rowHeight;
            if (index == row.Count - 1) width = bounds.X + bounds.Width - x;
            output.Add(new TreemapLayout(row[index], new TreemapBounds(x, bounds.Y, width, rowHeight)));
            x += width;
        }
        return new TreemapBounds(bounds.X, bounds.Y + rowHeight, bounds.Width, Math.Max(0d, bounds.Height - rowHeight));
    }

    private static double DescendantValue(Node node, IReadOnlyList<Node> all)
    {
        var children = all.Where(n => n.Parent.Equals(node.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        return children.Count == 0 ? node.Value : Math.Max(node.Value, children.Sum(child => DescendantValue(child, all)));
    }

    // ── SUNBURST ───────────────────────────────────────────────────────────────

    private static string Sunburst(VisualManifest v, PlotBounds? bounds)
    {
        var explicitLabel = Index(v, "label", "name", fallback: -1);
        var parent = Index(v, "parent", fallback: -1);
        var value = Index(v, "value", fallback: v.Columns.Count - 1);
        var paths = new List<(string[] Parts, double Value, int Row)>();
        if (explicitLabel >= 0 && parent >= 0)
        {
            var raw = v.Rows.Select((row, i) => new Node(Cell(row, explicitLabel, $"Item {i + 1}"), Cell(row, parent, ""), Math.Max(0, Number(row, value)), i)).ToList();
            foreach (var node in raw) paths.Add((Path(node, raw), node.Value, node.Row));
        }
        else
        {
            var levels = Enumerable.Range(1, 8).Select(i => Index(v, $"level{i}", fallback: -1)).Where(i => i >= 0).ToArray();
            if (levels.Length == 0) levels = Enumerable.Range(0, Math.Max(1, v.Columns.Count - 1)).ToArray();
            paths.AddRange(v.Rows.Select((row, i) => (levels.Select(level => Cell(row, level, "")).Where(s => s.Length > 0).ToArray(), Math.Max(0, Number(row, value)), i)));
        }

        // A sunburst's series are its root segments: every ring under one root shares that root's
        // colour, so the wedge and a BAR of the same category read the same.
        var roots = paths.Select(path => path.Parts.FirstOrDefault() ?? string.Empty)
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var inputs = FocusedLayoutInputs.From(v, roots, bounds);

        var maxDepth = Math.Max(1, paths.Select(p => p.Parts.Length).DefaultIfEmpty(1).Max());
        var total = paths.Sum(p => p.Value); if (total <= 0) total = Math.Max(1, paths.Count);
        var cx = inputs.Width / 2;
        var cy = (TitleBand + inputs.Height) / 2;
        var radius = Math.Min(inputs.Width, inputs.Height - TitleBand) * .42;
        var sb = Start(v, inputs); var cursor = -Math.PI / 2;
        foreach (var path in paths)
        {
            var root = path.Parts.FirstOrDefault() ?? string.Empty;
            var rootIndex = roots.FindIndex(item => item.Equals(root, StringComparison.OrdinalIgnoreCase));
            var color = inputs.Color(root, rootIndex >= 0 ? rootIndex : path.Row);
            var sweep = 2 * Math.PI * (path.Value <= 0 ? 1 : path.Value) / total;
            for (var depth = 0; depth < path.Parts.Length; depth++)
            {
                var inner = radius * depth / maxDepth;
                var outer = radius * (depth + 1) / maxDepth;
                sb.Append($"<path data-row-index='{path.Row}' d='{Arc(cx, cy, inner, outer, cursor, cursor + sweep)}' fill='{color}' fill-opacity='{F(Math.Max(.5, 1 - depth * .18))}' stroke='{inputs.Divider}'><title>{Xml(string.Join(" / ", path.Parts.Take(depth + 1)))}: {F(path.Value)}</title></path>");
            }
            cursor += sweep;
        }
        return End(sb);
    }

    // ── SANKEY ─────────────────────────────────────────────────────────────────

    private static string Sankey(VisualManifest v, PlotBounds? bounds)
    {
        var source = Index(v, "source", "from", fallback: 0);
        var target = Index(v, "target", "to", fallback: 1);
        var value = Index(v, "value", fallback: 2);
        var nodeColorIndex = Index(v, "node_color", "source_color", fallback: -1);
        if (nodeColorIndex < 0) nodeColorIndex = Index(v, "from_color", fallback: -1);
        var targetColorIndex = Index(v, "target_color", "to_color", fallback: -1);

        var links = v.Rows.Select((r, i) => (
            Source: Cell(r, source, ""),
            Target: Cell(r, target, ""),
            Value: Math.Max(.1, Number(r, value)),
            Row: i
        )).Where(e => e.Source.Length > 0 && e.Target.Length > 0).ToList();

        var names = links.SelectMany(e => new[] { e.Source, e.Target }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var inputs = FocusedLayoutInputs.From(v, names, bounds);

        // Options validation & extraction
        var nodeAlign = (v.Options.GetValueOrDefault("NODE_ALIGN") ?? "JUSTIFY").ToUpperInvariant();
        if (nodeAlign is not ("LEFT" or "RIGHT" or "CENTER" or "JUSTIFY"))
            throw new InvalidOperationException($"Invalid NODE_ALIGN '{v.Options["NODE_ALIGN"]}'. Valid values are LEFT, RIGHT, CENTER, or JUSTIFY.");

        double linkOpacity = 0.55;
        if (v.Options.TryGetValue("LINK_OPACITY", out var loStr) && !string.IsNullOrWhiteSpace(loStr))
        {
            if (!double.TryParse(loStr, NumberStyles.Any, CultureInfo.InvariantCulture, out linkOpacity) || linkOpacity < 0.0 || linkOpacity > 1.0)
                throw new InvalidOperationException($"Invalid LINK_OPACITY '{loStr}'. Valid values are between 0.0 and 1.0.");
        }

        double nodePadding = 12.0;
        if (v.Options.TryGetValue("NODE_PADDING", out var npStr) && !string.IsNullOrWhiteSpace(npStr))
        {
            if (!double.TryParse(npStr, NumberStyles.Any, CultureInfo.InvariantCulture, out nodePadding) || nodePadding < 0.0)
                throw new InvalidOperationException($"Invalid NODE_PADDING '{npStr}'. Value must be a non-negative number.");
        }

        // Custom node colors from NODE_COLOR mapping
        var customNodeColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (nodeColorIndex >= 0 || targetColorIndex >= 0)
        {
            foreach (var r in v.Rows)
            {
                var src = Cell(r, source, "");
                var tgt = Cell(r, target, "");
                if (nodeColorIndex >= 0 && src.Length > 0)
                {
                    var c = Cell(r, nodeColorIndex, "");
                    if (!string.IsNullOrWhiteSpace(c)) customNodeColors[src] = c;
                }
                if (targetColorIndex >= 0 && tgt.Length > 0)
                {
                    var c = Cell(r, targetColorIndex, "");
                    if (!string.IsNullOrWhiteSpace(c)) customNodeColors[tgt] = c;
                }
            }
        }

        // Node depths, heights, and degrees for alignment
        var depth = names.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
        var height = names.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
        var inDegree = names.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outDegree = names.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var edge in links)
        {
            if (!edge.Source.Equals(edge.Target, StringComparison.OrdinalIgnoreCase))
            {
                outDegree[edge.Source]++;
                inDegree[edge.Target]++;
            }
        }

        for (var pass = 0; pass < names.Count; pass++)
        {
            foreach (var edge in links)
            {
                if (!edge.Source.Equals(edge.Target, StringComparison.OrdinalIgnoreCase))
                {
                    depth[edge.Target] = Math.Max(depth[edge.Target], depth[edge.Source] + 1);
                }
            }
        }
        var maxDepth = Math.Max(1, depth.Values.DefaultIfEmpty().Max());

        for (var pass = 0; pass < names.Count; pass++)
        {
            foreach (var edge in links)
            {
                if (!edge.Source.Equals(edge.Target, StringComparison.OrdinalIgnoreCase))
                {
                    height[edge.Source] = Math.Max(height[edge.Source], height[edge.Target] + 1);
                }
            }
        }

        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            ranks[n] = nodeAlign switch
            {
                "LEFT" => depth[n],
                "RIGHT" => maxDepth - height[n],
                "JUSTIFY" => (outDegree[n] == 0 && inDegree[n] > 0) ? maxDepth : depth[n],
                "CENTER" => (int)Math.Round((depth[n] + (maxDepth - height[n])) / 2.0),
                _ => depth[n]
            };
        }
        var maxRank = Math.Max(1, ranks.Values.DefaultIfEmpty().Max());

        // Node positioning with node padding
        const double nodeHeight = 20.0;
        var availableHeight = inputs.Height - TitleBand - 40.0;
        var pos = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in names.GroupBy(n => ranks[n]))
        {
            var list = group.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var colX = 45.0 + group.Key * (inputs.Width - 100.0) / maxRank;

            if (list.Count == 1)
            {
                pos[list[0]] = (colX, TitleBand + 20.0 + availableHeight / 2.0);
            }
            else
            {
                var neededHeight = list.Count * nodeHeight + (list.Count - 1) * nodePadding;
                double effectivePadding = nodePadding;
                double startY;

                if (neededHeight <= availableHeight)
                {
                    startY = TitleBand + 20.0 + (availableHeight - neededHeight) / 2.0 + nodeHeight / 2.0;
                }
                else
                {
                    effectivePadding = Math.Max(2.0, (availableHeight - list.Count * nodeHeight) / Math.Max(1, list.Count - 1));
                    startY = TitleBand + 20.0 + nodeHeight / 2.0;
                }

                for (var i = 0; i < list.Count; i++)
                {
                    pos[list[i]] = (colX, startY + i * (nodeHeight + effectivePadding));
                }
            }
        }

        var maxValue = links.Select(e => e.Value).DefaultIfEmpty(1).Max();
        var sb = Start(v, inputs);

        sb.Append($"<g class='plot-sankey' data-node-align='{nodeAlign.ToLowerInvariant()}' data-link-opacity='{F(linkOpacity)}' data-node-padding='{F(nodePadding)}'>");

        foreach (var edge in links)
        {
            var a = pos[edge.Source];
            var b = pos[edge.Target];
            var mid = (a.X + b.X) / 2;
            sb.Append($"<path data-row-index='{edge.Row}' d='M {F(a.X + 8)} {F(a.Y)} C {F(mid)} {F(a.Y)} {F(mid)} {F(b.Y)} {F(b.X - 8)} {F(b.Y)}' fill='none' stroke='{inputs.Muted}' stroke-opacity='{F(linkOpacity)}' stroke-width='{F(1 + 12 * edge.Value / maxValue)}'><title>{Xml(edge.Source)} → {Xml(edge.Target)}: {F(edge.Value)}</title></path>");
        }

        for (var i = 0; i < names.Count; i++)
        {
            var p = pos[names[i]];
            var nodeName = names[i];
            var nodeColor = customNodeColors.TryGetValue(nodeName, out var c) && !string.IsNullOrWhiteSpace(c)
                ? c
                : inputs.Color(nodeName, i);

            sb.Append($"<rect x='{F(p.X - 8)}' y='{F(p.Y - 10)}' width='16' height='20' rx='2' fill='{nodeColor}' data-node='{Xml(nodeName)}'/><text x='{F(p.X + 12)}' y='{F(p.Y + 4)}' font-size='10' fill='{inputs.OnSurface}'>{Xml(Trim(nodeName, 18))}</text>");
        }

        sb.Append("</g>");
        return End(sb);
    }

    // ── NETWORK ────────────────────────────────────────────────────────────────

    private static string Network(VisualManifest v, PlotBounds? bounds)
    {
        var source = Index(v, "from", "source", fallback: 0); var target = Index(v, "to", "target", fallback: 1); var weight = Index(v, "value", "weight", fallback: -1);
        var links = v.Rows.Select((r, i) => (A: Cell(r, source, ""), B: Cell(r, target, ""), W: Math.Max(.1, Number(r, weight, 1)), Row: i)).Where(e => e.A.Length > 0 && e.B.Length > 0).ToList();
        var names = links.SelectMany(e => new[] { e.A, e.B }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var inputs = FocusedLayoutInputs.From(v, names, bounds);
        var cx = inputs.Width / 2;
        var cy = (TitleBand + inputs.Height) / 2;
        var radius = Math.Min(inputs.Width, inputs.Height - TitleBand) * .38;
        var pos = names.Select((n, i) => (n, p: (X: cx + radius * Math.Cos(2 * Math.PI * i / Math.Max(1, names.Count) - Math.PI / 2), Y: cy + radius * Math.Sin(2 * Math.PI * i / Math.Max(1, names.Count) - Math.PI / 2)))).ToDictionary(x => x.n, x => x.p, StringComparer.OrdinalIgnoreCase);
        var max = links.Select(e => e.W).DefaultIfEmpty(1).Max(); var sb = Start(v, inputs);
        foreach (var edge in links) { var a = pos[edge.A]; var b = pos[edge.B]; sb.Append($"<line data-row-index='{edge.Row}' x1='{F(a.X)}' y1='{F(a.Y)}' x2='{F(b.X)}' y2='{F(b.Y)}' stroke='{inputs.Muted}' stroke-width='{F(1 + 5 * edge.W / max)}'><title>{Xml(edge.A)} ↔ {Xml(edge.B)}: {F(edge.W)}</title></line>"); }
        for (var i = 0; i < names.Count; i++) { var p = pos[names[i]]; sb.Append($"<circle cx='{F(p.X)}' cy='{F(p.Y)}' r='9' fill='{inputs.Color(names[i], i)}'/><text x='{F(p.X)}' y='{F(p.Y + (p.Y < cy ? -14 : 23))}' text-anchor='middle' font-size='10' fill='{inputs.OnSurface}'>{Xml(Trim(names[i], 16))}</text>"); }
        return End(sb);
    }

    // ── MAP ────────────────────────────────────────────────────────────────────

    private static string Map(VisualManifest v, FocusedLayoutInputs inputs)
    {
        var sb = Start(v, inputs); var mode = v.Options.GetValueOrDefault("MODE") ?? "CHOROPLETH";
        if (mode.Equals("POINTS", StringComparison.OrdinalIgnoreCase))
        {
            DrawMapOutline(sb, v, inputs);
            var lon = Index(v, "lon", fallback: 0); var lat = Index(v, "lat", fallback: 1); var val = Index(v, "value", fallback: 2); var label = Index(v, "label", fallback: 3);
            var accent = inputs.Color(null, 0);
            var max = v.Rows.Select(r => Number(r, val)).DefaultIfEmpty(1).Max(); if (max <= 0) max = 1;
            foreach (var (row, i) in v.Rows.Select((r, i) => (r, i))) { var x = ProjectX(inputs, Number(row, lon)); var y = ProjectY(inputs, Number(row, lat)); sb.Append($"<circle data-row-index='{i}' cx='{F(x)}' cy='{F(y)}' r='{F(4 + 12 * Math.Sqrt(Math.Max(0, Number(row, val)) / max))}' fill='{accent}' fill-opacity='.7'><title>{Xml(Cell(row, label, $"Point {i + 1}"))}: {F(Number(row, val))}</title></circle>"); }
            return End(sb);
        }
        var geo = LoadGeoJson(v); if (geo is null) { sb.Append($"<text x='{F(inputs.Width / 2)}' y='{F(inputs.Height / 2)}' text-anchor='middle' fill='{inputs.OnSurface}'>Map geometry unavailable</text>"); return End(sb); }
        var regionIndex = Index(v, "region", fallback: 0); var valueIndex = Index(v, "value", fallback: 1);
        var values = v.Rows.Select((row, index) => (Region: Cell(row, regionIndex, ""), Value: Number(row, valueIndex), Row: index))
            .Where(item => item.Region.Length > 0)
            .GroupBy(item => item.Region, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (Value: group.Sum(item => item.Value), Row: group.First().Row), StringComparer.OrdinalIgnoreCase);
        var maximum = values.Values.Select(item => item.Value).DefaultIfEmpty(1).Max(); if (maximum <= 0) maximum = 1;
        foreach (var feature in geo.RootElement.GetProperty("features").EnumerateArray())
        {
            var props = feature.GetProperty("properties"); var name = Property(props, "name") ?? Property(props, "NAME") ?? Property(props, "admin") ?? ""; var match = values.GetValueOrDefault(name); var amount = match.Value;
            foreach (var polygon in Polygons(feature.GetProperty("geometry"))) sb.Append($"<path{(values.ContainsKey(name) ? $" data-row-index='{match.Row}'" : "")} d='{PolygonPath(inputs, polygon)}' fill='{Ramp(amount / maximum)}' stroke='{inputs.Divider}' stroke-width='.35'><title>{Xml(name)}: {F(amount)}</title></path>");
        }
        geo.Dispose(); return End(sb);
    }

    // ── MATRIX ─────────────────────────────────────────────────────────────────

    private static string Matrix(VisualManifest v, FocusedLayoutInputs inputs)
    {
        var sb = Start(v, inputs); var cols = Math.Max(1, v.Columns.Count); var rows = Math.Max(1, v.Rows.Count);
        var cw = (inputs.Width - 20) / cols; var ch = Math.Min(28, (inputs.Height - TitleBand - 8) / (rows + 1));
        var (even, odd) = inputs.BandFills;
        for (var c = 0; c < cols; c++) { sb.Append($"<rect x='{F(10 + c * cw)}' y='{F(TitleBand)}' width='{F(cw)}' height='{F(ch)}' fill='{inputs.HeaderFill}' stroke='{inputs.Divider}'/><text x='{F(14 + c * cw)}' y='{F(TitleBand + 18)}' font-size='10' font-weight='bold' fill='{inputs.OnSurface}'>{Xml(Trim(v.Columns[c], 16))}</text>"); }
        for (var r = 0; r < v.Rows.Count; r++) for (var c = 0; c < cols; c++) sb.Append($"<g data-row-index='{r}'><rect x='{F(10 + c * cw)}' y='{F(TitleBand + (r + 1) * ch)}' width='{F(cw)}' height='{F(ch)}' fill='{(r % 2 == 0 ? even : odd)}' stroke='{inputs.Divider}'/><text x='{F(14 + c * cw)}' y='{F(TitleBand + (r + 1) * ch + 18)}' font-size='10' fill='{inputs.OnSurface}'>{Xml(Trim(Cell(v.Rows[r], c, ""), 18))}</text></g>");
        return End(sb);
    }

    // ── Shared plumbing ────────────────────────────────────────────────────────

    private static JsonDocument? LoadGeoJson(VisualManifest v)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(v.ResolvedMapFile)) return JsonDocument.Parse(File.ReadAllText(v.ResolvedMapFile));
            var key = (v.Options.GetValueOrDefault("MAP_NAME") ?? "WORLD").ToLowerInvariant().Replace('_', '-');
            var name = key switch { "us-states" => "us-states", "us-counties" => "us-counties", "mn-counties" => "mn-counties", "canada-provinces" => "canada-provinces", "europe" => "europe", _ => "world" };
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"maps.{name}.geojson");
            return stream is null ? null : JsonDocument.Parse(stream);
        }
        catch { return null; }
    }

    private static void DrawMapOutline(StringBuilder sb, VisualManifest v, FocusedLayoutInputs inputs)
    {
        using var geo = LoadGeoJson(v); if (geo is null) return;
        foreach (var feature in geo.RootElement.GetProperty("features").EnumerateArray()) foreach (var polygon in Polygons(feature.GetProperty("geometry"))) sb.Append($"<path d='{PolygonPath(inputs, polygon)}' fill='{(inputs.IsDark ? "#2b3242" : "#f1f5f9")}' stroke='{inputs.Muted}' stroke-width='.35'/>");
    }

    private static IEnumerable<JsonElement> Polygons(JsonElement geometry)
    {
        var coordinates = geometry.GetProperty("coordinates"); var type = geometry.GetProperty("type").GetString();
        if (type == "Polygon") foreach (var ring in coordinates.EnumerateArray().Take(1)) yield return ring;
        else if (type == "MultiPolygon") foreach (var polygon in coordinates.EnumerateArray()) foreach (var ring in polygon.EnumerateArray().Take(1)) yield return ring;
    }

    private static string PolygonPath(FocusedLayoutInputs inputs, JsonElement ring) => string.Join(" ", ring.EnumerateArray().Select((point, i) => $"{(i == 0 ? "M" : "L")} {F(ProjectX(inputs, point[0].GetDouble()))} {F(ProjectY(inputs, point[1].GetDouble()))}")) + " Z";
    private static double ProjectX(FocusedLayoutInputs inputs, double lon) => 10 + (lon + 180) / 360 * (inputs.Width - 20);
    private static double ProjectY(FocusedLayoutInputs inputs, double lat) => TitleBand + (90 - lat) / 180 * (inputs.Height - TitleBand - 8);
    private static string Ramp(double t) { t = Math.Clamp(t, 0, 1); return $"#{(int)(224 - 216 * t):X2}{(int)(243 - 195 * t):X2}{(int)(248 - 141 * t):X2}"; }
    private static string? Property(JsonElement properties, string name) => properties.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static string[] Path(Node node, IReadOnlyList<Node> nodes) { var output = new List<string> { node.Id }; var parent = node.Parent; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { node.Id }; while (!string.IsNullOrWhiteSpace(parent) && seen.Add(parent)) { output.Insert(0, parent); parent = nodes.FirstOrDefault(n => n.Id.Equals(parent, StringComparison.OrdinalIgnoreCase))?.Parent ?? ""; } return output.ToArray(); }
    private static string Arc(double cx, double cy, double inner, double outer, double start, double end) { var large = end - start > Math.PI ? 1 : 0; var o1 = (X: cx + outer * Math.Cos(start), Y: cy + outer * Math.Sin(start)); var o2 = (X: cx + outer * Math.Cos(end), Y: cy + outer * Math.Sin(end)); if (inner <= .1) return $"M {F(cx)} {F(cy)} L {F(o1.X)} {F(o1.Y)} A {F(outer)} {F(outer)} 0 {large} 1 {F(o2.X)} {F(o2.Y)} Z"; var i2 = (X: cx + inner * Math.Cos(end), Y: cy + inner * Math.Sin(end)); var i1 = (X: cx + inner * Math.Cos(start), Y: cy + inner * Math.Sin(start)); return $"M {F(o1.X)} {F(o1.Y)} A {F(outer)} {F(outer)} 0 {large} 1 {F(o2.X)} {F(o2.Y)} L {F(i2.X)} {F(i2.Y)} A {F(inner)} {F(inner)} 0 {large} 0 {F(i1.X)} {F(i1.Y)} Z"; }
    private static int Index(VisualManifest v, string role, string? alias = null, int fallback = -1) { var column = FindRole(v, role) ?? (alias is null ? null : FindRole(v, alias)); var index = column is null ? -1 : v.Columns.FindIndex(c => c.Equals(column, StringComparison.OrdinalIgnoreCase)); return index >= 0 ? index : fallback >= 0 && fallback < v.Columns.Count ? fallback : -1; }
    private static string Cell(IReadOnlyList<string?> row, int index, string fallback) => index >= 0 && index < row.Count ? row[index] ?? fallback : fallback;
    private static double Number(IReadOnlyList<string?> row, int index, double fallback = 0) => index >= 0 && index < row.Count && double.TryParse(row[index], NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    /// <summary>
    /// Opens the canvas from the shared inputs: authored size, themed surface, an accessible name
    /// and description, and — when the visual resolved one — the compact interaction key, so a click
    /// on a focused mark filters on the same column a plan-backed visual would.
    /// </summary>
    private static StringBuilder Start(VisualManifest v, FocusedLayoutInputs inputs)
    {
        var interaction = inputs.Interaction.IsSelectable
            ? $" data-interaction-key='{Xml(inputs.Interaction.Key!)}' data-interaction-highlight='{Xml(inputs.Interaction.Highlight)}'"
            : string.Empty;
        return new StringBuilder(
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{F(inputs.Width)}' height='{F(inputs.Height)}' viewBox='0 0 {F(inputs.Width)} {F(inputs.Height)}' role='img' aria-label='{Xml(inputs.Title)}' font-family='sans-serif'{interaction}>" +
            $"<desc>{Xml(inputs.AccessibleDescription)}</desc>" +
            $"<rect width='100%' height='100%' fill='{inputs.Surface}'/>" +
            $"<text x='{F(inputs.Width / 2)}' y='22' text-anchor='middle' font-size='13' font-weight='bold' fill='{inputs.OnSurface}'>{Xml(inputs.Title)}</text>");
    }

    private static string End(StringBuilder sb) { sb.Append("</svg>"); return sb.ToString(); }
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? "";
    private static string Trim(string value, int max) => max > 1 && value.Length > max ? value[..(max - 1)] + "…" : value;
    private sealed record Node(string Id, string Parent, double Value, int Row);
    private sealed record TreemapItem(Node Node, double Area);
    private sealed record TreemapBounds(double X, double Y, double Width, double Height);
    private sealed record TreemapLayout(TreemapItem Item, TreemapBounds Bounds);
}
