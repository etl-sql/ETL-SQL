using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;
using ETL_SQL.ReportPortal.Models;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/designer")]
[Authorize(Roles = "Admin,Publisher")]
public class DesignerController : ControllerBase
{
    private const int GridCols = 12;

    // ── POST /api/designer/parse ──────────────────────────────────────────────

    [HttpPost("parse")]
    public IActionResult Parse([FromBody] ParseDesignerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Script))
            return Ok(new ParseDesignerResponse(EmptyState(), null));

        try
        {
            var tokens = new Lexer(req.Script).Tokenize();
            var ast    = new CoreParser(tokens, req.Script).Parse();
            return Ok(new ParseDesignerResponse(ScriptToState(ast), null));
        }
        catch (Exception ex)
        {
            return Ok(new ParseDesignerResponse(EmptyState(), ex.Message));
        }
    }

    // ── POST /api/designer/generate ───────────────────────────────────────────

    [HttpPost("generate")]
    public IActionResult Generate([FromBody] GenerateDesignerRequest req)
    {
        var script = StateToScript(req.DesignState);
        return Ok(new GenerateDesignerResponse(script));
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    private static DesignerStateDto EmptyState() =>
        new(new List<DesignerPageDto>(), new List<DesignerDatasetDto>());

    private static DesignerStateDto ScriptToState(Script ast)
    {
        // Collect datasets
        var datasets = ast.Statements.OfType<CreateDatasetStatement>()
            .Select((ds, i) => new DesignerDatasetDto(
                $"ds_{i}",
                ds.TempTableName,
                ds.SourceQuery.ToSql().Trim().TrimEnd(';')))
            .ToList();

        // Index visuals by name
        var visuals = ast.Statements.OfType<CreateVisualStatement>()
            .ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

        // Build pages
        var pages = new List<DesignerPageDto>();
        int pageNum = 0;
        foreach (var stmt in ast.Statements.OfType<CreatePageStatement>())
        {
            pageNum++;
            var grid = ParseStructure(stmt.Structure ?? ".");
            var pageVisuals = new List<DesignerVisualDto>();
            int vidx = 0;

            foreach (var (slot, visName) in stmt.SlotMap)
            {
                if (!visuals.TryGetValue(visName, out var vis)) continue;
                var (col, row, colSpan, rowSpan) = FindSlotBounds(grid, slot);
                pageVisuals.Add(VisualToDto(vis, vidx++, col, row, colSpan, rowSpan));
            }

            // Fallback: visuals referenced but not in SlotMap
            if (pageVisuals.Count == 0)
            {
                foreach (var vis in visuals.Values)
                    pageVisuals.Add(VisualToDto(vis, vidx++, 1, vidx, 12, 4));
            }

            pages.Add(new DesignerPageDto(
                $"p{pageNum}",
                stmt.Name,
                stmt.PageMode.ToString(),
                pageVisuals));
        }

        // No pages but visuals exist — create synthetic page
        if (pages.Count == 0 && visuals.Count > 0)
        {
            int idx = 0;
            var synth = visuals.Values.Select(v =>
                VisualToDto(v, idx, 1, ++idx * 4 - 3, 12, 4)).ToList();
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

        var dataset = v.Source.TempTableName;

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
        int minC = int.MaxValue, maxC = 0, minR = int.MaxValue, maxR = 0;
        for (int r = 0; r < grid.Count; r++)
        {
            for (int c = 0; c < grid[r].Count; c++)
            {
                if (!string.Equals(grid[r][c], slot, StringComparison.OrdinalIgnoreCase)) continue;
                if (c + 1 < minC) minC = c + 1;
                if (c + 1 > maxC) maxC = c + 1;
                if (r + 1 < minR) minR = r + 1;
                if (r + 1 > maxR) maxR = r + 1;
            }
        }
        return minC == int.MaxValue
            ? (1, 1, GridCols, 4)
            : (minC, minR, maxC - minC + 1, maxR - minR + 1);
    }

    // ── Generation helpers ────────────────────────────────────────────────────

    private static string StateToScript(DesignerStateDto state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Generated by ETL-SQL Report Designer");
        sb.AppendLine();

        foreach (var ds in state.Datasets ?? [])
        {
            var name  = SanitizeName(ds.Name);
            var query = string.IsNullOrWhiteSpace(ds.Query) ? "SELECT 1 AS Placeholder" : ds.Query.Trim().TrimEnd(';');
            sb.AppendLine($"CREATE DATASET #{name} AS (");
            sb.AppendLine($"  {query}");
            sb.AppendLine($");");
            sb.AppendLine();
        }

        int pageNum = 0;
        foreach (var page in state.Pages ?? [])
        {
            pageNum++;
            var pageName = SanitizeName(string.IsNullOrWhiteSpace(page.Name) ? $"Page{pageNum}" : page.Name);
            var visuals  = page.Visuals ?? [];

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
                sb.AppendLine($"        STRUCTURE = '{EscapeStructure(structure)}',");
                sb.AppendLine($"        MAP (");
                for (int i = 0; i < visuals.Count; i++)
                {
                    var v     = visuals[i];
                    var slot  = SanitizeSlotName(v.Name);
                    var trail = i < visuals.Count - 1 ? "," : "";
                    sb.AppendLine($"            '{slot}' = {SanitizeName(v.Name)}{trail}");
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

    private static string GenerateVisual(DesignerVisualDto v)
    {
        var sb   = new StringBuilder();
        var name = SanitizeName(v.Name);
        var type = v.Type.ToUpper();

        sb.AppendLine($"CREATE VISUAL {name} AS {type} (");

        if (!string.IsNullOrWhiteSpace(v.Title))
            sb.AppendLine($"    TITLE = '{EscapeStr(v.Title)}',");

        if (!string.IsNullOrWhiteSpace(v.Dataset))
            sb.AppendLine($"    SOURCE = #{SanitizeName(v.Dataset)},");

        var mappings = (v.Mappings ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Value))
            .Select(m => $"{m.Key.ToUpper()} = {m.Value}")
            .ToList();
        if (mappings.Count > 0)
            sb.AppendLine($"    MAPPINGS ({string.Join(", ", mappings)}),");

        sb.Append(");");
        return sb.ToString().TrimEnd();
    }

    private static string BuildStructure(IReadOnlyList<DesignerVisualDto> visuals)
    {
        if (visuals.Count == 0) return ".";
        int maxRow = visuals.Max(v => v.GridRow + v.GridRowSpan - 1);
        var grid   = new string[maxRow, GridCols];
        for (int r = 0; r < maxRow; r++)
            for (int c = 0; c < GridCols; c++)
                grid[r, c] = ".";

        foreach (var v in visuals)
        {
            var slot = SanitizeSlotName(v.Name);
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
        if (!char.IsLetter(safe[0])) safe = "v_" + safe;
        return safe;
    }

    private static string SanitizeSlotName(string name) => SanitizeName(name);

    private static string EscapeStr(string s) => s.Replace("'", "''");

    private static string EscapeStructure(string s) => s.Replace("'", "''");
}
