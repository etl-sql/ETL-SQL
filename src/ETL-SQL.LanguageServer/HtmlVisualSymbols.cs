using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Core;

namespace ETL_SQL.LSP;

/// <summary>Shared symbol lookup for constrained HTML completion, hover, and rename.</summary>
internal static class HtmlVisualSymbols
{
    internal static readonly string[] ThemeTokens =
    [
        "--etl-bg", "--etl-surface", "--etl-border", "--etl-text", "--etl-text-secondary",
        "--etl-accent", "--etl-success", "--etl-warning", "--etl-danger", "--etl-info",
        "--etl-font-family", "--etl-font-mono", "--etl-radius", "--etl-shadow"
    ];

    internal static CreateVisualStatement? ContainingVisual(Script? script, int offset) => script?.Statements
        .OfType<CreateVisualStatement>()
        .FirstOrDefault(visual => visual.VisualType == VisualType.Html
            && offset >= Math.Max(0, visual.StartOffset)
            && (visual.EndOffset <= visual.StartOffset || offset <= visual.EndOffset));

    internal static CreateVisualStatement? ActiveVisual(Script? script, string scriptBefore)
    {
        var match = Regex.Matches(scriptBefore,
            @"(?is)\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?VISUAL\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+AS\s+HTML\s*\(")
            .Cast<Match>().LastOrDefault();
        if (match is null) return null;
        return script?.Statements.OfType<CreateVisualStatement>()
            .LastOrDefault(visual => visual.Name.Equals(match.Groups["name"].Value, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsTemplateBindingContext(string scriptBefore)
    {
        var visualStart = LastHtmlVisualStart(scriptBefore);
        if (visualStart < 0) return false;
        var scope = scriptBefore[visualStart..];
        var template = Regex.Matches(scope, @"(?i)\b(?:TEMPLATE|FALLBACK)\s*=\s*'").Cast<Match>().LastOrDefault();
        if (template is null || !IsInsideSqlString(scope, template.Index + template.Length)) return false;
        var tail = scope[(template.Index + template.Length)..];
        return tail.LastIndexOf("{{", StringComparison.Ordinal) > tail.LastIndexOf("}}", StringComparison.Ordinal);
    }

    internal static bool IsCssContext(string scriptBefore)
    {
        var visualStart = LastHtmlVisualStart(scriptBefore);
        if (visualStart < 0) return false;
        var scope = scriptBefore[visualStart..];
        var css = Regex.Matches(scope, @"(?i)\bCSS\s*=\s*'").Cast<Match>().LastOrDefault();
        return css is not null && IsInsideSqlString(scope, css.Index + css.Length);
    }

    internal static IReadOnlyList<string> Columns(Script script, CreateVisualStatement visual)
    {
        if (visual.Source.InlineSelect is SelectStatement inline) return SelectColumns(inline);
        var source = visual.Source.TempTableName;
        if (source is null) return [];
        var select = script.Statements.OfType<SelectStatement>()
            .LastOrDefault(statement => statement.IntoTable?.TableName.Equals(source, StringComparison.OrdinalIgnoreCase) == true);
        if (select is not null) return SelectColumns(select);
        var dataset = script.Statements.OfType<CreateDatasetStatement>()
            .LastOrDefault(statement => statement.TempTableName.Equals(source, StringComparison.OrdinalIgnoreCase));
        return dataset?.SourceQuery is SelectStatement datasetSelect ? SelectColumns(datasetSelect) : [];
    }

    internal static IReadOnlyList<DeclareStatement> Parameters(Script script) => script.Statements
        .OfType<DeclareStatement>().ToList();

    internal static string? Describe(Script script, CreateVisualStatement visual, string name)
    {
        if (name.StartsWith('@'))
        {
            var parameter = Parameters(script).FirstOrDefault(item =>
                item.VariableName.TrimStart('@').Equals(name.TrimStart('@'), StringComparison.OrdinalIgnoreCase));
            return parameter is null ? null
                : $"**HTML parameter binding** `{name}`\n\nType: `{parameter.DataType}`. Values are HTML-escaped before insertion.";
        }
        return Columns(script, visual).Contains(name, StringComparer.OrdinalIgnoreCase)
            ? $"**HTML field binding** `{name}`\n\nSource: `{visual.Source.ToSql()}`. Values are HTML-escaped before insertion."
            : null;
    }

    internal static List<int> BindingOffsets(string text, CreateVisualStatement visual, string name)
    {
        var start = Math.Max(0, visual.StartOffset);
        var end = visual.EndOffset > start ? Math.Min(text.Length, visual.EndOffset) : text.Length;
        var scope = text[start..end];
        var escaped = Regex.Escape(name.TrimStart('@'));
        var prefix = name.StartsWith('@') ? "@" : string.Empty;
        return Regex.Matches(scope,
                $@"(?ix)\{{\{{\s*(?:\#IF\s+)?(?<name>{Regex.Escape(prefix)}{escaped})\b")
            .Select(match => start + match.Groups["name"].Index).Distinct().Order().ToList();
    }

    private static int LastHtmlVisualStart(string text) => Regex.Matches(text,
            @"(?is)\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?VISUAL\s+[A-Za-z_][A-Za-z0-9_]*\s+AS\s+HTML\s*\(")
        .Cast<Match>().LastOrDefault()?.Index ?? -1;

    private static bool IsInsideSqlString(string scope, int contentStart)
    {
        var quoted = true;
        for (var index = contentStart; index < scope.Length; index++)
        {
            if (scope[index] != '\'') continue;
            if (index + 1 < scope.Length && scope[index + 1] == '\'') { index++; continue; }
            quoted = !quoted;
        }
        return quoted;
    }

    private static IReadOnlyList<string> SelectColumns(SelectStatement select) => select.Columns
        .Select(column => column.Alias ?? (column.Expression as IdentifierExpression)?.Name.Split('.').Last())
        .Where(name => !string.IsNullOrWhiteSpace(name) && name != "*")
        .Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
