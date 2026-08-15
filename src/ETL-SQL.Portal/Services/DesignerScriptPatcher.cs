using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Models;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Surgical script patcher for the Visual Report Builder.
/// Updates or inserts report statements (CREATE VISUAL, CREATE PAGE, CREATE DATASET, SET REPORT STYLE)
/// by replacing only their exact character spans in the original script, preserving all preceding data prep SQL,
/// CTEs, variable declarations, and interleaved comments (-- ... and /* ... */).
/// </summary>
public sealed class DesignerScriptPatcher
{
    private readonly DesignerScriptGenerationService _generator;

    public DesignerScriptPatcher(DesignerScriptGenerationService? generator = null)
    {
        _generator = generator ?? new DesignerScriptGenerationService();
    }

    /// <summary>
    /// Performs a full surgical reconciliation of the script against the designer state.
    /// If the script is empty or contains no parseable statements, falls back to full generation.
    /// </summary>
    public string Patch(string? script, DesignerStateDto state)
    {
        if (string.IsNullOrWhiteSpace(script))
            return _generator.Generate(state);

        Script ast;
        try
        {
            var tokens = new Lexer(script).Tokenize();
            ast = new CoreParser(tokens, script).Parse();
        }
        catch
        {
            // If syntax is currently unparseable, return original script to avoid destroying state
            return script;
        }

        if (ast.Statements.Count == 0)
            return _generator.Generate(state);

        var replacements = new List<SpanReplacement>();

        // 1. Reconcile SET REPORT STYLE / THEME
        PatchReportStyleReplacements(script, ast, state.ReportStyle, replacements);

        // 2. Reconcile DATASETS
        PatchDatasetReplacements(script, ast, state.Datasets ?? [], replacements);

        // 3. Reconcile VISUALS, CONTAINERS, and BUTTONS
        var allStateVisuals = (state.Pages ?? []).SelectMany(p => p.Visuals ?? []).ToList();
        PatchVisualElementReplacements(script, ast, state, allStateVisuals, replacements);

        // 4. Reconcile PAGES (STRUCTURE and MAP)
        PatchPageReplacements(script, ast, state.Pages ?? [], replacements);

        // Apply replacements from highest offset to lowest offset
        return ApplyReplacements(script, replacements);
    }

    private static void PatchReportStyleReplacements(
        string script,
        Script ast,
        DesignerReportStyleDto? style,
        List<SpanReplacement> replacements)
    {
        var existingStyleStmt = ast.Statements.OfType<CreateStyleStatement>().FirstOrDefault(s => s.StyleName == null)
            ?? (Statement?)ast.Statements.OfType<SetReportMetadataStatement>().FirstOrDefault(s => s.Key.Equals("THEME", StringComparison.OrdinalIgnoreCase));

        if (style == null)
            return;

        var styleOpts = new List<string>();
        if (!string.IsNullOrWhiteSpace(style.Theme))
            styleOpts.Add($"THEME = '{DesignerScriptGenerationService.EscapeStr(style.Theme)}'");
        if (!string.IsNullOrWhiteSpace(style.Accent))
            styleOpts.Add($"ACCENT = '{DesignerScriptGenerationService.EscapeStr(style.Accent)}'");
        if (!string.IsNullOrWhiteSpace(style.Background))
            styleOpts.Add($"BACKGROUND = '{DesignerScriptGenerationService.EscapeStr(style.Background)}'");
        if (!string.IsNullOrWhiteSpace(style.Surface))
            styleOpts.Add($"SURFACE = '{DesignerScriptGenerationService.EscapeStr(style.Surface)}'");
        if (!string.IsNullOrWhiteSpace(style.Text))
            styleOpts.Add($"TEXT = '{DesignerScriptGenerationService.EscapeStr(style.Text)}'");

        if (styleOpts.Count == 0)
            return;

        var newStyleSql = $"SET REPORT STYLE ({string.Join(", ", styleOpts)});";

        if (existingStyleStmt != null && existingStyleStmt.StartOffset >= 0 && existingStyleStmt.EndOffset > existingStyleStmt.StartOffset)
        {
            replacements.Add(new SpanReplacement(existingStyleStmt.StartOffset, existingStyleStmt.EndOffset, newStyleSql));
        }
        else
        {
            // Insert at top after any header comments/tags
            int insertPos = 0;
            if (ast.Statements.Count > 0 && ast.Statements[0].StartOffset > 0)
            {
                insertPos = 0;
            }
            replacements.Add(new SpanReplacement(insertPos, insertPos, newStyleSql + Environment.NewLine + Environment.NewLine));
        }
    }

