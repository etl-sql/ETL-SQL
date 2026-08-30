using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Portal.Models;
using Microsoft.Extensions.DependencyInjection;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Services;

public sealed class DesignerAnalysisService
{
    private const int GridCols = 12;

    public ParseDesignerResponse Parse(string? script, int maxAstStatements)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new ParseDesignerResponse(EmptyState(), null);

        try
        {
            var ast = ParseScript(script);
            ValidateAstLimit(ast, maxAstStatements);

            // The parser recovers from most syntax errors instead of throwing, so an exception is not
            // the only way a script can be broken. Reporting Error == null for a recovered parse handed
            // the caller a design state built from a damaged AST — the canvas would render it, and the
            // "keep the last valid canvas" guard never fired. Gate on the same condition the patcher
            // does, so a script the patcher refuses to touch is a script the canvas refuses to adopt.
            var firstError = ast.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (firstError is not null)
                return new ParseDesignerResponse(EmptyState(), FormatDiagnostic(firstError));

            return new ParseDesignerResponse(ScriptToState(ast, script), null);
        }
        catch (DesignerAstLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ParseDesignerResponse(EmptyState(), ex.Message);
        }
    }

    public async Task<AnalyzeDesignerResponse> AnalyzeAsync(
        string? script,
        string? documentUri,
        int maxAstStatements,
        IServiceProvider? serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new AnalyzeDesignerResponse([]);

        var lines = SplitLines(script);
        var diagnostics = new List<AnalysisDiagnostic>();

        try
        {
            var ast = ParseScript(script);
            ValidateAstLimit(ast, maxAstStatements);
            diagnostics.AddRange(AnalysisDiagnosticBuilder.FromParserDiagnostics(ast.Diagnostics, lines));

            var linter = LinterFactory.CreateWithAllRules(serviceProvider);
            var lintContext = new DefaultLintContext
            {
                DocumentUri = string.IsNullOrWhiteSpace(documentUri) ? "portal-designer" : documentUri!
            };
            var lintResults = await linter.AnalyzeAsync(ast, lintContext);
            diagnostics.AddRange(AnalysisDiagnosticBuilder.FromLintResults(lintResults, lines));

            // Register the temp tables this script declares so the editor's session explorer and
            // autocomplete can see them. Connections are deliberately NOT registered here: in the
            // Portal they come from the ACL-gated shared catalog, never from the script.
            if (serviceProvider?.GetService<IMetadataManager>() is { } metadata)
            {
                var discovery = new ScriptMetadataDiscovery(metadata) { RegisterConnections = false };
                await discovery.DiscoverAsync(ast, lintContext.DocumentUri);
            }
        }
        catch (DesignerAstLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add(AnalysisDiagnosticBuilder.FromException(ex, lines));
        }

        var ordered = diagnostics
            .OrderByDescending(d => d.Severity == DiagnosticSeverity.Error)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartColumn)
            .ToList();
        return new AnalyzeDesignerResponse(ordered);
    }

    private static string FormatDiagnostic(Diagnostic diagnostic) =>
        diagnostic.Line > 0
            ? $"Line {diagnostic.Line}, column {diagnostic.Column}: {diagnostic.Message}"
            : diagnostic.Message;

    private static Script ParseScript(string script)
    {
        var tokens = new Lexer(script).Tokenize();
        return new CoreParser(tokens, script).Parse();
    }

    private static void ValidateAstLimit(Script ast, int maxAstStatements)
    {
        if (ast.Statements.Count > maxAstStatements)
            throw new DesignerAstLimitExceededException(maxAstStatements);
    }

    private static DesignerStateDto EmptyState() =>
        new(new List<DesignerPageDto>(), new List<DesignerDatasetDto>());

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

    private static DesignerStateDto ScriptToState(Script ast, string? script = null)
    {
        var datasets = ast.Statements.OfType<CreateDatasetStatement>()
            .Select((ds, i) => new DesignerDatasetDto(
                $"ds_{i}",
                NormalizeDatasetName(ds.TempTableName),
                ExtractAuthoredNode(script, ds.SourceQuery, ds.SourceQuery.ToSql()).Trim().TrimEnd(';')))
            .ToList();

        var elements = new Dictionary<string, DesignerVisualDto>(StringComparer.OrdinalIgnoreCase);
        int idx = 0;

        var visualsList = ast.Statements.OfType<CreateVisualStatement>().Select(v => (v.Name, Dto: VisualToDto(v, idx++, 1, 1, 12, 4, script))).ToList();
        var containersList = ast.Statements.OfType<CreateContainerStatement>().Select(c => (c.Name, c.SlotMap, Dto: ContainerToDto(c, idx++))).ToList();
        var buttonsList = ast.Statements.OfType<CreateButtonStatement>().Select(b => (b.Name, Dto: ButtonToDto(b, idx++))).ToList();

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

        var pages = new List<DesignerPageDto>();
        int pageNum = 0;
        foreach (var stmt in ast.Statements.OfType<CreatePageStatement>())
        {
            pageNum++;
            var grid = ParseStructure(stmt.Structure ?? ".");
            var pageVisuals = new List<DesignerVisualDto>();
            int vidx = 0;

            foreach (var (slot, elName) in stmt.SlotMap)
            {
                if (!elements.TryGetValue(elName, out var el))
                    continue;

                var (col, row, colSpan, rowSpan) = FindSlotBounds(grid, slot);
                pageVisuals.Add(el with { GridCol = col, GridRow = row, GridColSpan = colSpan, GridRowSpan = rowSpan });
            }

            var containerChildren = new List<DesignerVisualDto>();
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

            pages.Add(new DesignerPageDto(
                $"p{pageNum}",
                stmt.Name,
                stmt.PageMode.ToString(),
                pageVisuals));
        }

        if (pages.Count == 0 && elements.Count > 0)
        {
            int vidx = 0;
            var synth = elements.Values.Select(el =>
                el with { GridCol = 1, GridRow = ++vidx * 4 - 3, GridColSpan = 12, GridRowSpan = 4 }).ToList();
            pages.Add(new DesignerPageDto("p1", "Page 1", "Dashboard", synth));
        }

        return new DesignerStateDto(pages, datasets, null, BookmarksToDto(ast), ParametersToDto(ast));
    }

    private static List<DesignerParameterDto> ParametersToDto(Script ast) =>
        ast.Statements.SelectMany(statement => statement is BlockStatement block
                ? block.Statements.OfType<DeclareStatement>()
                : statement is DeclareStatement declaration ? [declaration] : [])
            .Select(parameter => new DesignerParameterDto(
                parameter.VariableName,
                parameter.DataType,
                parameter.InitialValue?.ToSql(),
                parameter.IsInput,
                parameter.IsOutput,
                parameter.IsRequired,
                parameter.IsSensitive))
            .ToList();

    /// <summary>
    /// Surfaces the script's author bookmarks so the builder can list and edit them. Parameter values
    /// keep their authored source form (<c>'West'</c>, <c>25</c>, <c>TRUE</c>) rather than being
    /// stringified, so editing an unrelated bookmark cannot quietly retype another one's values.
    /// </summary>
    private static List<DesignerBookmarkDto> BookmarksToDto(Script ast) =>
        ast.Statements.OfType<CreateBookmarkStatement>()
            .Select((bookmark, index) => new DesignerBookmarkDto(
                $"bm_{index}",
                bookmark.Name,
                bookmark.Title is LiteralExpression literal
                    ? literal.Value?.ToString()
                    : bookmark.Title?.ToSql().Trim('\''),
                bookmark.PageName,
                bookmark.IsDefault,
                bookmark.Parameters
                    .Select(p => new DesignerBookmarkParameterDto(p.ParameterName, p.Value.ToSql()))
                    .ToList(),
                bookmark.StateEntries
                    .Select(s => new DesignerBookmarkStateDto(
                        s.ObjectName, s.Property.ToString().ToUpperInvariant(), s.On))
                    .ToList()))
            .ToList();

    private static DesignerVisualDto VisualToDto(
        CreateVisualStatement v, int idx, int col, int row, int colSpan, int rowSpan, string? script = null)
    {
        var title = v.Title is LiteralExpression lit
            ? lit.Value?.ToString()
            : v.Title?.ToSql().Trim('\'', '"');

        var sourceName = v.Source.TempTableName;
        var dataset = !string.IsNullOrWhiteSpace(sourceName) && sourceName.StartsWith('&')
            ? NormalizeDatasetName(sourceName)
            : null;

        var mappings = v.Mappings.ToDictionary(
            m => m.Role.ToUpper(),
            m => m.Column,
            StringComparer.OrdinalIgnoreCase);

        var options = v.Options.ToDictionary(
            o => o.Key,
            o => o.Value,
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
            options[style.Key.ToUpper()] = style.Value;
        }

        foreach (var act in v.Actions)
        {
            options[$"action:{act.Trigger.ToUpper()}"] = act.ToSql();
        }
        foreach (var inter in v.Interactions)
        {
            options[$"interaction:{inter.Key.ToUpper()}"] = inter.Value;
        }
        if (v.Cascade != null)
        {
            options["cascade"] = v.Cascade.ToSql();
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

        return new DesignerVisualDto(
            $"v_{v.Name}_{idx}",
            v.Name,
            v.VisualType.ToString().ToUpper(),
            col, row, colSpan, rowSpan,
            title,
            dataset,
            mappings,
            options);
    }

    private static string ExtractAuthoredNode(string? script, AstNode node, string fallback)
    {
        if (script is null || node.StartOffset < 0 || node.EndOffset <= node.StartOffset || node.EndOffset > script.Length)
            return fallback;
        return script[node.StartOffset..node.EndOffset];
    }

    private static DesignerVisualDto ContainerToDto(CreateContainerStatement c, int idx)
    {
        var title = c.Title is LiteralExpression lit
            ? lit.Value?.ToString()
            : c.Title?.ToSql().Trim('\'', '"');
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONTAINER_TYPE"] = c.ContainerType
        };
        return new DesignerVisualDto(
            $"v_{c.Name}_{idx}", c.Name, "CONTAINER",
            1, 1, 12, 4, title, null, new Dictionary<string, string>(), options);
    }

    private static DesignerVisualDto ButtonToDto(CreateButtonStatement b, int idx)
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
        return new DesignerVisualDto(
            $"v_{b.Name}_{idx}", b.Name, "BUTTON",
            1, 1, 12, 4, title, null, new Dictionary<string, string>(), options);
    }

    /// <summary>
    /// Splits a LAYOUT STRUCTURE into rows of slots the same way the runtime page compiler does. Rows
    /// separate on <c>/</c> or a newline: an author who writes the grid across several lines means
    /// several rows, and reading it as one row put every visual in the same cell.
    /// Kept in step with <see cref="ETL_SQL.Reporting.Authoring.DesignerScriptParsingService"/>, which
    /// carries the same logic for the host-neutral path.
    /// </summary>
    private static List<List<string>> ParseStructure(string structure)
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

    private static string NormalizeDatasetName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.StartsWith("&", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal))
            trimmed = trimmed[1..];
        return "&" + SanitizeName(trimmed);
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "visual1";
        var safe = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_]", "_");
        if (!char.IsLetter(safe[0])) safe = "v_" + safe;
        return safe;
    }
}

public sealed class DesignerAstLimitExceededException(int maxStatements) : Exception(
    $"Designer script exceeds the {maxStatements} statement complexity limit.")
{
    public int MaxStatements { get; } = maxStatements;
}
