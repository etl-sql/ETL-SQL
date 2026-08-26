using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>Validates constrained HTML bindings, security policy, budgets, and static embeds.</summary>
public sealed class HtmlVisualAuthoringRule : ILintRule
{
    public string Name => "HtmlVisualAuthoring";
    public string Description => "Validates constrained HTML templates, bindings, budgets, and embedded visual references.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var visuals = script.Statements.OfType<CreateVisualStatement>().ToList();
        var htmlVisuals = visuals.Where(visual => visual.VisualType == VisualType.Html && visual.HtmlTemplate is not null).ToList();
        var declaredParameters = script.Statements.OfType<DeclareStatement>()
            .Select(statement => NormalizeParameter(statement.VariableName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sensitiveParameters = script.Statements.OfType<DeclareStatement>()
            .Where(statement => statement.IsSensitive || statement.IsSecret
                || string.Equals(statement.DataType, "ENCRYPTED", StringComparison.OrdinalIgnoreCase))
            .Select(statement => NormalizeParameter(statement.VariableName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceColumns = CollectSourceColumns(script);

        foreach (var visual in htmlVisuals)
        {
            var definition = visual.HtmlTemplate!;
            foreach (var violation in ConstrainedHtmlPolicy.ValidateTemplate(definition.Template))
                results.Add(Error(visual, "RPT3012", violation.Message));
            foreach (var violation in ConstrainedHtmlPolicy.ValidateCss(definition.Css ?? string.Empty))
                results.Add(Error(visual, "RPT3012", violation.Message));
            foreach (var violation in ConstrainedHtmlPolicy.ValidateEmbedSyntax(definition.Template))
                results.Add(Error(visual, "RPT3010", violation.Message));

            ValidateAuthoredBudgets(visual, definition, results);
            ValidateFallback(visual, definition.Fallback, results);

            var knownColumns = ResolveColumns(visual.Source, sourceColumns);
            foreach (var binding in ConstrainedHtmlPolicy.Bindings(definition.Template)
                .Concat(definition.Fallback is null ? [] : ConstrainedHtmlPolicy.Bindings(definition.Fallback))
                .Concat(ConstrainedHtmlPolicy.EmbeddedParameters(definition.Template))
                .Concat(ConstrainedHtmlPolicy.EmbeddedFields(definition.Template))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (binding.StartsWith('@'))
                {
                    if (!declaredParameters.Contains(NormalizeParameter(binding)))
                        results.Add(Error(visual, "RPT3002", $"HTML visual '{visual.Name}' references undeclared parameter '{binding}'."));
                    else if (sensitiveParameters.Contains(NormalizeParameter(binding)))
                        results.Add(Error(visual, "RPT3014", $"HTML visual '{visual.Name}' cannot disclose sensitive parameter '{binding}'."));
                }
                else if (knownColumns is not null && !knownColumns.Contains(binding))
                {
                    results.Add(Error(visual, "RPT3001", $"HTML visual '{visual.Name}' references unknown source field '{binding}'."));
                }
                else if (knownColumns is null && visual.Source.TempTableName is null)
                {
                    results.Add(Error(visual, "RPT3001", $"Source-free HTML visual '{visual.Name}' cannot reference field '{binding}'."));
                }
            }
        }

        ValidateEmbeds(visuals, htmlVisuals, results);
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private static void ValidateAuthoredBudgets(CreateVisualStatement visual, HtmlTemplateDefinition definition, List<LintResult> results)
    {
        var templateBytes = Encoding.UTF8.GetByteCount(definition.Template);
        if (templateBytes > ConstrainedHtmlPolicy.MaxTemplateBytes)
            results.Add(Error(visual, "RPT3020", $"HTML visual template byte budget exceeded: {templateBytes} > {ConstrainedHtmlPolicy.MaxTemplateBytes}."));
        var cssBytes = Encoding.UTF8.GetByteCount(definition.Css ?? string.Empty);
        if (cssBytes > ConstrainedHtmlPolicy.MaxCssBytes)
            results.Add(Error(visual, "RPT3021", $"HTML visual CSS byte budget exceeded: {cssBytes} > {ConstrainedHtmlPolicy.MaxCssBytes}."));
        var nodes = ConstrainedHtmlPolicy.CountElementNodes(definition.Template);
        if (nodes > ConstrainedHtmlPolicy.MaxTemplateNodes)
            results.Add(Error(visual, "RPT3022", $"HTML visual template node budget exceeded: {nodes} > {ConstrainedHtmlPolicy.MaxTemplateNodes}."));
    }

    private static void ValidateFallback(CreateVisualStatement visual, string? fallback, List<LintResult> results)
    {
        if (fallback is null) return;
        if (Regex.IsMatch(fallback, @"<\/?[A-Za-z]|\{\{\s*(?:#IF|/IF|VISUAL\s*\(|SPARKLINE\s*\(|PROGRESS_BAR\s*\(|BG_CHART\s*\()", RegexOptions.IgnoreCase))
            results.Add(Error(visual, "RPT3013", "HTML visual FALLBACK must be plain text with field or parameter substitutions only."));
    }

    private static void ValidateEmbeds(
        IReadOnlyList<CreateVisualStatement> visuals,
        IReadOnlyList<CreateVisualStatement> htmlVisuals,
        List<LintResult> results)
    {
        var declared = visuals.ToDictionary(visual => visual.Name, StringComparer.OrdinalIgnoreCase);
        var graph = htmlVisuals.ToDictionary(
            visual => visual.Name,
            visual => ConstrainedHtmlPolicy.EmbeddedVisuals(visual.HtmlTemplate!.Template).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (source, targets) in graph)
        {
            var statement = declared[source];
            foreach (var target in targets.Where(target => !declared.ContainsKey(target)))
                results.Add(Error(statement, "RPT3010", $"HTML visual '{source}' embeds missing visual '{target}'."));
        }

        foreach (var visual in htmlVisuals)
        {
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Walk(visual.Name, visual.Name, 0, path, graph, declared, results);
        }
    }

    private static void Walk(
        string root,
        string current,
        int depth,
        HashSet<string> path,
        IReadOnlyDictionary<string, List<string>> graph,
        IReadOnlyDictionary<string, CreateVisualStatement> declared,
        List<LintResult> results)
    {
        if (!path.Add(current))
        {
            results.Add(Error(declared[root], "RPT3010", $"HTML visual embed cycle detected from '{root}' through '{current}'."));
            return;
        }
        if (!graph.TryGetValue(current, out var targets))
        {
            path.Remove(current);
            return;
        }
        foreach (var target in targets.Where(declared.ContainsKey))
        {
            if (path.Contains(target))
            {
                results.Add(Error(declared[root], "RPT3010", $"HTML visual embed cycle detected from '{root}' through '{target}'."));
                continue;
            }
            var nextDepth = depth + 1;
            if (nextDepth > ConstrainedHtmlPolicy.MaxEmbedDepth)
                results.Add(Error(declared[root], "RPT3011", $"HTML visual '{root}' embed depth exceeds {ConstrainedHtmlPolicy.MaxEmbedDepth}."));
            else
                Walk(root, target, nextDepth, path, graph, declared, results);
        }
        path.Remove(current);
    }

    private static Dictionary<string, HashSet<string>> CollectSourceColumns(Script script)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var select in script.Statements.OfType<SelectStatement>().Where(select => select.IntoTable is not null))
            result[select.IntoTable!.TableName] = SelectColumns(select);
        foreach (var dataset in script.Statements.OfType<CreateDatasetStatement>())
            if (dataset.SourceQuery is SelectStatement select)
                result[dataset.TempTableName] = SelectColumns(select);
        return result;
    }

    private static HashSet<string>? ResolveColumns(VisualSourceExpression source, IReadOnlyDictionary<string, HashSet<string>> sources)
    {
        if (source.InlineSelect is SelectStatement inline) return SelectColumns(inline);
        if (source.TempTableName is not null && sources.TryGetValue(source.TempTableName, out var columns)) return columns;
        return null;
    }

    private static HashSet<string> SelectColumns(SelectStatement select) => select.Columns
        .Select(column => column.Alias ?? (column.Expression as IdentifierExpression)?.Name.Split('.').Last())
        .Where(name => !string.IsNullOrWhiteSpace(name) && name != "*")
        .Cast<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeParameter(string name) => name.TrimStart('@');

    private static LintResult Error(CreateVisualStatement visual, string code, string message) => new()
    {
        RuleName = "HtmlVisualAuthoring",
        Code = code,
        Severity = LintSeverity.Error,
        Message = message,
        LineNumber = visual.Line,
        ColumnNumber = visual.Column
    };
}
