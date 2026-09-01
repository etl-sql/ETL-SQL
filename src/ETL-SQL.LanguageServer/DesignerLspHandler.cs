using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting.Authoring;
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

    [Method("etlsql/scriptDag", Direction.ClientToServer)]
    public interface IScriptDagHandler : IJsonRpcRequestHandler<ScriptDagParams, ScriptDagResponse> { }

    // ── Handler ───────────────────────────────────────────────────────────────

    public class DesignerLspHandler(ILogger<DesignerLspHandler> logger)
        : IDesignerParseHandler, IDesignerGenerateHandler, IScriptDagHandler
    {
        private readonly ScriptDagProjectionService _scriptDagProjection = new();
        private readonly DesignerScriptPatcher _designerScriptPatcher = new();

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
                var state = ScriptToState(ast, request.script);
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
                var script = _designerScriptPatcher.Patch(request.script, ToAuthoringState(state));
                return Task.FromResult(new DesignerGenerateResponse { script = script });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LSP: etlsql/designerGenerate failed");
                return Task.FromResult(new DesignerGenerateResponse { script = $"-- Error: {ex.Message}\n" });
            }
        }

        /// <summary>
        /// Builds the read-only pipeline diagram for the VS Code Visual Flow panel, using the same
        /// <see cref="ScriptDagBuilder"/> the Portal's Orchestrator job view renders.
        /// </summary>
        public Task<ScriptDagResponse> Handle(ScriptDagParams request, CancellationToken ct)
        {
            try
            {
                var projected = _scriptDagProjection.Project(request.script);
                if (!projected.Parsed)
                    return Task.FromResult(new ScriptDagResponse { error = projected.Error });

                return Task.FromResult(new ScriptDagResponse
                {
                    nodes = projected.Dag.Nodes
                        .Select(n => new ScriptDagNodeDto
                        {
                            id = n.Id,
                            label = n.Label,
                            type = n.Type,
                            line = GetLine(n.Meta)
                        })
                        .ToList(),
                    edges = projected.Dag.Edges
                        .Select(e => new ScriptDagEdgeDto { source = e.Source, target = e.Target })
                        .ToList(),
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LSP: etlsql/scriptDag failed");
                return Task.FromResult(new ScriptDagResponse { error = ex.Message });
            }
        }

        private static int GetLine(object? meta)
        {
            if (meta == null) return 0;
            var property = meta.GetType().GetProperty("line") ?? meta.GetType().GetProperty("Line");
            return property?.GetValue(meta) is int line ? line : 0;
        }

        private static DesignerAuthoringState ToAuthoringState(LspDesignState state) => new(
            (state.Pages ?? []).Select(page => new DesignerAuthoringPage(
                page.Id,
                page.Name,
                page.Mode,
                (page.Visuals ?? []).Select(visual => new DesignerAuthoringVisual(
                    visual.Id,
                    visual.Name,
                    visual.Type,
                    visual.GridCol,
                    visual.GridRow,
                    visual.GridColSpan,
                    visual.GridRowSpan,
                    visual.Title,
                    visual.Dataset,
                    visual.Mappings ?? new Dictionary<string, string>(),
                    visual.Options ?? new Dictionary<string, string>())).ToList())).ToList(),
            (state.Datasets ?? []).Select(dataset => new DesignerAuthoringDataset(
                dataset.Id,
                dataset.Name,
                dataset.Query)).ToList());

        // ── Conversion: Script → DesignState ──────────────────────────────────

        private const int GridCols = 12;

        /// <summary>The node's own source slice, falling back to a re-serialisation when the parser
        /// did not record usable offsets.</summary>
        private static string AuthoredText(string? script, AstNode node, string fallback) =>
            script is null || node.StartOffset < 0 || node.EndOffset <= node.StartOffset || node.EndOffset > script.Length
                ? fallback
                : script[node.StartOffset..node.EndOffset];

        private static LspDesignState EmptyState() =>
            new(new List<LspDesignPage>(), new List<LspDesignDataset>());

        private static LspDesignState ScriptToState(Script ast, string? script)
        {
            // The authored CREATE CONNECTION statements, so a surface that has to reproduce the
            // script's connection context asks the parser rather than scanning the buffer.
            var connections = ast.Statements.OfType<CreateConnectionStatement>()
                .Select(c => new LspDesignConnection(
                    c.ConnectionName, AuthoredText(script, c, c.ToSql()).Trim()))
                .ToList();

            var datasets = ast.Statements.OfType<CreateDatasetStatement>()
                .Select((ds, i) => new LspDesignDataset(
                    $"ds_{i}", NormalizeDatasetName(ds.TempTableName),
                    ds.SourceQuery.ToSql().Trim().TrimEnd(';')))
                .ToList();

            var elements = new Dictionary<string, LspDesignVisual>(StringComparer.OrdinalIgnoreCase);
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

            var pages = new List<LspDesignPage>();
            int pageNum = 0;
            foreach (var stmt in ast.Statements.OfType<CreatePageStatement>())
            {
                pageNum++;
                var grid = ParseStructure(stmt.Structure ?? ".");
                var pageVisuals = new List<LspDesignVisual>();
                int vidx = 0;
                foreach (var (slot, elName) in stmt.SlotMap)
                {
                    if (!elements.TryGetValue(elName, out var el)) continue;
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

                pages.Add(new LspDesignPage($"p{pageNum}", stmt.Name, stmt.PageMode.ToString(), pageVisuals));
            }

            if (pages.Count == 0 && elements.Count > 0)
            {
                int vidx = 0;
                var synth = elements.Values.Select(el =>
                    el with { GridCol = 1, GridRow = ++vidx * 4 - 3, GridColSpan = 12, GridRowSpan = 4 }).ToList();
                pages.Add(new LspDesignPage("p1", "Page 1", "Dashboard", synth));
            }

            return new LspDesignState(pages, datasets, connections);
        }

        private static LspDesignVisual VisualToDto(
            CreateVisualStatement v, int idx, int col, int row, int colSpan, int rowSpan)
        {
            var title = v.Title is LiteralExpression lit
                ? lit.Value?.ToString()
                : v.Title?.ToSql().Trim('\'', '"');
            var dataset = string.IsNullOrWhiteSpace(v.Source.TempTableName) ? null : NormalizeDatasetName(v.Source.TempTableName);
            var mappings = v.Mappings.ToDictionary(
                m => m.Role.ToUpper(), m => m.Column, StringComparer.OrdinalIgnoreCase);
            var options = v.Options.ToDictionary(
                o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
            return new LspDesignVisual(
                $"v_{v.Name}_{idx}", v.Name, v.VisualType.ToString().ToUpper(),
                col, row, colSpan, rowSpan, title, dataset, mappings, options);
        }

        private static LspDesignVisual ContainerToDto(CreateContainerStatement c, int idx)
        {
            var title = c.Title is LiteralExpression lit
                ? lit.Value?.ToString()
                : c.Title?.ToSql().Trim('\'', '"');
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CONTAINER_TYPE"] = c.ContainerType
            };
            return new LspDesignVisual(
                $"v_{c.Name}_{idx}", c.Name, "CONTAINER",
                1, 1, 12, 4, title, null, new Dictionary<string, string>(), options);
        }

        private static LspDesignVisual ButtonToDto(CreateButtonStatement b, int idx)
        {
            var title = b.Title is LiteralExpression lit
                ? lit.Value?.ToString()
                : b.Title?.ToSql().Trim('\'', '"');
            var options = b.Options.ToDictionary(
                o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
            if (!options.ContainsKey("BUTTON_TYPE"))
            {
                options["BUTTON_TYPE"] = "REFRESH";
            }
            return new LspDesignVisual(
                $"v_{b.Name}_{idx}", b.Name, "BUTTON",
                1, 1, 12, 4, title, null, new Dictionary<string, string>(), options);
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

        private static string NormalizeDatasetName(string name)
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.StartsWith("&", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal))
                trimmed = trimmed[1..];
            return "&" + SanitizeName(trimmed);
        }

        private static string SanitizeName(string name, string? fallback = null)
        {
            var input = string.IsNullOrWhiteSpace(name) ? fallback : name;
            if (string.IsNullOrWhiteSpace(input)) return "visual1";
            var safe = Regex.Replace(input.Trim(), @"[^a-zA-Z0-9_]", "_");
            return !char.IsLetter(safe[0]) ? "v_" + safe : safe;
        }
    }

    // ── Local DTOs (parallel to Portal.Models, camelCase JSON output) ──

    record LspDesignState(
        List<LspDesignPage> Pages,
        List<LspDesignDataset> Datasets,
        List<LspDesignConnection>? Connections = null);
    /// <summary><c>Text</c> is the authored CREATE CONNECTION statement, exactly as written.</summary>
    record LspDesignConnection(string Name, string Text);
    record LspDesignPage(string Id, string Name, string Mode, List<LspDesignVisual> Visuals);
    record LspDesignVisual(
        string Id, string Name, string Type,
        int GridCol, int GridRow, int GridColSpan, int GridRowSpan,
        string? Title, string? Dataset,
        Dictionary<string, string> Mappings,
        Dictionary<string, string> Options);
    record LspDesignDataset(string Id, string Name, string Query);
}