    private static void PatchDatasetReplacements(
        string script,
        Script ast,
        IReadOnlyList<DesignerDatasetDto> datasets,
        List<SpanReplacement> replacements)
    {
        var existingDatasets = ast.Statements.OfType<CreateDatasetStatement>().ToList();
        var existingByName = existingDatasets.ToDictionary(
            d => DesignerScriptGenerationService.NormalizeDatasetName(d.TempTableName),
            d => d,
            StringComparer.OrdinalIgnoreCase);

        foreach (var ds in datasets)
        {
            var normalizedName = DesignerScriptGenerationService.NormalizeDatasetName(ds.Name);
            if (existingByName.TryGetValue(normalizedName, out var existingStmt))
            {
                // If query changed, update dataset span
                var existingQuery = existingStmt.SourceQuery.ToSql().Trim().TrimEnd(';');
                var newQuery = string.IsNullOrWhiteSpace(ds.Query) ? "SELECT 1 AS Placeholder" : ds.Query.Trim().TrimEnd(';');

                if (!string.Equals(existingQuery, newQuery, StringComparison.Ordinal))
                {
                    var newDatasetSql = $"CREATE DATASET {normalizedName} AS (\n  {newQuery}\n);";
                    if (existingStmt.StartOffset >= 0 && existingStmt.EndOffset > existingStmt.StartOffset)
                    {
                        replacements.Add(new SpanReplacement(existingStmt.StartOffset, existingStmt.EndOffset, newDatasetSql));
                    }
                }
            }
        }
    }

    private static void PatchVisualElementReplacements(
        string script,
        Script ast,
        DesignerStateDto state,
        IReadOnlyList<DesignerVisualDto> allVisuals,
        List<SpanReplacement> replacements)
    {
        var existingVisualStmts = ast.Statements
            .Where(s => s is CreateVisualStatement or CreateContainerStatement or CreateButtonStatement)
            .ToList();

        var existingMap = new Dictionary<string, Statement>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in existingVisualStmts)
        {
            var name = s switch
            {
                CreateVisualStatement v => v.Name,
                CreateContainerStatement c => c.Name,
                CreateButtonStatement b => b.Name,
                _ => null
            };
            if (name != null)
                existingMap[name] = s;
        }

