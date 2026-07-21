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
            return new ParseDesignerResponse(ScriptToState(ast), null);
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

    private static DesignerStateDto ScriptToState(Script ast)
    {
        var datasets = ast.Statements.OfType<CreateDatasetStatement>()
            .Select((ds, i) => new DesignerDatasetDto(
                $"ds_{i}",
                NormalizeDatasetName(ds.TempTableName),
                ds.SourceQuery.ToSql().Trim().TrimEnd(';')))
            .ToList();

        var elements = new Dictionary<string, DesignerVisualDto>(StringComparer.OrdinalIgnoreCase);
        int idx = 0;
        foreach (var v in ast.Statements.OfType<CreateVisualStatement>())
        {
            elements[v.Name] = VisualToDto(v, idx++, 1, 1, 12, 4);
        }
        foreach (var c in ast.Statements.OfType<CreateContainerStatement>())
        {
            elements[c.Name] = ContainerToDto(c, idx++);
        }
        foreach (var b in ast.Statements.OfType<CreateButtonStatement>())
        {
            elements[b.Name] = ButtonToDto(b, idx++);
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

        return new DesignerStateDto(pages, datasets);
    }

    private static DesignerVisualDto VisualToDto(
        CreateVisualStatement v, int idx, int col, int row, int colSpan, int rowSpan)
    {
        var title = v.Title is LiteralExpression lit
            ? lit.Value?.ToString()
            : v.Title?.ToSql().Trim('\'', '"');

        var dataset = string.IsNullOrWhiteSpace(v.Source.TempTableName) ? null : NormalizeDatasetName(v.Source.TempTableName);

        var mappings = v.Mappings.ToDictionary(
            m => m.Role.ToUpper(),
            m => m.Column,
            StringComparer.OrdinalIgnoreCase);

        var options = v.Options.ToDictionary(
            o => o.Key,
            o => o.Value,
            StringComparer.OrdinalIgnoreCase);

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

    private static List<List<string>> ParseStructure(string structure)
    {
        var rows = structure.Split('/', StringSplitOptions.TrimEntries);
        return rows.Select(r =>
            r.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
             .Select(s => s.Trim('"', '\'', '[', ']'))
             .ToList()
        ).ToList();
    }

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
