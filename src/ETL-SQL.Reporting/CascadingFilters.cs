using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Reporting;

/// <summary>Compiles Report-SQL cascade declarations into one deterministic dependency graph.</summary>
public static class CascadingFilterGraphCompiler
{
    public static CascadeGraph Compile(IEnumerable<CreateVisualStatement> definitions)
    {
        var visuals = definitions.ToList();
        var producerCandidates = new Dictionary<string, List<CreateVisualStatement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var visual in visuals)
        {
            var produced = ProducedParameter(visual);
            if (produced == null) continue;
            if (!producerCandidates.TryGetValue(produced, out var candidates))
                producerCandidates[produced] = candidates = new List<CreateVisualStatement>();
            candidates.Add(visual);
        }

        var nodes = new List<CascadeNode>();
        foreach (var visual in visuals.Where(v => v.Cascade != null))
        {
            if (visual.VisualType is not (VisualType.Slicer or VisualType.MultiSelect))
                throw new InvalidOperationException($"CASCADE is only valid on SLICER and MULTISELECT visuals ('{visual.Name}').");

            var produced = ProducedParameter(visual)
                ?? throw new InvalidOperationException($"Cascading visual '{visual.Name}' must have exactly one ON_CHANGE SET_PARAMETER action.");
            var parents = visual.Cascade!.Mode == CascadeMode.Local
                ? visual.Cascade.Parents.Select(p => Normalize(p.ParameterName)).ToList()
                : ParameterScanner.Scan(visual.Source.InlineSelect
                    ?? throw new InvalidOperationException($"LIVE cascading visual '{visual.Name}' requires an inline SELECT source."))
                    .Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (visual.Cascade.Mode == CascadeMode.Live && parents.Count == 0)
                throw new InvalidOperationException($"LIVE cascading visual '{visual.Name}' must reference at least one parent parameter in its inline SELECT source.");

            foreach (var parent in parents)
            {
                if (!producerCandidates.ContainsKey(parent))
                    throw new InvalidOperationException($"Cascading visual '{visual.Name}' references {parent}, but no filter visual produces it.");
            }

            nodes.Add(new CascadeNode(visual, produced, parents));
        }

        var participating = nodes.Select(n => n.ProducedParameter)
            .Concat(nodes.SelectMany(n => n.ParentParameters)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in producerCandidates.Where(pair => participating.Contains(pair.Key) && pair.Value.Count > 1))
            throw new InvalidOperationException($"Parameter {pair.Key} is produced by more than one filter visual.");
        var producers = producerCandidates.ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.OrdinalIgnoreCase);

        var nodeByParameter = nodes.ToDictionary(n => n.ProducedParameter, StringComparer.OrdinalIgnoreCase);
        var indegree = nodes.ToDictionary(n => n.ProducedParameter, _ => 0, StringComparer.OrdinalIgnoreCase);
        var children = nodes.ToDictionary(n => n.ProducedParameter, _ => new List<CascadeNode>(), StringComparer.OrdinalIgnoreCase);
        foreach (var child in nodes)
        {
            foreach (var parent in child.ParentParameters)
            {
                if (!nodeByParameter.ContainsKey(parent)) continue;
                indegree[child.ProducedParameter]++;
                children[parent].Add(child);
            }
        }

        var queue = new Queue<CascadeNode>(nodes.Where(n => indegree[n.ProducedParameter] == 0));
        var ordered = new List<CascadeNode>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            ordered.Add(node);
            foreach (var child in children[node.ProducedParameter])
            {
                if (--indegree[child.ProducedParameter] == 0) queue.Enqueue(child);
            }
        }

        if (ordered.Count != nodes.Count)
            throw new InvalidOperationException($"Cycle detected in cascading filter dependencies: {FindCycle(nodes, nodeByParameter)}");

        return new CascadeGraph(ordered, producers);
    }

    public static string? ProducedParameter(CreateVisualStatement visual)
    {
        var actions = visual.Actions.OfType<SetParameterAction>()
            .Where(a => a.Trigger.Equals("ON_CHANGE", StringComparison.OrdinalIgnoreCase)).ToList();
        return actions.Count == 1 ? Normalize(actions[0].ParameterName) : null;
    }

    public static string Normalize(string name) => name.StartsWith('@') ? name : "@" + name;

    private static string FindCycle(List<CascadeNode> nodes, Dictionary<string, CascadeNode> nodeByParameter)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new List<string>();
        string? Walk(string parameter)
        {
            var index = active.FindIndex(p => p.Equals(parameter, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return string.Join(" -> ", active.Skip(index).Append(parameter));
            if (!visited.Add(parameter) || !nodeByParameter.TryGetValue(parameter, out var node)) return null;
            active.Add(parameter);
            foreach (var parent in node.ParentParameters)
            {
                var cycle = Walk(parent);
                if (cycle != null) return cycle;
            }
            active.RemoveAt(active.Count - 1);
            return null;
        }

        foreach (var node in nodes)
        {
            var cycle = Walk(node.ProducedParameter);
            if (cycle != null) return cycle;
        }
        return "unknown";
    }
}

public sealed record CascadeNode(
    CreateVisualStatement Visual,
    string ProducedParameter,
    IReadOnlyList<string> ParentParameters);

