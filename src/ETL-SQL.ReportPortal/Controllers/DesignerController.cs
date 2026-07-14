using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportPortal.Filters;
using ETL_SQL.ReportPortal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/designer")]
[Authorize(Roles = "Admin,Publisher")]
[RequirePortalModule("Designer")]
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
            var ast = new CoreParser(tokens, req.Script).Parse();
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
        if (req.DesignState.Pages == null || req.DesignState.Pages.Count == 0)
            return BadRequest(new { Error = "Design state must contain at least one page." });
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

        // Build pages
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
                if (!elements.TryGetValue(elName, out var el)) continue;
                var (col, row, colSpan, rowSpan) = FindSlotBounds(grid, slot);
                pageVisuals.Add(el with { GridCol = col, GridRow = row, GridColSpan = colSpan, GridRowSpan = rowSpan });
            }

            // Fallback: visuals referenced but not in SlotMap
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

        // No pages but visuals exist — create synthetic page
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
            var name = NormalizeDatasetName(ds.Name);
            var query = string.IsNullOrWhiteSpace(ds.Query) ? "SELECT 1 AS Placeholder" : ds.Query.Trim().TrimEnd(';');
            sb.AppendLine($"CREATE DATASET {name} AS (");
            sb.AppendLine($"  {query}");
            sb.AppendLine($");");
            sb.AppendLine();
        }

        int pageNum = 0;
        foreach (var page in state.Pages ?? [])
        {
            pageNum++;
            var pageName = SanitizeName(string.IsNullOrWhiteSpace(page.Name) ? $"Page{pageNum}" : page.Name);
            var visuals = page.Visuals ?? [];

            foreach (var v in visuals)
            {
                sb.AppendLine(GenerateElement(v));
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
                    var v = visuals[i];
                    var slot = SanitizeSlotName(v.Name, v.Id);
                    var trail = i < visuals.Count - 1 ? "," : "";
                    sb.AppendLine($"            '{slot}' = {SanitizeName(v.Name, v.Id)}{trail}");
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

    private static string GenerateElement(DesignerVisualDto v)
    {
        var sb = new StringBuilder();
        var name = SanitizeName(v.Name, v.Id);
        if (string.Equals(v.Type, "CONTAINER", StringComparison.OrdinalIgnoreCase))
        {
            var containerType = v.Options.TryGetValue("CONTAINER_TYPE", out var ct) ? ct : "BOX";
            sb.AppendLine($"CREATE CONTAINER {name} AS {containerType.ToUpper()} (");
            if (!string.IsNullOrWhiteSpace(v.Title))
                sb.AppendLine($"    TITLE = '{EscapeStr(v.Title)}',");
            sb.Append(");");
        }
        else if (string.Equals(v.Type, "BUTTON", StringComparison.OrdinalIgnoreCase))
        {
            var buttonType = v.Options.TryGetValue("BUTTON_TYPE", out var bt) ? bt : "REFRESH";
            sb.AppendLine($"CREATE BUTTON {name} AS (");
            if (!string.IsNullOrWhiteSpace(v.Title))
                sb.AppendLine($"    TITLE = '{EscapeStr(v.Title)}',");
            sb.AppendLine($"    OPTIONS (BUTTON_TYPE = '{buttonType}'),");
            sb.Append(");");
        }
        else
        {
            sb.AppendLine($"CREATE VISUAL {name} AS {v.Type.ToUpper()} (");
            if (!string.IsNullOrWhiteSpace(v.Title))
                sb.AppendLine($"    TITLE = '{EscapeStr(v.Title)}',");
            if (!string.IsNullOrWhiteSpace(v.Dataset))
                sb.AppendLine($"    SOURCE = {NormalizeDatasetName(v.Dataset)},");
            var mappings = (v.Mappings ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m.Value))
                .Select(m => $"{m.Key.ToUpper()} = {m.Value}")
                .ToList();
            if (mappings.Count > 0)
                sb.AppendLine($"    MAPPINGS ({string.Join(", ", mappings)}),");
            sb.Append(");");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildStructure(IReadOnlyList<DesignerVisualDto> visuals)
    {
        if (visuals.Count == 0) return ".";
        int maxRow = visuals.Max(v => v.GridRow + v.GridRowSpan - 1);
        // Use only as many columns as the visuals actually occupy, not the full 12-column grid.
        // This avoids trailing rows of ". . . . . . . ." that appear when visuals span fewer columns.
        int usedCols = Math.Min(GridCols, visuals.Max(v => v.GridCol + v.GridColSpan - 1));
        var grid = new string[maxRow, usedCols];
        for (int r = 0; r < maxRow; r++)
            for (int c = 0; c < usedCols; c++)
                grid[r, c] = ".";

        foreach (var v in visuals)
        {
            var slot = SanitizeSlotName(v.Name, v.Id);
            for (int r = v.GridRow - 1; r < v.GridRow - 1 + v.GridRowSpan && r < maxRow; r++)
                for (int c = v.GridCol - 1; c < v.GridCol - 1 + v.GridColSpan && c < usedCols; c++)
                    grid[r, c] = slot;
        }

        var rows = Enumerable.Range(0, maxRow)
            .Select(r => string.Join(" ", Enumerable.Range(0, usedCols).Select(c => grid[r, c])));
        return string.Join(" / ", rows);
    }

    private static string SanitizeName(string name, string? fallback = null)
    {
        var input = string.IsNullOrWhiteSpace(name) ? fallback : name;
        if (string.IsNullOrWhiteSpace(input)) return "visual1";
        var safe = Regex.Replace(input.Trim(), @"[^a-zA-Z0-9_]", "_");
        if (!char.IsLetter(safe[0])) safe = "v_" + safe;
        return safe;
    }

    private static string NormalizeDatasetName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.StartsWith("&", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal))
            trimmed = trimmed[1..];
        return "&" + SanitizeName(trimmed);
    }

    private static string SanitizeSlotName(string name, string fallback) => SanitizeName(name, fallback);

    private static string EscapeStr(string s) => s.Replace("'", "''");

    private static string EscapeStructure(string s) => s.Replace("'", "''");
}
