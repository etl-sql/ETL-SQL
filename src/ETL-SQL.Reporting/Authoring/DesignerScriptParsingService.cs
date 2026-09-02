using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Reporting.Authoring;

/// <summary>
/// Host-neutral Report-SQL parser converting AST into <see cref="DesignerAuthoringState"/>
/// for visual canvas roundtripping across Portal, Workstation Editor, VS Code, and Studio.
/// </summary>
public sealed class DesignerScriptParsingService
{
    private const int GridCols = 12;

    public DesignerAuthoringState Parse(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return EmptyState();

        try
        {
            var tokens = new Lexer(script).Tokenize();
            var ast = new CoreParser(tokens, script).Parse();
            return ScriptToState(ast, script);
        }
        catch
        {
            return EmptyState();
        }
    }

    /// <summary>
    /// Reads design state from an AST the caller has already parsed. Hosts that need the diagnostics or
    /// enforce their own statement limits parse once and call this, rather than parsing a second time.
    /// </summary>
    public DesignerAuthoringState Parse(Script ast, string? script)
    {
        ArgumentNullException.ThrowIfNull(ast);
        return ScriptToState(ast, script);
    }

    public static DesignerAuthoringState EmptyState() =>
        new([new DesignerAuthoringPage("p1", "Page 1", "Dashboard", [])], []);

    private static DesignerAuthoringState ScriptToState(Script ast, string? script = null)
    {
        var datasets = ast.Statements.OfType<CreateDatasetStatement>()
            .Select((ds, i) => new DesignerAuthoringDataset(
                $"ds_{i}",
                NormalizeDatasetName(ds.TempTableName),
                ExtractAuthoredNode(script, ds.SourceQuery, ds.SourceQuery.ToSql()).Trim().TrimEnd(';'),
                ds.Ttl))
            .ToList();

        var elements = new Dictionary<string, DesignerAuthoringVisual>(StringComparer.OrdinalIgnoreCase);
        int idx = 0;

        var visualsList = ast.Statements.OfType<CreateVisualStatement>()
            .Select(v => (v.Name, Dto: VisualToAuthoring(v, idx++, 1, 1, 12, 4, script))).ToList();
        var containersList = ast.Statements.OfType<CreateContainerStatement>()
            .Select(c => (c.Name, c.SlotMap, Dto: ContainerToAuthoring(c, idx++))).ToList();
        var buttonsList = ast.Statements.OfType<CreateButtonStatement>()
            .Select(b => (b.Name, Dto: ButtonToAuthoring(b, idx++))).ToList();

        var childToParentId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in containersList)
        {
            foreach (var childName in c.SlotMap.Values)
            {
                childToParentId[childName] = c.Dto.Id;
            }
        }

        foreach (var item in visualsList)
        {
            var dto = item.Dto;
            if (childToParentId.TryGetValue(item.Name, out var pId))
            {
                dto = dto with { ContainerId = pId };
            }
            elements[item.Name] = dto;
        }
        foreach (var item in containersList)
        {
            elements[item.Name] = item.Dto;
        }
        foreach (var item in buttonsList)
        {
            elements[item.Name] = item.Dto;
        }

