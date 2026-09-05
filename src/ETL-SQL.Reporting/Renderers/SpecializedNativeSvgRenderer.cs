using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ETL_SQL.Reporting.Contracts;
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
        var colorIdx = Index(v, "color", fallback: -1);
        var nodes = v.Rows.Select((row, i) => new Node(
            Cell(row, label, $"Item {i + 1}"),
            Cell(row, parent, ""),
            Math.Max(0, Number(row, value)),
            i,
            colorIdx >= 0 ? Cell(row, colorIdx, "") : null
        )).ToList();
        var roots = nodes.Where(n => string.IsNullOrWhiteSpace(n.Parent) || nodes.All(p => !p.Id.Equals(n.Parent, StringComparison.OrdinalIgnoreCase))).ToList();
        if (roots.Count == 0) roots = nodes;

        // Options
        var showBreadcrumb = false;
        if (v.Options.TryGetValue("SHOW_BREADCRUMB", out var bcStr) && !string.IsNullOrWhiteSpace(bcStr))
        {
            var upper = bcStr.ToUpperInvariant();
            if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                throw new InvalidOperationException($"Invalid SHOW_BREADCRUMB '{bcStr}'. Valid values are ON or OFF.");
            showBreadcrumb = upper is "ON" or "TRUE" or "1";
        }

        double labelMinSize = 42.0;
        if (v.Options.TryGetValue("LABEL_MIN_SIZE", out var lmsStr) && !string.IsNullOrWhiteSpace(lmsStr))
        {
            if (!double.TryParse(lmsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out labelMinSize) || labelMinSize < 0.0)
                throw new InvalidOperationException($"Invalid LABEL_MIN_SIZE '{lmsStr}'. Value must be a non-negative number.");
        }

        var labelOverflow = (v.Options.GetValueOrDefault("LABEL_OVERFLOW") ?? "CLIP").ToUpperInvariant();
        if (labelOverflow is not ("CLIP" or "WRAP" or "HIDDEN"))
            throw new InvalidOperationException($"Invalid LABEL_OVERFLOW '{v.Options["LABEL_OVERFLOW"]}'. Valid values are CLIP, WRAP, or HIDDEN.");

        var sb = Start(v, inputs);

        double breadcrumbHeight = showBreadcrumb ? 22.0 : 0.0;
        if (showBreadcrumb)
        {
            var pathSummary = roots.Count <= 3 ? string.Join(" · ", roots.Select(r => r.Id)) : $"{roots[0].Id} · {roots[1].Id} +{roots.Count - 2} more";
            sb.Append($"<g class='hierarchy-breadcrumb' data-show-breadcrumb='true'>");
            sb.Append($"<rect x='8' y='{F(TitleBand)}' width='{F(inputs.Width - 16)}' height='18' rx='3' fill='{(inputs.IsDark ? "#2b3242" : "#f1f5f9")}'/>");
            sb.Append($"<text x='16' y='{F(TitleBand + 13)}' font-size='10' font-weight='500' fill='{inputs.Muted}'>All &gt; <tspan fill='{inputs.OnSurface}'>{Xml(pathSummary)}</tspan></text>");
            sb.Append("</g>");
        }

        Squarify(sb, inputs, roots, nodes, 8, TitleBand + breadcrumbHeight, inputs.Width - 16, inputs.Height - TitleBand - breadcrumbHeight - 8, 0, labelMinSize, labelOverflow);
        return End(sb);
    }

    private static void Squarify(StringBuilder sb, FocusedLayoutInputs inputs, IReadOnlyList<Node> siblings,
        IReadOnlyList<Node> all, double x, double y, double w, double h, int depth, double labelMinSize = 42.0, string labelOverflow = "CLIP")
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
            string color;
            if (!string.IsNullOrWhiteSpace(node.Color))
            {
                color = node.Color.StartsWith('#') ? node.Color : inputs.Color(node.Color, node.Row);
            }
            else
            {
                color = inputs.Color(node.Id, node.Row + depth);
            }

            sb.Append($"<g data-row-index='{node.Row}'><rect class='treemap-tile' x='{F(layout.Bounds.X)}' y='{F(layout.Bounds.Y)}' width='{F(Math.Max(0, layout.Bounds.Width))}' height='{F(Math.Max(0, layout.Bounds.Height))}' fill='{color}' fill-opacity='{F(Math.Max(.55, .9 - depth * .1))}' stroke='{inputs.Divider}' stroke-width='2'/><title>{Xml(node.Id)}: {F(node.Value)}</title>");

            var tileW = layout.Bounds.Width;
            var tileH = layout.Bounds.Height;
            if (tileW >= labelMinSize && tileH >= (labelMinSize * 0.4))
            {
                var maxChars = Math.Max(3, (int)(tileW / 7.2));
                if (labelOverflow == "HIDDEN" && node.Id.Length > maxChars)
                {
                    // Hidden if text exceeds available width
                }
                else if (labelOverflow == "WRAP" && node.Id.Length > maxChars && tileH >= 34)
                {
                    var splitIdx = node.Id.LastIndexOf(' ', Math.Min(node.Id.Length - 1, maxChars));
                    if (splitIdx > 0)
                    {
                        var line1 = node.Id[..splitIdx];
                        var line2 = Trim(node.Id[(splitIdx + 1)..], maxChars);
                        sb.Append($"<text class='treemap-label' x='{F(layout.Bounds.X + 6)}' y='{F(layout.Bounds.Y + 14)}' font-size='10' font-weight='{(depth == 0 ? "600" : "400")}' fill='{inputs.OnAccent}' data-overflow='wrap'><tspan x='{F(layout.Bounds.X + 6)}'>{Xml(line1)}</tspan><tspan x='{F(layout.Bounds.X + 6)}' dy='12'>{Xml(line2)}</tspan></text>");
                    }
                    else
                    {
                        sb.Append($"<text class='treemap-label' x='{F(layout.Bounds.X + 6)}' y='{F(layout.Bounds.Y + 16)}' font-size='11' font-weight='{(depth == 0 ? "600" : "400")}' fill='{inputs.OnAccent}' data-overflow='wrap'>{Xml(Trim(node.Id, maxChars))}</text>");
                    }
                }
                else
                {
                    sb.Append($"<text class='treemap-label' x='{F(layout.Bounds.X + 6)}' y='{F(layout.Bounds.Y + 16)}' font-size='11' font-weight='{(depth == 0 ? "600" : "400")}' fill='{inputs.OnAccent}' data-overflow='clip'>{Xml(Trim(node.Id, maxChars))}</text>");
                }
            }

            sb.Append("</g>");
            var children = all.Where(candidate => candidate.Parent.Equals(node.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (children.Count > 0)
                Squarify(sb, inputs, children, all, layout.Bounds.X + 4, layout.Bounds.Y + 21,
                    Math.Max(0, layout.Bounds.Width - 8), Math.Max(0, layout.Bounds.Height - 25), depth + 1, labelMinSize, labelOverflow);
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
        var colorIdx = Index(v, "color", fallback: -1);

        var paths = new List<(string[] Parts, double Value, int Row, string? Color)>();
        if (explicitLabel >= 0 && parent >= 0)
        {
            var raw = v.Rows.Select((row, i) => new Node(
                Cell(row, explicitLabel, $"Item {i + 1}"),
                Cell(row, parent, ""),
                Math.Max(0, Number(row, value)),
                i,
                colorIdx >= 0 ? Cell(row, colorIdx, "") : null
            )).ToList();
            foreach (var node in raw) paths.Add((Path(node, raw), node.Value, node.Row, node.Color));
        }
        else
        {
            var levels = Enumerable.Range(1, 8).Select(i => Index(v, $"level{i}", fallback: -1)).Where(i => i >= 0).ToArray();
            if (levels.Length == 0) levels = Enumerable.Range(0, Math.Max(1, v.Columns.Count - 1)).ToArray();
            paths.AddRange(v.Rows.Select((row, i) => (
                levels.Select(level => Cell(row, level, "")).Where(s => s.Length > 0).ToArray(),
                Math.Max(0, Number(row, value)),
                i,
                colorIdx >= 0 ? Cell(row, colorIdx, "") : null
            )));
        }

        var roots = paths.Select(path => path.Parts.FirstOrDefault() ?? string.Empty)
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var inputs = FocusedLayoutInputs.From(v, roots, bounds);

        // Options
        var showBreadcrumb = false;
        if (v.Options.TryGetValue("SHOW_BREADCRUMB", out var bcStr) && !string.IsNullOrWhiteSpace(bcStr))
        {
            var upper = bcStr.ToUpperInvariant();
            if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                throw new InvalidOperationException($"Invalid SHOW_BREADCRUMB '{bcStr}'. Valid values are ON or OFF.");
            showBreadcrumb = upper is "ON" or "TRUE" or "1";
        }

        var maxDepth = Math.Max(1, paths.Select(p => p.Parts.Length).DefaultIfEmpty(1).Max());
        var total = paths.Sum(p => p.Value); if (total <= 0) total = Math.Max(1, paths.Count);
        var cx = inputs.Width / 2;
        var breadcrumbHeight = showBreadcrumb ? 22.0 : 0.0;
        var cy = (TitleBand + breadcrumbHeight + inputs.Height) / 2;
        var radius = Math.Min(inputs.Width, inputs.Height - TitleBand - breadcrumbHeight) * .42;
        var sb = Start(v, inputs);

        if (showBreadcrumb)
        {
            var pathSummary = roots.Count <= 3 ? string.Join(" · ", roots) : $"{roots[0]} · {roots[1]} +{roots.Count - 2} more";
            sb.Append($"<g class='hierarchy-breadcrumb' data-show-breadcrumb='true'>");
            sb.Append($"<rect x='8' y='{F(TitleBand)}' width='{F(inputs.Width - 16)}' height='18' rx='3' fill='{(inputs.IsDark ? "#2b3242" : "#f1f5f9")}'/>");
            sb.Append($"<text x='16' y='{F(TitleBand + 13)}' font-size='10' font-weight='500' fill='{inputs.Muted}'>All &gt; <tspan fill='{inputs.OnSurface}'>{Xml(pathSummary)}</tspan></text>");
            sb.Append("</g>");
        }

        var cursor = -Math.PI / 2;
        foreach (var path in paths)
        {
            var root = path.Parts.FirstOrDefault() ?? string.Empty;
            var rootIndex = roots.FindIndex(item => item.Equals(root, StringComparison.OrdinalIgnoreCase));
            string color;
            if (!string.IsNullOrWhiteSpace(path.Color))
            {
                color = path.Color.StartsWith('#') ? path.Color : inputs.Color(path.Color, path.Row);
            }
            else
            {
                color = inputs.Color(root, rootIndex >= 0 ? rootIndex : path.Row);
            }
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
        var source = Index(v, "from", "source", fallback: 0);
        var target = Index(v, "to", "target", fallback: 1);
        var weight = Index(v, "value", "weight", fallback: -1);
        var sizeCol = Index(v, "node_size", "size", fallback: -1);
        var targetSizeCol = Index(v, "target_size", "to_size", fallback: -1);
        var groupCol = Index(v, "node_group", "group", fallback: -1);
        var xCol = Index(v, "node_x", "x", fallback: -1);
        var yCol = Index(v, "node_y", "y", fallback: -1);

        var links = v.Rows.Select((r, i) => (
            A: Cell(r, source, ""),
            B: Cell(r, target, ""),
            W: Math.Max(.1, Number(r, weight, 1)),
            Row: i
        )).Where(e => e.A.Length > 0 && e.B.Length > 0).ToList();

        var names = links.SelectMany(e => new[] { e.A, e.B })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nodeSizes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var nodeGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fixedCoords = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in v.Rows)
        {
            var a = Cell(r, source, "");
            var b = Cell(r, target, "");
            if (sizeCol >= 0 && a.Length > 0)
            {
                var sz = Number(r, sizeCol, double.NaN);
                if (!double.IsNaN(sz))
                    nodeSizes[a] = nodeSizes.TryGetValue(a, out var prev) ? Math.Max(prev, sz) : sz;
            }
            if (targetSizeCol >= 0 && b.Length > 0)
            {
                var sz = Number(r, targetSizeCol, double.NaN);
                if (!double.IsNaN(sz))
                    nodeSizes[b] = nodeSizes.TryGetValue(b, out var prev) ? Math.Max(prev, sz) : sz;
            }
            if (groupCol >= 0)
            {
                var grp = Cell(r, groupCol, "");
                if (grp.Length > 0)
                {
                    if (a.Length > 0 && !nodeGroups.ContainsKey(a)) nodeGroups[a] = grp;
                    if (b.Length > 0 && !nodeGroups.ContainsKey(b)) nodeGroups[b] = grp;
                }
            }
            if (xCol >= 0 && yCol >= 0 && a.Length > 0 && !fixedCoords.ContainsKey(a))
            {
                var x = Number(r, xCol, double.NaN);
                var y = Number(r, yCol, double.NaN);
                if (!double.IsNaN(x) && !double.IsNaN(y))
                {
                    fixedCoords[a] = (x, y);
                }
            }
        }

        const double minRadius = 5.0;
        const double maxRadius = 24.0;
        const double defaultRadius = 9.0;

        var radii = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (nodeSizes.Count > 0)
        {
            var minVal = nodeSizes.Values.Min();
            var maxVal = nodeSizes.Values.Max();
            foreach (var name in names)
            {
                if (nodeSizes.TryGetValue(name, out var val))
                {
                    if (maxVal > minVal)
                    {
                        var t = Math.Clamp((val - minVal) / (maxVal - minVal), 0, 1);
                        radii[name] = minRadius + (maxRadius - minRadius) * Math.Sqrt(t);
                    }
                    else
                    {
                        radii[name] = defaultRadius;
                    }
                }
                else
                {
                    radii[name] = defaultRadius;
                }
            }
        }
        else
        {
            foreach (var name in names)
            {
                radii[name] = defaultRadius;
            }
        }

        var distinctGroups = groupCol >= 0 && nodeGroups.Count > 0
            ? nodeGroups.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList()
            : names;
        var inputs = FocusedLayoutInputs.From(v, distinctGroups, bounds);

        var layout = v.Options.GetValueOrDefault("LAYOUT") ?? "FORCE";
        double repulsion = 500;
        if (v.Options.TryGetValue("REPULSION", out var repStr) &&
            double.TryParse(repStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedRep) &&
            parsedRep > 0)
        {
            repulsion = parsedRep;
        }

        var isDirected = false;
        if (v.Options.TryGetValue("DIRECTED", out var dirStr) || v.Options.TryGetValue("ARROWS", out dirStr))
        {
            isDirected = dirStr.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                         dirStr.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                         dirStr.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        var showLabels = true;
        if (v.Options.TryGetValue("NODE_LABELS", out var nlStr) || v.Options.TryGetValue("LABELS", out nlStr))
        {
            showLabels = !nlStr.Equals("OFF", StringComparison.OrdinalIgnoreCase) &&
                         !nlStr.Equals("FALSE", StringComparison.OrdinalIgnoreCase) &&
                         !nlStr.Equals("0", StringComparison.OrdinalIgnoreCase);
        }

        double minLabelSize = 0;
        if (v.Options.TryGetValue("NODE_LABEL_MIN_SIZE", out var mlsStr) &&
            double.TryParse(mlsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMls))
        {
            minLabelSize = parsedMls;
        }

        v.Options.TryGetValue("NODE_COLOR", out var customNodeColor);

        var cx = inputs.Width / 2;
        var cy = (TitleBand + inputs.Height) / 2;
        var plotMinX = 28.0;
        var plotMaxX = inputs.Width - 28.0;
        var plotMinY = TitleBand + 24.0;
        var plotMaxY = inputs.Height - 24.0;

        var mappedFixedPositions = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        if (fixedCoords.Count > 0)
        {
            var minFx = fixedCoords.Values.Min(c => c.X);
            var maxFx = fixedCoords.Values.Max(c => c.X);
            var minFy = fixedCoords.Values.Min(c => c.Y);
            var maxFy = fixedCoords.Values.Max(c => c.Y);

            foreach (var (k, c) in fixedCoords)
            {
                double px;
                if (maxFx > minFx)
                    px = plotMinX + (c.X - minFx) / (maxFx - minFx) * (plotMaxX - plotMinX);
                else if (c.X >= 0 && c.X <= inputs.Width)
                    px = Math.Clamp(c.X, plotMinX, plotMaxX);
                else
                    px = cx;

                double py;
                if (maxFy > minFy)
                    py = plotMinY + (c.Y - minFy) / (maxFy - minFy) * (plotMaxY - plotMinY);
                else if (c.Y >= 0 && c.Y <= inputs.Height)
                    py = Math.Clamp(c.Y, plotMinY, plotMaxY);
                else
                    py = cy;

                mappedFixedPositions[k] = (px, py);
            }
        }

        Dictionary<string, (double X, double Y)> pos;
        if (layout.Equals("CIRCULAR", StringComparison.OrdinalIgnoreCase))
        {
            var radius = Math.Min(inputs.Width, inputs.Height - TitleBand) * .38;
            pos = names.Select((n, i) =>
            {
                if (mappedFixedPositions.TryGetValue(n, out var fixedPos))
                    return (n, fixedPos);
                var angle = 2 * Math.PI * i / Math.Max(1, names.Count) - Math.PI / 2;
                return (n, (X: cx + radius * Math.Cos(angle), Y: cy + radius * Math.Sin(angle)));
            }).ToDictionary(x => x.n, x => x.Item2, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            pos = ComputeForceLayout(names, links, mappedFixedPositions, inputs.Width, inputs.Height, repulsion, cx, cy, plotMinX, plotMaxX, plotMinY, plotMaxY);
        }

        var max = links.Select(e => e.W).DefaultIfEmpty(1).Max();
        var sb = Start(v, inputs);

        var markerId = $"arrow-{Math.Abs(v.Name.GetHashCode())}";
        if (isDirected)
        {
            sb.Append($"<defs><marker id='{markerId}' viewBox='0 0 10 10' refX='7' refY='5' markerWidth='6' markerHeight='6' orient='auto-start-reverse'><path d='M 0 1.5 L 8 5 L 0 8.5 z' fill='{inputs.Muted}'/></marker></defs>");
        }

        foreach (var edge in links)
        {
            var a = pos[edge.A];
            var b = pos[edge.B];
            var rA = radii.GetValueOrDefault(edge.A, defaultRadius);
            var rB = radii.GetValueOrDefault(edge.B, defaultRadius);

            double x1 = a.X, y1 = a.Y, x2 = b.X, y2 = b.Y;
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist > 0.001)
            {
                var ux = dx / dist;
                var uy = dy / dist;
                if (isDirected && dist > rA + rB + 2)
                {
                    x1 = a.X + ux * rA;
                    y1 = a.Y + uy * rA;
                    x2 = b.X - ux * (rB + 2);
                    y2 = b.Y - uy * (rB + 2);
                }
            }

            var edgeWidth = 1 + 5 * edge.W / max;
            var arrowAttr = isDirected ? $" marker-end='url(#{markerId})'" : "";
            var titleArrow = isDirected ? "→" : "↔";
            sb.Append($"<line data-row-index='{edge.Row}' x1='{F(x1)}' y1='{F(y1)}' x2='{F(x2)}' y2='{F(y2)}' stroke='{inputs.Muted}' stroke-width='{F(edgeWidth)}'{arrowAttr}><title>{Xml(edge.A)} {titleArrow} {Xml(edge.B)}: {F(edge.W)}</title></line>");
        }

        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var p = pos[name];
            var r = radii.GetValueOrDefault(name, defaultRadius);

            var group = nodeGroups.TryGetValue(name, out var grp) ? grp : name;
            var fillColor = !string.IsNullOrWhiteSpace(customNodeColor) && ChartPalette.IsSafePaint(customNodeColor)
                ? customNodeColor
                : inputs.Color(group, i);

            sb.Append($"<circle cx='{F(p.X)}' cy='{F(p.Y)}' r='{F(r)}' fill='{fillColor}'/>");
            if (showLabels && r >= minLabelSize)
            {
                var labelY = p.Y + (p.Y < cy ? -(r + 5) : (r + 14));
                sb.Append($"<text x='{F(p.X)}' y='{F(labelY)}' text-anchor='middle' font-size='10' fill='{inputs.OnSurface}'>{Xml(Trim(name, 16))}</text>");
            }
        }
        return End(sb);
    }

    private static Dictionary<string, (double X, double Y)> ComputeForceLayout(
        List<string> names,
        List<(string A, string B, double W, int Row)> links,
        Dictionary<string, (double X, double Y)> fixedPositions,
        double width,
        double height,
        double repulsion,
        double cx,
        double cy,
        double minX,
        double maxX,
        double minY,
        double maxY)
    {
        var pos = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);
        var n = names.Count;
        if (n == 0) return pos;

        var initRadius = Math.Min(maxX - minX, maxY - minY) * 0.35;
        for (var i = 0; i < n; i++)
        {
            var name = names[i];
            if (fixedPositions.TryGetValue(name, out var fixedPos))
            {
                pos[name] = fixedPos;
            }
            else
            {
                var angle = 2 * Math.PI * i / n - Math.PI / 2;
                pos[name] = (cx + initRadius * Math.Cos(angle), cy + initRadius * Math.Sin(angle));
            }
        }

        if (n == 1) return pos;

        var area = (maxX - minX) * (maxY - minY);
        var k = Math.Sqrt(area / n) * Math.Sqrt(Math.Max(50.0, repulsion) / 500.0);
        var k2 = k * k;

        var avgWeight = links.Select(l => l.W).DefaultIfEmpty(1.0).Average();
        if (avgWeight <= 0) avgWeight = 1.0;

        const int iterations = 70;
        var initTemp = Math.Min(maxX - minX, maxY - minY) * 0.15;

        var dispX = new double[n];
        var dispY = new double[n];
        var nameToIndex = names.Select((name, idx) => (name, idx)).ToDictionary(x => x.name, x => x.idx, StringComparer.OrdinalIgnoreCase);

        for (var iter = 0; iter < iterations; iter++)
        {
            Array.Clear(dispX, 0, n);
            Array.Clear(dispY, 0, n);

            var temp = initTemp * (1.0 - (double)iter / iterations);

            for (var i = 0; i < n; i++)
            {
                var pi = pos[names[i]];
                for (var j = i + 1; j < n; j++)
                {
                    var pj = pos[names[j]];
                    var dx = pi.X - pj.X;
                    var dy = pi.Y - pj.Y;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.01) { dx = 0.1 * ((i % 3) - 1); dy = 0.1 * ((j % 3) - 1); dist = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy)); }

                    var force = k2 / dist;
                    var fx = (dx / dist) * force;
                    var fy = (dy / dist) * force;

                    dispX[i] += fx;
                    dispY[i] += fy;
                    dispX[j] -= fx;
                    dispY[j] -= fy;
                }
            }

            foreach (var edge in links)
            {
                if (!nameToIndex.TryGetValue(edge.A, out var u) || !nameToIndex.TryGetValue(edge.B, out var v))
                    continue;
                if (u == v) continue;

                var pu = pos[names[u]];
                var pv = pos[names[v]];
                var dx = pu.X - pv.X;
                var dy = pu.Y - pv.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 0.01) continue;

                var weightFactor = Math.Clamp(edge.W / avgWeight, 0.5, 2.0);
                var force = (dist * dist / k) * weightFactor;
                var fx = (dx / dist) * force;
                var fy = (dy / dist) * force;

                dispX[u] -= fx;
                dispY[u] -= fy;
                dispX[v] += fx;
                dispY[v] += fy;
            }

            for (var i = 0; i < n; i++)
            {
                var p = pos[names[i]];
                dispX[i] += (cx - p.X) * 0.05;
                dispY[i] += (cy - p.Y) * 0.05;
            }

            for (var i = 0; i < n; i++)
            {
                var name = names[i];
                if (fixedPositions.ContainsKey(name)) continue;

                var dx = dispX[i];
                var dy = dispY[i];
                var dispLen = Math.Sqrt(dx * dx + dy * dy);
                if (dispLen > 0.001)
                {
                    var step = Math.Min(dispLen, temp);
                    var cur = pos[name];
                    var nx = Math.Clamp(cur.X + (dx / dispLen) * step, minX, maxX);
                    var ny = Math.Clamp(cur.Y + (dy / dispLen) * step, minY, maxY);
                    pos[name] = (nx, ny);
                }
            }
        }

        return pos;
    }

    // ── MAP ────────────────────────────────────────────────────────────────────

    private static string Map(VisualManifest v, FocusedLayoutInputs inputs)
    {
        var sb = Start(v, inputs);
        var mode = v.Options.GetValueOrDefault("MODE") ?? "CHOROPLETH";
        var zoom = 1.0;
        if (v.Options.TryGetValue("ZOOM", out var zoomStr) && double.TryParse(zoomStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var zVal) && zVal > 0)
            zoom = zVal;

        var centerLat = 0.0;
        var centerLon = 0.0;
        if (v.Options.TryGetValue("CENTER", out var centerStr))
        {
            var trimmed = centerStr.Trim('(', ')', ' ', '\t');
            var parts = trimmed.Split(',');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLat) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLon))
            {
                centerLat = parsedLat;
                centerLon = parsedLon;
            }
        }

        if (mode.Equals("POINTS", StringComparison.OrdinalIgnoreCase))
        {
            DrawMapOutline(sb, v, inputs, zoom, centerLat, centerLon);
            var lon = Index(v, "lon", fallback: 0);
            var lat = Index(v, "lat", fallback: 1);
            var val = Index(v, "value", fallback: 2);
            var label = Index(v, "label", fallback: 3);
            var colorCol = Index(v, "color");
            var tooltipCol = Index(v, "tooltip");
            var accent = inputs.Color(null, 0);
            var max = v.Rows.Select(r => Number(r, val)).DefaultIfEmpty(1).Max();
            if (max <= 0) max = 1;

            foreach (var (row, i) in v.Rows.Select((r, i) => (r, i)))
            {
                var x = ProjectX(inputs, Number(row, lon), zoom, centerLon);
                var y = ProjectY(inputs, Number(row, lat), zoom, centerLat);
                var fill = accent;
                if (colorCol >= 0)
                {
                    var colorVal = Cell(row, colorCol, "");
                    if (!string.IsNullOrWhiteSpace(colorVal))
                    {
                        fill = colorVal.StartsWith('#') ? colorVal : inputs.Color(colorVal, i);
                    }
                }
                var tip = tooltipCol >= 0 ? Cell(row, tooltipCol, "") : "";
                var titleContent = !string.IsNullOrWhiteSpace(tip) ? tip : $"{Cell(row, label, $"Point {i + 1}")}: {F(Number(row, val))}";
                var r = F(4 + 12 * Math.Sqrt(Math.Max(0, Number(row, val)) / max));
                sb.Append($"<circle data-row-index='{i}' cx='{F(x)}' cy='{F(y)}' r='{r}' fill='{fill}' fill-opacity='.7'><title>{Xml(titleContent)}</title></circle>");
            }
            return End(sb);
        }

        var geo = LoadGeoJson(v);
        if (geo is null) { sb.Append($"<text x='{F(inputs.Width / 2)}' y='{F(inputs.Height / 2)}' text-anchor='middle' fill='{inputs.OnSurface}'>Map geometry unavailable</text>"); return End(sb); }

        var regionIndex = Index(v, "region", fallback: 0);
        var valueIndex = Index(v, "value", fallback: 1);
        var tooltipIndex = Index(v, "tooltip");

        var values = v.Rows.Select((row, index) => (
                Region: Cell(row, regionIndex, ""),
                Value: Number(row, valueIndex),
                Tooltip: tooltipIndex >= 0 ? Cell(row, tooltipIndex, "") : null,
                Row: index))
            .Where(item => item.Region.Length > 0)
            .GroupBy(item => item.Region, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (Value: group.Sum(item => item.Value), Tooltip: group.First().Tooltip, Row: group.First().Row),
                StringComparer.OrdinalIgnoreCase);

        var colorScaleType = (v.Options.GetValueOrDefault("COLOR_SCALE") ?? "LINEAR").Trim().ToUpperInvariant();
        var nullColor = v.Options.GetValueOrDefault("NULL_COLOR") ?? (inputs.IsDark ? "#2b3242" : "#e5e7eb");
        var colorLow = v.Options.GetValueOrDefault("COLOR_LOW") ?? "#dbeafe";
        var colorHigh = v.Options.GetValueOrDefault("COLOR_HIGH") ?? "#1d4ed8";

        var numericValues = values.Values.Select(item => item.Value).ToArray();
        var minimum = numericValues.DefaultIfEmpty(0).Min();
        var maximum = numericValues.DefaultIfEmpty(1).Max();
        if (maximum <= minimum) maximum = minimum + 1;
        var sortedValues = numericValues.OrderBy(x => x).ToArray();

        string ResolveChoroplethColor(double val)
        {
            double t;
            if (colorScaleType == "QUANTILE" && sortedValues.Length > 1)
            {
                int rank = Array.BinarySearch(sortedValues, val);
                if (rank < 0) rank = ~rank;
                rank = Math.Clamp(rank, 0, sortedValues.Length - 1);
                int bucket = Math.Clamp((int)Math.Floor((double)rank / sortedValues.Length * 5.0), 0, 4);
                t = bucket / 4.0;
            }
            else if (colorScaleType == "QUANTIZE")
            {
                double ratio = Math.Clamp((val - minimum) / (maximum - minimum), 0.0, 1.0);
                int bucket = Math.Clamp((int)Math.Floor(ratio * 5.0), 0, 4);
                t = bucket / 4.0;
            }
            else if (colorScaleType == "THRESHOLD")
            {
                double ratio = Math.Clamp((val - minimum) / (maximum - minimum), 0.0, 1.0);
                t = ratio switch
                {
                    < 0.25 => 0.0,
                    < 0.50 => 0.33,
                    < 0.75 => 0.67,
                    _ => 1.0
                };
            }
            else // LINEAR
            {
                t = Math.Clamp((val - minimum) / (maximum - minimum), 0.0, 1.0);
            }
            return InterpolateColorDouble(colorLow, colorHigh, t);
        }

        foreach (var feature in geo.RootElement.GetProperty("features").EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            var name = Property(props, "name") ?? Property(props, "NAME") ?? Property(props, "admin") ?? "";
            var hasMatch = values.TryGetValue(name, out var match);
            var fill = hasMatch ? ResolveChoroplethColor(match.Value) : nullColor;
            var tip = hasMatch && !string.IsNullOrWhiteSpace(match.Tooltip) ? match.Tooltip : (hasMatch ? $"{name}: {F(match.Value)}" : name);

            foreach (var polygon in Polygons(feature.GetProperty("geometry")))
            {
                sb.Append($"<path{(hasMatch ? $" data-row-index='{match.Row}'" : "")} d='{PolygonPath(inputs, polygon, zoom, centerLat, centerLon)}' fill='{fill}' stroke='{inputs.Divider}' stroke-width='.35'><title>{Xml(tip)}</title></path>");
            }
        }
        geo.Dispose();
        return End(sb);
    }

    // ── MATRIX ─────────────────────────────────────────────────────────────────

    private static string Matrix(VisualManifest v, FocusedLayoutInputs inputs)
    {
        var sb = Start(v, inputs); var cols = Math.Max(1, v.Columns.Count); var rows = Math.Max(1, v.Rows.Count);
        var cw = (inputs.Width - 20) / cols; var ch = Math.Min(28, (inputs.Height - TitleBand - 8) / (rows + 1));
        var (even, odd) = inputs.BandFills;
        for (var c = 0; c < cols; c++) { sb.Append($"<rect x='{F(10 + c * cw)}' y='{F(TitleBand)}' width='{F(cw)}' height='{F(ch)}' fill='{inputs.HeaderFill}' stroke='{inputs.Divider}'/><text x='{F(14 + c * cw)}' y='{F(TitleBand + 18)}' font-size='10' font-weight='bold' fill='{inputs.OnSurface}'>{Xml(Trim(v.Columns[c], 16))}</text>"); }
        var isDataBar = v.Options.ContainsKey("mapping:value:data_bar") ||
                        (v.Options.TryGetValue("DATA_BAR", out var dbOpt) && (dbOpt.Equals("ON", StringComparison.OrdinalIgnoreCase) || dbOpt.Equals("TRUE", StringComparison.OrdinalIgnoreCase))) ||
                        (v.Options.TryGetValue("DATA_BARS", out var dbsOpt) && (dbsOpt.Equals("ON", StringComparison.OrdinalIgnoreCase) || dbsOpt.Equals("TRUE", StringComparison.OrdinalIgnoreCase)));
        var barColor = v.Options.GetValueOrDefault("mapping:value:data_bar_color") ?? v.Options.GetValueOrDefault("DATA_BAR_COLOR") ?? "#4472C4";
        double maxNum = 1;
        if (isDataBar)
        {
            foreach (var row in v.Rows)
                for (var c = 0; c < cols; c++)
                    if (double.TryParse(Cell(row, c, ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && n > maxNum)
                        maxNum = n;
        }

        for (var r = 0; r < v.Rows.Count; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var cellRaw = Cell(v.Rows[r], c, "");
                var (customBg, customFg) = EvaluateMatrixCellFormatting(v, cellRaw, v.Columns[c]);
                var bgFill = customBg ?? (r % 2 == 0 ? even : odd);
                var textFill = customFg ?? (customBg != null ? "#000000" : inputs.OnSurface);
                sb.Append($"<g data-row-index='{r}'><rect x='{F(10 + c * cw)}' y='{F(TitleBand + (r + 1) * ch)}' width='{F(cw)}' height='{F(ch)}' fill='{bgFill}' stroke='{inputs.Divider}'/>");
                if (isDataBar && double.TryParse(cellRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) && val > 0)
                {
                    var barW = Math.Min(cw, (val / maxNum) * cw);
                    sb.Append($"<rect class='matrix-data-bar' x='{F(10 + c * cw)}' y='{F(TitleBand + (r + 1) * ch)}' width='{F(barW)}' height='{F(ch)}' fill='{barColor}' opacity='0.35'/>");
                }
                sb.Append($"<text x='{F(14 + c * cw)}' y='{F(TitleBand + (r + 1) * ch + 18)}' font-size='10' fill='{textFill}'>{Xml(Trim(cellRaw, 18))}</text></g>");
            }
        }
        return End(sb);
    }

    private static (string? Bg, string? Fg) EvaluateMatrixCellFormatting(VisualManifest v, string rawVal, string colName)
    {
        if (v.FormattingRules == null || v.FormattingRules.Count == 0) return (null, null);
        if (!double.TryParse(rawVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)) return (null, null);

        foreach (var rule in v.FormattingRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Condition)) continue;
            if (EvaluateMatrixNumericCondition(rule.Condition, num, colName))
            {
                return (rule.Color, rule.FontColor);
            }
        }
        return (null, null);
    }

    private static bool EvaluateMatrixNumericCondition(string cond, double val, string colName)
    {
        var expr = cond.Trim();
        while (expr.StartsWith('(') && expr.EndsWith(')'))
        {
            expr = expr[1..^1].Trim();
        }
        var mBetween = System.Text.RegularExpressions.Regex.Match(expr, @"(?:(?:[\w""\[\]]+)\s+)?BETWEEN\s+(-?[\d.]+)\s+AND\s+(-?[\d.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (mBetween.Success)
        {
            if (double.TryParse(mBetween.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var low) &&
                double.TryParse(mBetween.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var high))
            {
                return val >= low && val <= high;
            }
        }

        var andIdx = expr.IndexOf(" AND ", StringComparison.OrdinalIgnoreCase);
        if (andIdx >= 0)
        {
            return EvaluateMatrixNumericCondition(expr[..andIdx], val, colName) &&
                   EvaluateMatrixNumericCondition(expr[(andIdx + 5)..], val, colName);
        }
        var orIdx = expr.IndexOf(" OR ", StringComparison.OrdinalIgnoreCase);
        if (orIdx >= 0)
        {
            return EvaluateMatrixNumericCondition(expr[..orIdx], val, colName) ||
                   EvaluateMatrixNumericCondition(expr[(orIdx + 4)..], val, colName);
        }

        var mBare = System.Text.RegularExpressions.Regex.Match(expr, @"^([<>!=]=?|<>)\s*(-?[\d.]+)$");
        if (mBare.Success)
        {
            var op = mBare.Groups[1].Value;
            if (double.TryParse(mBare.Groups[2].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var target))
                return CompareMatrixNumeric(val, op, target);
        }

        var mComp = System.Text.RegularExpressions.Regex.Match(expr, @"^(.*?)\s*([<>!=]=?|<>)\s*(.*?)$");
        if (mComp.Success)
        {
            var left = mComp.Groups[1].Value.Trim().Trim('(', ')', '[', ']', '"');
            var op = mComp.Groups[2].Value;
            var right = mComp.Groups[3].Value.Trim().Trim('(', ')', '[', ']', '"');

            if (double.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out var rNum))
                return CompareMatrixNumeric(val, op, rNum);
            if (double.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out var lNum))
                return CompareMatrixNumeric(lNum, op, val);
        }

        return false;
    }

    private static bool CompareMatrixNumeric(double a, string op, double b) => op switch
    {
        ">" => a > b,
        ">=" => a >= b,
        "<" => a < b,
        "<=" => a <= b,
        "=" or "==" => Math.Abs(a - b) < 1e-9,
        "!=" or "<>" => Math.Abs(a - b) >= 1e-9,
        _ => false
    };

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

    private static void DrawMapOutline(StringBuilder sb, VisualManifest v, FocusedLayoutInputs inputs, double zoom = 1.0, double centerLat = 0.0, double centerLon = 0.0)
    {
        using var geo = LoadGeoJson(v); if (geo is null) return;
        foreach (var feature in geo.RootElement.GetProperty("features").EnumerateArray()) foreach (var polygon in Polygons(feature.GetProperty("geometry"))) sb.Append($"<path d='{PolygonPath(inputs, polygon, zoom, centerLat, centerLon)}' fill='{(inputs.IsDark ? "#2b3242" : "#f1f5f9")}' stroke='{inputs.Muted}' stroke-width='.35'/>");
    }

    private static IEnumerable<JsonElement> Polygons(JsonElement geometry)
    {
        var coordinates = geometry.GetProperty("coordinates"); var type = geometry.GetProperty("type").GetString();
        if (type == "Polygon") foreach (var ring in coordinates.EnumerateArray().Take(1)) yield return ring;
        else if (type == "MultiPolygon") foreach (var polygon in coordinates.EnumerateArray()) foreach (var ring in polygon.EnumerateArray().Take(1)) yield return ring;
    }

    private static string PolygonPath(FocusedLayoutInputs inputs, JsonElement ring, double zoom = 1.0, double centerLat = 0.0, double centerLon = 0.0) => string.Join(" ", ring.EnumerateArray().Select((point, i) => $"{(i == 0 ? "M" : "L")} {F(ProjectX(inputs, point[0].GetDouble(), zoom, centerLon))} {F(ProjectY(inputs, point[1].GetDouble(), zoom, centerLat))}")) + " Z";
    private static double ProjectX(FocusedLayoutInputs inputs, double lon, double zoom = 1.0, double centerLon = 0.0)
    {
        var cx = 10 + (inputs.Width - 20) / 2.0;
        return cx + ((lon - centerLon) / 360.0) * (inputs.Width - 20) * zoom;
    }
    private static double ProjectY(FocusedLayoutInputs inputs, double lat, double zoom = 1.0, double centerLat = 0.0)
    {
        var cy = TitleBand + (inputs.Height - TitleBand - 8) / 2.0;
        return cy + ((centerLat - lat) / 180.0) * (inputs.Height - TitleBand - 8) * zoom;
    }
    private static string InterpolateColorDouble(string low, string high, double ratio)
    {
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        if (low.Length != 7 || high.Length != 7 || low[0] != '#' || high[0] != '#') return high;
        if (!int.TryParse(low.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rLow) ||
            !int.TryParse(low.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gLow) ||
            !int.TryParse(low.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bLow) ||
            !int.TryParse(high.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rHigh) ||
            !int.TryParse(high.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gHigh) ||
            !int.TryParse(high.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bHigh))
            return high;

        static int Mix(int first, int second, double amount) =>
            (int)Math.Round(first + (second - first) * amount, MidpointRounding.AwayFromZero);

        return $"#{Mix(rLow, rHigh, ratio):X2}{Mix(gLow, gHigh, ratio):X2}{Mix(bLow, bHigh, ratio):X2}";
    }
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
    private sealed record Node(string Id, string Parent, double Value, int Row, string? Color = null);
    private sealed record TreemapItem(Node Node, double Area);
    private sealed record TreemapBounds(double X, double Y, double Width, double Height);
    private sealed record TreemapLayout(TreemapItem Item, TreemapBounds Bounds);
}