public sealed record CascadeGraph(
    IReadOnlyList<CascadeNode> OrderedNodes,
    IReadOnlyDictionary<string, CreateVisualStatement> Producers)
{
    public CascadeGraphManifest ToManifest() => new()
    {
        Order = OrderedNodes.Select(n => n.ProducedParameter).ToList(),
        Edges = OrderedNodes.SelectMany(n => n.ParentParameters.Select(p =>
            new CascadeEdgeManifest(p, n.ProducedParameter))).ToList()
    };
}

/// <summary>Pure LOCAL option-vector filtering and selection reconciliation.</summary>
public static class CascadingFilterState
{
    public static List<List<string?>> FilterRows(
        CascadeVisualManifest cascade,
        IReadOnlyDictionary<string, string> parameters)
    {
        var columns = cascade.SourceColumns ?? [];
        var rows = cascade.SourceRows ?? [];
        if (cascade.MultiSelect.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var valueIndex = cascade.ValueColumn == null
                ? 0
                : columns.FindIndex(c => c.Equals(cascade.ValueColumn, StringComparison.OrdinalIgnoreCase));
            if (valueIndex < 0)
                throw new InvalidOperationException($"Cascade value column '{cascade.ValueColumn}' was not found.");
            var eligible = rows.GroupBy(row => valueIndex < row.Count ? row[valueIndex] : null, StringComparer.OrdinalIgnoreCase)
                .Where(group => cascade.Parents.All(parent => GroupMatchesAll(group, parent, columns, parameters, cascade)))
                .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return rows.Where(row => eligible.Contains(valueIndex < row.Count ? row[valueIndex] : null))
                .Select(CloneRow).ToList();
        }
        return rows.Where(row => cascade.Parents.All(parent =>
        {
            var index = columns.FindIndex(c => c.Equals(parent.Column, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"Cascade parent column '{parent.Column}' was not found.");
            parameters.TryGetValue(CascadingFilterGraphCompiler.Normalize(parent.Parameter), out var selected);
            if (string.Equals(selected, cascade.AllValue, StringComparison.OrdinalIgnoreCase)) return true;
            var selections = ParseSelection(selected, cascade.AllValue);
            var cell = index < row.Count ? row[index] : null;
            if (selections.Count == 0)
                return cascade.Null.Equals("ALL", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(cell);
            return selections.Any(value => Matches(cell, value));
        })).Select(CloneRow).ToList();
    }

    private static bool GroupMatchesAll(
        IEnumerable<List<string?>> rows,
        CascadeParentManifest parent,
        List<string> columns,
        IReadOnlyDictionary<string, string> parameters,
        CascadeVisualManifest cascade)
    {
        var index = columns.FindIndex(c => c.Equals(parent.Column, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException($"Cascade parent column '{parent.Column}' was not found.");
        parameters.TryGetValue(CascadingFilterGraphCompiler.Normalize(parent.Parameter), out var selected);
        if (string.Equals(selected, cascade.AllValue, StringComparison.OrdinalIgnoreCase)) return true;
        var selections = ParseSelection(selected, cascade.AllValue);
        if (selections.Count == 0)
            return cascade.Null.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                || rows.Any(row => index >= row.Count || string.IsNullOrEmpty(row[index]));
        return selections.All(selection => rows.Any(row => index < row.Count && Matches(row[index], selection)));
    }

    public static string Reconcile(CascadeVisualManifest cascade, VisualManifest visual, string? current)
    {
        var valueColumn = visual.Options.TryGetValue("mapping:value", out var mapped) ? mapped : visual.Columns.FirstOrDefault();
        var valueIndex = valueColumn == null ? -1 : visual.Columns.FindIndex(c => c.Equals(valueColumn, StringComparison.OrdinalIgnoreCase));
        var validValues = valueIndex < 0
            ? new List<string>()
            : visual.Rows.Where(r => valueIndex < r.Count && r[valueIndex] != null)
                .Select(r => r[valueIndex]!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var valid = validValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selections = ParseSelection(current, cascade.AllValue);
        if (selections.Count == 0 || selections.All(valid.Contains)) return Canonicalize(selections, visual.VisualType == "MULTISELECT", cascade.AllValue, current);

        return cascade.Invalid.ToUpperInvariant() switch
        {
            "ERROR" => throw new InvalidOperationException($"Selection for {cascade.ProducedParameter} is invalid after its parent filters changed."),
            "FIRST" when validValues.Count > 0 => visual.VisualType == "MULTISELECT"
                ? JsonSerializer.Serialize(new[] { validValues[0] })
                : validValues[0],
            _ => string.Empty
        };
    }

    public static IReadOnlyList<string> ParseSelection(string? value, string allValue)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals(allValue, StringComparison.OrdinalIgnoreCase)) return [];
        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return (JsonSerializer.Deserialize<List<string>>(trimmed) ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (JsonException) { /* legacy input below */ }
        }
        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Canonicalize(IReadOnlyList<string> values, bool multi, string allValue, string? original)
    {
        if (values.Count == 0) return string.IsNullOrWhiteSpace(original) ? string.Empty : allValue;
        return multi ? JsonSerializer.Serialize(values) : values[0];
    }

    private static bool Matches(string? cell, string value) =>
        string.Equals(cell, value, StringComparison.OrdinalIgnoreCase);

    private static List<string?> CloneRow(List<string?> row) => row.ToList();
}