        var pages = new List<DesignerAuthoringPage>();
        int pageNum = 0;
        foreach (var stmt in ast.Statements.OfType<CreatePageStatement>())
        {
            pageNum++;
            var grid = ParseStructure(stmt.Structure ?? ".");
            var pageVisuals = new List<DesignerAuthoringVisual>();
            int vidx = 0;

            foreach (var (slot, elName) in stmt.SlotMap)
            {
                if (!elements.TryGetValue(elName, out var el))
                    continue;

                var (col, row, colSpan, rowSpan) = FindSlotBounds(grid, slot);
                pageVisuals.Add(el with { GridCol = col, GridRow = row, GridColSpan = colSpan, GridRowSpan = rowSpan });
            }

            var containerChildren = new List<DesignerAuthoringVisual>();
            foreach (var v in pageVisuals.Where(x => string.Equals(x.Type, "CONTAINER", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var cStmt = ast.Statements.OfType<CreateContainerStatement>()
                    .FirstOrDefault(c => string.Equals(c.Name, v.Name, StringComparison.OrdinalIgnoreCase));
                if (cStmt == null) continue;

                var containerGrid = ParseStructure(cStmt.Structure ?? "A");
                foreach (var (slot, childName) in cStmt.SlotMap)
                {
                    if (!elements.TryGetValue(childName, out var childEl))
                        continue;

                    var (cCol, cRow, cColSpan, cRowSpan) = CalculateAbsoluteChildBounds(
                        v.GridCol, v.GridRow, v.GridColSpan, v.GridRowSpan,
                        containerGrid, slot);

                    containerChildren.Add(childEl with
                    {
                        GridCol = cCol,
                        GridRow = cRow,
                        GridColSpan = cColSpan,
                        GridRowSpan = cRowSpan,
                        ContainerId = v.Id
                    });
                }
            }
            pageVisuals.AddRange(containerChildren);

            if (pageVisuals.Count == 0)
            {
                foreach (var el in elements.Values)
                {
                    pageVisuals.Add(el with { GridCol = 1, GridRow = ++vidx * 4 - 3, GridColSpan = 12, GridRowSpan = 4 });
                }
            }

            pages.Add(new DesignerAuthoringPage(
                $"p{pageNum}",
                stmt.Name,
                stmt.PageMode.ToString(),
                pageVisuals,
                stmt.PrintLayout is null ? null : new DesignerAuthoringPageLayout(
                    stmt.PrintLayout.PageSize,
                    stmt.PrintLayout.Orientation,
                    stmt.PrintLayout.MarginTop,
                    stmt.PrintLayout.MarginRight,
                    stmt.PrintLayout.MarginBottom,
                    stmt.PrintLayout.MarginLeft,
                    stmt.PrintLayout.Units,
                    stmt.PrintLayout.Overflow,
                    stmt.PrintLayout.CustomWidth,
                    stmt.PrintLayout.CustomHeight)));
        }

        if (pages.Count == 0 && elements.Count > 0)
        {
            int vidx = 0;
            var synth = elements.Values.Select(el =>
                el with { GridCol = 1, GridRow = ++vidx * 4 - 3, GridColSpan = 12, GridRowSpan = 4 }).ToList();
            pages.Add(new DesignerAuthoringPage("p1", "Page 1", "Dashboard", synth));
        }

        if (pages.Count == 0)
        {
            pages.Add(new DesignerAuthoringPage("p1", "Page 1", "Dashboard", []));
        }

        return new DesignerAuthoringState(
            pages, datasets, null, BookmarksToAuthoring(ast), ParametersToAuthoring(ast), ConnectionsToAuthoring(ast, script));
    }

    /// <summary>
    /// The <c>CREATE CONNECTION</c> statements the script declares, as the author wrote them.
    ///
    /// <para>Top level only, which is what a preamble can reproduce: a connection created inside a
    /// block is scoped to that block, and lifting it out would change what the script means. The
    /// text is the authored slice rather than a re-serialisation, so a preamble carries the exact
    /// bytes — secret references, comments, and formatting included — that the run would use.</para>
    /// </summary>
    private static List<DesignerAuthoringConnection> ConnectionsToAuthoring(Script ast, string? script) =>
        ast.Statements.OfType<CreateConnectionStatement>()
            .Select(connection => new DesignerAuthoringConnection(
                connection.ConnectionName,
                ExtractAuthoredNode(script, connection, connection.ToSql()).Trim()))
            .ToList();

    private static List<DesignerAuthoringParameter> ParametersToAuthoring(Script ast) =>
        ast.Statements.SelectMany(statement => statement is BlockStatement block
                ? block.Statements.OfType<DeclareStatement>().Select(declaration => (Declaration: declaration, BlockScoped: true))
                : statement is DeclareStatement declaration
                    ? [(Declaration: declaration, BlockScoped: false)]
                    : [])
            .Select(item => new DesignerAuthoringParameter(
                item.Declaration.VariableName,
                item.Declaration.DataType,
                item.Declaration.InitialValue?.ToSql(),
                item.Declaration.IsInput,
                item.Declaration.IsOutput,
                item.Declaration.IsRequired,
                item.Declaration.IsSensitive,
                item.BlockScoped))
            .ToList();

    private static List<DesignerAuthoringBookmark> BookmarksToAuthoring(Script ast) =>
        ast.Statements.OfType<CreateBookmarkStatement>()
            .Select((bookmark, index) => new DesignerAuthoringBookmark(
                $"bm_{index}",
                bookmark.Name,
                bookmark.Title is LiteralExpression literal
                    ? literal.Value?.ToString()
                    : bookmark.Title?.ToSql().Trim('\''),
                bookmark.PageName,
                bookmark.IsDefault,
                bookmark.Parameters
                    .Select(p => new DesignerAuthoringBookmarkParameter(p.ParameterName, p.Value.ToSql()))
                    .ToList(),
                bookmark.StateEntries
                    .Select(s => new DesignerAuthoringBookmarkState(
                        s.ObjectName, s.Property.ToString().ToUpperInvariant(), s.On))
                    .ToList()))
            .ToList();

    private static DesignerAuthoringVisual VisualToAuthoring(
        CreateVisualStatement v, int idx, int col, int row, int colSpan, int rowSpan, string? script = null)
    {
        var authoredTitle = v.TitleDefinition?.Text ?? v.Title;
        var title = authoredTitle is LiteralExpression lit
            ? lit.Value?.ToString()
            : authoredTitle?.ToSql().Trim('\'', '"');

        var sourceName = v.Source.TempTableName;
        var dataset = !string.IsNullOrWhiteSpace(sourceName) && sourceName.StartsWith('&')
            ? NormalizeDatasetName(sourceName)
            : null;

        var mappings = v.Mappings.ToDictionary(
            m => m.Role.ToUpper(),
            m => m.Column,
            StringComparer.OrdinalIgnoreCase);

        var fieldFormatting = v.Mappings
            .Where(mapping => mapping.Format != null || mapping.Align != null || mapping.DisplayName != null
                || mapping.DataBar || mapping.DataBarColor != null
                || mapping.ColorScaleFrom != null || mapping.ColorScaleTo != null)
            .ToDictionary(
                mapping => mapping.Role.ToUpperInvariant(),
                mapping => new DesignerAuthoringFieldFormatting(
                    mapping.Format,
                    mapping.Align,
                    mapping.DisplayName,
                    mapping.DataBar,
                    mapping.DataBarColor,
                    mapping.ColorScaleFrom,
                    mapping.ColorScaleTo),
                StringComparer.OrdinalIgnoreCase);

        var options = v.Options.ToDictionary(
            o => o.Key,
            o => NormalizeOptionValue(o.Value),
            StringComparer.OrdinalIgnoreCase);

        if (v.Source.InlineSelect != null)
        {
            var inline = ExtractAuthoredNode(script, v.Source.InlineSelect, v.Source.InlineSelect.ToSql())
                .Trim().TrimEnd(';');
            options["inline_source"] = $"({inline})";
        }
        else if (!string.IsNullOrWhiteSpace(v.Source.TempTableName))
        {
            options["inline_source"] = v.Source.TempTableName;
        }

        foreach (var style in v.Styles)
        {
            options[style.Key] = style.Value;
        }

        foreach (var act in v.Actions)
        {
            options[$"action:{act.Trigger.ToUpper()}"] = act.ToSql();
        }
        foreach (var inter in v.Interactions)
        {
            options[$"interaction:{inter.Key.ToUpper()}"] = inter.Value;
        }
        if (v.Overlays.Count > 0)
        {
            options["overlays"] = "OVERLAYS (" + string.Join(", ", v.Overlays.Select(FormatOverlay)) + ")";
        }
        if (v.Cascade != null)
        {
            options["cascade"] = v.Cascade.ToSql();
        }
        // A TEXT band's content lives in its own DEFAULT clause, not in OPTIONS. Without this the
        // designer cannot see the text an author wrote, so a round-trip through the canvas would
        // hand the patcher a band with no content and delete it.
        if (v.DefaultValue != null)
        {
            options["text_default"] = v.DefaultValue.ToSql();
        }
        if (v.PrintLayout != null)
        {
            var parts = new List<string>();
            if (v.PrintLayout.PageBreakBefore.HasValue) parts.Add($"PAGE_BREAK_BEFORE = {(v.PrintLayout.PageBreakBefore.Value ? "ON" : "OFF")}");
            if (v.PrintLayout.PageBreakAfter.HasValue) parts.Add($"PAGE_BREAK_AFTER = {(v.PrintLayout.PageBreakAfter.Value ? "ON" : "OFF")}");
            if (v.PrintLayout.KeepTogether.HasValue) parts.Add($"KEEP_TOGETHER = {(v.PrintLayout.KeepTogether.Value ? "ON" : "OFF")}");
            if (v.PrintLayout.ExcludeFromPrint.HasValue) parts.Add($"EXCLUDE_FROM_PRINT = {(v.PrintLayout.ExcludeFromPrint.Value ? "ON" : "OFF")}");
            options["print_layout"] = $"PRINT_LAYOUT ({string.Join(", ", parts)})";
        }
        if (v.AdvancedChart != null)
        {
            if (script != null && v.AdvancedChart.StartOffset >= 0 && v.AdvancedChart.EndOffset > v.AdvancedChart.StartOffset && v.AdvancedChart.EndOffset <= script.Length)
            {
                options["advanced_chart"] = script[v.AdvancedChart.StartOffset..v.AdvancedChart.EndOffset];
            }
            else
            {
                options["advanced_chart"] = v.AdvancedChart.ToSql();
            }
        }
        if (v.HtmlTemplate != null)
        {
            options["html_template"] = v.HtmlTemplate.Template;
            options["html_mode"] = v.HtmlTemplate.Mode.ToString().ToUpperInvariant();
            if (v.HtmlTemplate.Css != null)
                options["html_style"] = v.HtmlTemplate.Css;
            if (v.HtmlTemplate.Fallback != null)
                options["html_fallback"] = v.HtmlTemplate.Fallback;
        }

        var titleFormatting = ToAuthoringTextFormatting(v.TitleDefinition, title);
        var subtitleFormatting = ToAuthoringTextFormatting(v.SubtitleDefinition,
            v.Subtitle is LiteralExpression subtitleLiteral
                ? subtitleLiteral.Value?.ToString()
                : v.Subtitle?.ToSql().Trim('\'', '"'));
        var xAxis = v.AxisOptions.FirstOrDefault(axis => axis.Axis.Equals("X", StringComparison.OrdinalIgnoreCase))?
            .Options.ToDictionary(option => option.Key, option => option.Value, StringComparer.OrdinalIgnoreCase);
        var yAxis = v.AxisOptions.FirstOrDefault(axis => axis.Axis.Equals("Y", StringComparison.OrdinalIgnoreCase))?
            .Options.ToDictionary(option => option.Key, option => option.Value, StringComparer.OrdinalIgnoreCase);
        var formatting = titleFormatting is not null || subtitleFormatting is not null
            || xAxis is not null || yAxis is not null || !v.Palette.IsDefaultOrEmpty
            || v.FormattingRules.Count > 0 || fieldFormatting.Count > 0
            ? new DesignerAuthoringVisualFormatting(
                titleFormatting,
                subtitleFormatting,
                xAxis,
                yAxis,
                v.Palette.IsDefaultOrEmpty ? null : v.Palette.ToList(),
                v.FormattingRules.Select(rule => new DesignerAuthoringConditionalFormattingRule(
                    rule.Condition.ToSql(), rule.Color, rule.FontColor)).ToList(),
                fieldFormatting.Count == 0 ? null : fieldFormatting)
            : null;

        return new DesignerAuthoringVisual(
            $"v_{v.Name}_{idx}",
            v.Name,
            v.VisualType.ToString().ToUpper(),
            col, row, colSpan, rowSpan,
            title,
            dataset,
            mappings,
            options,
            Formatting: formatting);
    }

    private static DesignerAuthoringTextFormatting? ToAuthoringTextFormatting(
        TitleDefinition? definition,
        string? text)
    {
        if (definition is null && string.IsNullOrWhiteSpace(text)) return null;
        return new DesignerAuthoringTextFormatting(
            text,
            definition?.Color,
            definition?.Font,
            definition?.Size,
            definition?.Weight,
            definition?.Align);
    }

    private static string ExtractAuthoredNode(string? script, AstNode node, string fallback)
    {
        if (script is null || node.StartOffset < 0 || node.EndOffset <= node.StartOffset || node.EndOffset > script.Length)
            return fallback;
        return script[node.StartOffset..node.EndOffset];
    }

    private static DesignerAuthoringVisual ContainerToAuthoring(CreateContainerStatement c, int idx)
    {
        var title = c.Title is LiteralExpression lit
            ? lit.Value?.ToString()
            : c.Title?.ToSql().Trim('\'', '"');
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONTAINER_TYPE"] = c.ContainerType
        };
        return new DesignerAuthoringVisual(
            $"v_{c.Name}_{idx}", c.Name, "CONTAINER",
            1, 1, 12, 4, title, null, new Dictionary<string, string>(), options);
    }

