using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.LSP
{
    // ── Interface declarations ────────────────────────────────────────────────

    [Method("etlsql/designerParse", Direction.ClientToServer)]
    public interface IDesignerParseHandler : IJsonRpcRequestHandler<DesignerParseParams, DesignerParseResponse> { }

    [Method("etlsql/designerGenerate", Direction.ClientToServer)]
    public interface IDesignerGenerateHandler : IJsonRpcRequestHandler<DesignerGenerateParams, DesignerGenerateResponse> { }

    // ── Handler ───────────────────────────────────────────────────────────────

    public class DesignerLspHandler(ILogger<DesignerLspHandler> logger) : IDesignerParseHandler, IDesignerGenerateHandler
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        public Task<DesignerParseResponse> Handle(DesignerParseParams request, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.script))
                    return Task.FromResult(new DesignerParseResponse
                    { designStateJson = JsonSerializer.Serialize(EmptyState(), _json) });

                var tokens = new Lexer(request.script).Tokenize();
                var ast = new CoreParser(tokens, request.script).Parse();
                var state = ScriptToState(ast);
                return Task.FromResult(new DesignerParseResponse
                { designStateJson = JsonSerializer.Serialize(state, _json) });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LSP: etlsql/designerParse failed");
                return Task.FromResult(new DesignerParseResponse { error = ex.Message });
            }
        }

        public Task<DesignerGenerateResponse> Handle(DesignerGenerateParams request, CancellationToken ct)
        {
            try
            {
                var state = JsonSerializer.Deserialize<LspDesignState>(request.designStateJson, _json)
                             ?? new LspDesignState(new List<LspDesignPage>(), new List<LspDesignDataset>());
                var script = StateToScript(state);
                return Task.FromResult(new DesignerGenerateResponse { script = script });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LSP: etlsql/designerGenerate failed");
                return Task.FromResult(new DesignerGenerateResponse { script = $"-- Error: {ex.Message}\n" });
            }
        }

        // ── Conversion: Script → DesignState ──────────────────────────────────

        private const int GridCols = 12;

        private static LspDesignState EmptyState() =>
            new(new List<LspDesignPage>(), new List<LspDesignDataset>());

        private static LspDesignState ScriptToState(Script ast)
        {
            var datasets = ast.Statements.OfType<CreateDatasetStatement>()
                .Select((ds, i) => new LspDesignDataset(
                    $"ds_{i}", ds.TempTableName,
                    ds.SourceQuery.ToSql().Trim().TrimEnd(';')))
                .ToList();

            var visuals = ast.Statements.OfType<CreateVisualStatement>()
                .ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

            var pages = new List<LspDesignPage>();
            int pageNum = 0;
            foreach (var stmt in ast.Statements.OfType<CreatePageStatement>())
            {
                pageNum++;
                var grid = ParseStructure(stmt.Structure ?? ".");
                var pageVisuals = new List<LspDesignVisual>();
                int vidx = 0;
                foreach (var (slot, visName) in stmt.SlotMap)
                {
                    if (!visuals.TryGetValue(visName, out var vis)) continue;
                    var (col, row, colSpan, rowSpan) = FindSlotBounds(grid, slot);
                    pageVisuals.Add(VisualToDto(vis, vidx++, col, row, colSpan, rowSpan));
                }
                if (pageVisuals.Count == 0)
                    foreach (var vis in visuals.Values)
                        pageVisuals.Add(VisualToDto(vis, vidx++, 1, vidx * 4 - 3, 12, 4));

                pages.Add(new LspDesignPage($"p{pageNum}", stmt.Name, stmt.PageMode.ToString(), pageVisuals));
            }

            if (pages.Count == 0 && visuals.Count > 0)
            {
                int idx = 0;
                var synth = visuals.Values.Select(v => VisualToDto(v, idx, 1, ++idx * 4 - 3, 12, 4)).ToList();
                pages.Add(new LspDesignPage("p1", "Page 1", "Dashboard", synth));
            }

            return new LspDesignState(pages, datasets);
        }

        private static LspDesignVisual VisualToDto(
            CreateVisualStatement v, int idx, int col, int row, int colSpan, int rowSpan)
        {
            var title = v.Title is LiteralExpression lit
                ? lit.Value?.ToString()
                : v.Title?.ToSql().Trim('\'', '"');
            var mappings = v.Mappings.ToDictionary(
                m => m.Role.ToUpper(), m => m.Column, StringComparer.OrdinalIgnoreCase);
            var options = v.Options.ToDictionary(
                o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
            return new LspDesignVisual(
                $"v_{v.Name}_{idx}", v.Name, v.VisualType.ToString().ToUpper(),
                col, row, colSpan, rowSpan, title, v.Source.TempTableName, mappings, options);
        }

        private static List<List<string>> ParseStructure(string structure) =>
            structure.Split('/', StringSplitOptions.TrimEntries)
                .Select(r => r
                    .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim('"', '\'', '[', ']'))
                    .ToList())
                .ToList();

        private static (int col, int row, int colSpan, int rowSpan) FindSlotBounds(
            List<List<string>> grid, string slot)
        {
            int minC = int.MaxValue, maxC = 0, minR = int.MaxValue, maxR = 0;
            for (int r = 0; r < grid.Count; r++)
                for (int c = 0; c < grid[r].Count; c++)
                    if (string.Equals(grid[r][c], slot, StringComparison.OrdinalIgnoreCase))
                    {
                        if (c + 1 < minC) minC = c + 1;
                        if (c + 1 > maxC) maxC = c + 1;
                        if (r + 1 < minR) minR = r + 1;
                        if (r + 1 > maxR) maxR = r + 1;
                    }
            return minC == int.MaxValue ? (1, 1, GridCols, 4) : (minC, minR, maxC - minC + 1, maxR - minR + 1);
        }

        // ── Conversion: DesignState → Script ──────────────────────────────────

        private static string StateToScript(LspDesignState state)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Generated by ETL-SQL Report Designer");
            sb.AppendLine();

            foreach (var ds in state.Datasets ?? new List<LspDesignDataset>())
            {
                var name = SanitizeName(ds.Name);
                var query = string.IsNullOrWhiteSpace(ds.Query) ? "SELECT 1 AS Placeholder" : ds.Query.Trim().TrimEnd(';');
                sb.AppendLine($"CREATE DATASET #{name} AS (");
                sb.AppendLine($"  {query}");
                sb.AppendLine($");");
                sb.AppendLine();
            }

            int pageNum = 0;
            foreach (var page in state.Pages ?? new List<LspDesignPage>())
            {
                pageNum++;
                var pageName = SanitizeName(
                    string.IsNullOrWhiteSpace(page.Name) ? $"Page{pageNum}" : page.Name);
                var visuals = page.Visuals ?? new List<LspDesignVisual>();

                foreach (var v in visuals)
                {
                    sb.AppendLine(GenerateVisual(v));
                    sb.AppendLine();
                }

                var mode = string.Equals(page.Mode, "Paginated", StringComparison.OrdinalIgnoreCase)
                    ? "PAGINATED" : "DASHBOARD";

                if (visuals.Count > 0)
                {
                    var structure = BuildStructure(visuals);
                    sb.AppendLine($"CREATE PAGE [{pageName}] AS {mode} (");
                    sb.AppendLine($"    LAYOUT (");
                    sb.AppendLine($"        STRUCTURE = '{EscapeStr(structure)}',");
                    sb.AppendLine($"        MAP (");
                    for (int i = 0; i < visuals.Count; i++)
                    {
                        var slot = SanitizeName(visuals[i].Name);
                        var trail = i < visuals.Count - 1 ? "," : "";
                        sb.AppendLine($"            '{slot}' = {SanitizeName(visuals[i].Name)}{trail}");
                    }
                    sb.AppendLine($"        )");
                    sb.AppendLine($"    )");
                    sb.AppendLine($");");
                }
                else
                {
                    sb.AppendLine($"CREATE PAGE [{pageName}] AS {mode} ( LAYOUT ( STRUCTURE = '.' ) );");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string GenerateVisual(LspDesignVisual v)
        {
            var sb = new StringBuilder();
            var name = SanitizeName(v.Name);
            sb.AppendLine($"CREATE VISUAL {name} AS {v.Type.ToUpper()} (");
            if (!string.IsNullOrWhiteSpace(v.Title))
                sb.AppendLine($"    TITLE = '{EscapeStr(v.Title)}',");
            if (!string.IsNullOrWhiteSpace(v.Dataset))
                sb.AppendLine($"    SOURCE = #{SanitizeName(v.Dataset)},");
            var mappings = (v.Mappings ?? new Dictionary<string, string>())
                .Where(m => !string.IsNullOrWhiteSpace(m.Value))
                .Select(m => $"{m.Key.ToUpper()} = {m.Value}")
                .ToList();
            if (mappings.Count > 0)
                sb.AppendLine($"    MAPPINGS ({string.Join(", ", mappings)}),");
            sb.Append(");");
            return sb.ToString().TrimEnd();
        }

        private static string BuildStructure(IReadOnlyList<LspDesignVisual> visuals)
        {
            if (visuals.Count == 0) return ".";
            int maxRow = visuals.Max(v => v.GridRow + v.GridRowSpan - 1);
            var grid = new string[maxRow, GridCols];
            for (int r = 0; r < maxRow; r++)
                for (int c = 0; c < GridCols; c++)
                    grid[r, c] = ".";
            foreach (var v in visuals)
            {
                var slot = SanitizeName(v.Name);
                for (int r = v.GridRow - 1; r < v.GridRow - 1 + v.GridRowSpan && r < maxRow; r++)
                    for (int c = v.GridCol - 1; c < v.GridCol - 1 + v.GridColSpan && c < GridCols; c++)
                        grid[r, c] = slot;
            }
            var rows = Enumerable.Range(0, maxRow)
                .Select(r => string.Join(" ", Enumerable.Range(0, GridCols).Select(c => grid[r, c])));
            return string.Join(" / ", rows);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "visual1";
            var safe = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_]", "_");
            return !char.IsLetter(safe[0]) ? "v_" + safe : safe;
        }

        private static string EscapeStr(string s) => s.Replace("'", "''");
    }

    // ── Local DTOs (parallel to ReportPortal.Models, camelCase JSON output) ──

    record LspDesignState(List<LspDesignPage> Pages, List<LspDesignDataset> Datasets);
    record LspDesignPage(string Id, string Name, string Mode, List<LspDesignVisual> Visuals);
    record LspDesignVisual(
        string Id, string Name, string Type,
        int GridCol, int GridRow, int GridColSpan, int GridRowSpan,
        string? Title, string? Dataset,
        Dictionary<string, string> Mappings,
        Dictionary<string, string> Options);
    record LspDesignDataset(string Id, string Name, string Query);
}