        var stateVisualNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in allVisuals)
        {
            var vName = DesignerScriptGenerationService.SanitizeName(v.Name, v.Id);
            stateVisualNames.Add(vName);

            var newElementSql = DesignerScriptGenerationService.GenerateElement(v, allVisuals);

            if (existingMap.TryGetValue(vName, out var existingStmt))
            {
                if (existingStmt.StartOffset >= 0 && existingStmt.EndOffset > existingStmt.StartOffset)
                {
                    // Include trailing semicolon if it exists right after EndOffset
                    int endPos = existingStmt.EndOffset;
                    if (endPos < script.Length && script[endPos] == ';')
                        endPos++;

                    replacements.Add(new SpanReplacement(existingStmt.StartOffset, endPos, newElementSql));
                }
            }
            else
            {
                // New visual: insert before the first CREATE PAGE statement, or before EOF
                var firstPage = ast.Statements.OfType<CreatePageStatement>().FirstOrDefault();
                int insertPos = firstPage != null && firstPage.StartOffset >= 0
                    ? firstPage.StartOffset
                    : (existingVisualStmts.Count > 0 ? existingVisualStmts[^1].EndOffset : script.Length);

                replacements.Add(new SpanReplacement(insertPos, insertPos, newElementSql + Environment.NewLine + Environment.NewLine));
            }
        }

        // Handle deleted visuals: remove statements no longer in designer state
        foreach (var (existingName, existingStmt) in existingMap)
        {
            if (!stateVisualNames.Contains(existingName))
            {
                if (existingStmt.StartOffset >= 0 && existingStmt.EndOffset > existingStmt.StartOffset)
                {
                    int startPos = existingStmt.StartOffset;
                    int endPos = existingStmt.EndOffset;
                    if (endPos < script.Length && script[endPos] == ';') endPos++;
                    // Also absorb following whitespace/newlines
                    while (endPos < script.Length && (script[endPos] == '\r' || script[endPos] == '\n'))
                        endPos++;

                    replacements.Add(new SpanReplacement(startPos, endPos, string.Empty));
                }
            }
        }
    }

    private static void PatchPageReplacements(
        string script,
        Script ast,
        IReadOnlyList<DesignerPageDto> pages,
        List<SpanReplacement> replacements)
    {
        var existingPages = ast.Statements.OfType<CreatePageStatement>().ToList();
        int pageNum = 0;

        for (int pIdx = 0; pIdx < pages.Count; pIdx++)
        {
            pageNum++;
            var page = pages[pIdx];
            var pageName = DesignerScriptGenerationService.SanitizeName(string.IsNullOrWhiteSpace(page.Name) ? $"Page{pageNum}" : page.Name);
            var visuals = page.Visuals ?? [];

            var mode = string.Equals(page.Mode, "Paginated", StringComparison.OrdinalIgnoreCase)
                ? "PAGINATED" : "DASHBOARD";

            string newPageSql;
            if (visuals.Count > 0)
            {
                var structure = DesignerScriptGenerationService.BuildStructure(visuals);
                var sb = new StringBuilder();
                sb.AppendLine($"CREATE PAGE [{pageName}] AS {mode} (");
                sb.AppendLine($"    LAYOUT (");
                sb.AppendLine($"        STRUCTURE = '{DesignerScriptGenerationService.EscapeStructure(structure)}',");
                sb.AppendLine($"        MAP (");
                for (int i = 0; i < visuals.Count; i++)
                {
                    var v = visuals[i];
                    var slot = DesignerScriptGenerationService.GetSlotLetter(i);
                    var trail = i < visuals.Count - 1 ? "," : "";
                    sb.AppendLine($"            '{slot}' = {DesignerScriptGenerationService.SanitizeName(v.Name, v.Id)}{trail}");
                }
                sb.AppendLine($"        )");
                sb.AppendLine($"    )");
                sb.Append($");");
                newPageSql = sb.ToString();
            }
            else
            {
                newPageSql = $"CREATE PAGE [{pageName}] AS {mode} ( LAYOUT ( STRUCTURE = '.' ) );";
            }

            CreatePageStatement? existingPageStmt = null;
            if (pIdx < existingPages.Count)
            {
                existingPageStmt = existingPages[pIdx];
            }
            else
            {
                existingPageStmt = existingPages.FirstOrDefault(p => string.Equals(p.Name, pageName, StringComparison.OrdinalIgnoreCase));
            }

            if (existingPageStmt != null && existingPageStmt.StartOffset >= 0 && existingPageStmt.EndOffset > existingPageStmt.StartOffset)
            {
                int endPos = existingPageStmt.EndOffset;
                if (endPos < script.Length && script[endPos] == ';') endPos++;
                replacements.Add(new SpanReplacement(existingPageStmt.StartOffset, endPos, newPageSql));
            }
            else
            {
                // New page: append at bottom of script
                replacements.Add(new SpanReplacement(script.Length, script.Length, Environment.NewLine + Environment.NewLine + newPageSql));
            }
        }

        // Handle deleted pages
        if (existingPages.Count > pages.Count)
        {
            for (int i = pages.Count; i < existingPages.Count; i++)
            {
                var pStmt = existingPages[i];
                if (pStmt.StartOffset >= 0 && pStmt.EndOffset > pStmt.StartOffset)
                {
                    int startPos = pStmt.StartOffset;
                    int endPos = pStmt.EndOffset;
                    if (endPos < script.Length && script[endPos] == ';') endPos++;
                    while (endPos < script.Length && (script[endPos] == '\r' || script[endPos] == '\n'))
                        endPos++;
                    replacements.Add(new SpanReplacement(startPos, endPos, string.Empty));
                }
            }
        }
    }

    private static string ApplyReplacements(string script, List<SpanReplacement> replacements)
    {
        if (replacements.Count == 0)
            return script;

        // Sort descending by StartOffset so replacements from the end do not shift earlier offsets
        var ordered = replacements
            .OrderByDescending(r => r.StartOffset)
            .ThenByDescending(r => r.EndOffset)
            .ToList();

        var sb = new StringBuilder(script);

        int lastProcessedStart = int.MaxValue;
        foreach (var r in ordered)
        {
            int start = Math.Clamp(r.StartOffset, 0, sb.Length);
            int end = Math.Clamp(r.EndOffset, start, sb.Length);

            // Avoid overlapping replacement conflicts
            if (end > lastProcessedStart)
            {
                end = lastProcessedStart;
            }
            if (start > end) start = end;

            sb.Remove(start, end - start);
            sb.Insert(start, r.NewText);

            lastProcessedStart = start;
        }

        return sb.ToString();
    }

    private record SpanReplacement(int StartOffset, int EndOffset, string NewText);
}