    private static DesignerAuthoringVisual ButtonToAuthoring(CreateButtonStatement b, int idx)
    {
        var title = b.Title is LiteralExpression lit
            ? lit.Value?.ToString()
            : b.Title?.ToSql().Trim('\'', '"');
        var options = b.Options.ToDictionary(
            o => o.Key,
            o => o.Value,
            StringComparer.OrdinalIgnoreCase);
        if (!options.ContainsKey("BUTTON_TYPE"))
        {
            options["BUTTON_TYPE"] = "REFRESH";
        }
        return new DesignerAuthoringVisual(
            $"v_{b.Name}_{idx}", b.Name, "BUTTON",
            1, 1, 12, 4, title, null, new Dictionary<string, string>(), options);
    }

    /// <summary>
    /// Splits a LAYOUT STRUCTURE into rows of slots the same way the runtime does. Rows separate on
    /// <c>/</c> or a newline — an author who writes the grid across several lines means several rows,
    /// and reading it as one row put every visual in the same cell, which the patcher then wrote back
    /// as a collapsed single-slot layout.
    /// </summary>
    internal static List<List<string>> ParseStructure(string structure)
    {
        var rows = structure.Split(
            StructureRowSeparators,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return rows.Select(r =>
            r.Split(StructureSlotSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
             .Select(s => s.Trim('"', '\'', '[', ']'))
             .ToList()
        ).ToList();
    }

    private static readonly char[] StructureRowSeparators = ['/', '\n', '\r'];
    private static readonly char[] StructureSlotSeparators = [' ', '\t'];

    private static (int col, int row, int colSpan, int rowSpan) FindSlotBounds(
        List<List<string>> grid, string slot)
    {
        int minC = int.MaxValue, maxC = -1, minR = int.MaxValue, maxR = -1;
        for (int r = 0; r < grid.Count; r++)
        {
            for (int c = 0; c < grid[r].Count; c++)
            {
                if (!string.Equals(grid[r][c], slot, StringComparison.OrdinalIgnoreCase)) continue;
                if (c < minC) minC = c;
                if (c > maxC) maxC = c;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
            }
        }

        if (minC == int.MaxValue || minR == int.MaxValue)
            return (1, 1, GridCols, 4);

        int totalSlotsInRow = Math.Max(1, grid[minR].Count);
        int gridColStart = 1 + (int)Math.Round((double)minC * GridCols / totalSlotsInRow);
        int gridColEnd = (int)Math.Round((double)(maxC + 1) * GridCols / totalSlotsInRow);
        int colSpan = Math.Max(1, Math.Min(GridCols - gridColStart + 1, gridColEnd - gridColStart + 1));

        int gridRowStart = 1 + minR * 4;
        int rowSpan = Math.Max(1, (maxR - minR + 1) * 4);

        return (gridColStart, gridRowStart, colSpan, rowSpan);
    }

    private static (int col, int row, int colSpan, int rowSpan) FindRawSlotBounds(
        List<List<string>> grid, string slot)
    {
        int minC = int.MaxValue, maxC = -1, minR = int.MaxValue, maxR = -1;
        for (int r = 0; r < grid.Count; r++)
        {
            for (int c = 0; c < grid[r].Count; c++)
            {
                if (!string.Equals(grid[r][c], slot, StringComparison.OrdinalIgnoreCase)) continue;
                if (c < minC) minC = c;
                if (c > maxC) maxC = c;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
            }
        }

        if (minC == int.MaxValue || minR == int.MaxValue)
            return (0, 0, 1, 1);

        return (minC, minR, maxC - minC + 1, maxR - minR + 1);
    }

    private static (int col, int row, int colSpan, int rowSpan) CalculateAbsoluteChildBounds(
        int containerCol, int containerRow, int containerColSpan, int containerRowSpan,
        List<List<string>> containerGrid, string slot)
    {
        var (sCol, sRow, sColSpan, sRowSpan) = FindRawSlotBounds(containerGrid, slot);
        int gridRows = containerGrid.Count;
        int gridCols = containerGrid[0].Count;

        double colWidth = (double)containerColSpan / gridCols;
        int startCol = containerCol + (int)Math.Round(sCol * colWidth);
        int endCol = containerCol + (int)Math.Round((sCol + sColSpan) * colWidth);
        int colSpan = Math.Max(1, endCol - startCol);

        double rowHeight = (double)containerRowSpan / gridRows;
        int startRow = containerRow + (int)Math.Round(sRow * rowHeight);
        int endRow = containerRow + (int)Math.Round((sRow + sRowSpan) * rowHeight);
        int rowSpan = Math.Max(1, endRow - startRow);

        return (startCol, startRow, colSpan, rowSpan);
    }

    // Naming belongs to the side that writes names into the script. This used to be a third private
    // copy of the rule, and it disagreed with generation on two cases: it kept Unicode letters, and it
    // prefixed only a leading digit rather than anything that is not a letter. A dataset read back
    // under a name generation would never write stops matching the visual SOURCE that refers to it.
    private static string NormalizeDatasetName(string name) =>
        DesignerScriptGenerationService.NormalizeDatasetName(name);

    private static string FormatOverlay(VisualOverlay overlay)
    {
        var parameter = overlay.Parameter?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (overlay.OverlayType == OverlayType.ReferenceLine)
        {
            var props = new List<string>
            {
                $"VALUE = {parameter ?? "0"}"
            };
            if (!string.IsNullOrWhiteSpace(overlay.Label)) props.Add($"LABEL = '{Escape(overlay.Label)}'");
            props.Add($"STYLE = {overlay.LineStyle.ToString().ToUpperInvariant()}");
            if (!string.IsNullOrWhiteSpace(overlay.Color)) props.Add($"COLOR = '{Escape(overlay.Color)}'");
            return $"REFERENCE_LINE ({string.Join(", ", props)})";
        }

        var type = overlay.OverlayType switch
        {
            OverlayType.Goal => $"GOAL({parameter ?? "0"})",
            OverlayType.MovingAvg => $"MOVING_AVG({parameter ?? "1"})",
            OverlayType.Polynomial => $"POLYNOMIAL({parameter ?? "2"})",
            OverlayType.Forecast => $"FORECAST({overlay.ForecastField ?? string.Empty})",
            _ => overlay.OverlayType.ToString().ToUpperInvariant()
        };
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(overlay.ConfidenceLowField)) details.Add($"CONFIDENCE_LOW = {overlay.ConfidenceLowField}");
        if (!string.IsNullOrWhiteSpace(overlay.ConfidenceHighField)) details.Add($"CONFIDENCE_HIGH = {overlay.ConfidenceHighField}");
        if (!string.IsNullOrWhiteSpace(overlay.AnomalyField)) details.Add($"ANOMALY = {overlay.AnomalyField}");
        if (!string.IsNullOrWhiteSpace(overlay.Color)) details.Add($"COLOR = '{Escape(overlay.Color)}'");
        if (!string.IsNullOrWhiteSpace(overlay.Label)) details.Add($"LABEL = '{Escape(overlay.Label)}'");
        return $"{type} AS {overlay.LineStyle.ToString().ToUpperInvariant()}"
            + (details.Count == 0 ? string.Empty : $" WITH ({string.Join(", ", details)})");
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string NormalizeOptionValue(string value) => value.ToUpperInvariant() switch
    {
        "TRUE" => "ON",
        "FALSE" => "OFF",
        _ => value
    };
}
