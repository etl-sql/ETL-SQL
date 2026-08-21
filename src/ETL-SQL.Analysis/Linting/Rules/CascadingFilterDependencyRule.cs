using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>Reports invalid cascade producers, parent references, and cycles while authoring.</summary>
public sealed class CascadingFilterDependencyRule : ILintRule
{
    public string Name => "CascadingFilterDependency";
    public string Description => "Validates cascading filter producers and their acyclic dependency graph.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var visuals = script.Statements.OfType<CreateVisualStatement>().ToList();
        var producerCandidates = new Dictionary<string, List<CreateVisualStatement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var visual in visuals)
        {
            var actions = visual.Actions.OfType<SetParameterAction>()
                .Where(a => a.Trigger.Equals("ON_CHANGE", StringComparison.OrdinalIgnoreCase)).ToList();
            if (actions.Count != 1) continue;
            var parameter = Normalize(actions[0].ParameterName);
            if (!producerCandidates.TryGetValue(parameter, out var candidates))
                producerCandidates[parameter] = candidates = new List<CreateVisualStatement>();
            candidates.Add(visual);
        }

        var dependencies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var visual in visuals.Where(v => v.Cascade != null))
        {
            var produced = visual.Actions.OfType<SetParameterAction>()
                .Where(a => a.Trigger.Equals("ON_CHANGE", StringComparison.OrdinalIgnoreCase)).ToList();
            if (produced.Count != 1)
            {
                Add(results, visual, $"Cascading visual '{visual.Name}' must have exactly one ON_CHANGE SET_PARAMETER action.");
                continue;
            }
            if (visual.VisualType is not (VisualType.Slicer or VisualType.MultiSelect))
                Add(results, visual, $"CASCADE is only valid on SLICER and MULTISELECT visuals ('{visual.Name}').");

            var parameter = Normalize(produced[0].ParameterName);
            var parents = visual.Cascade!.Mode == CascadeMode.Local
                ? visual.Cascade.Parents.Select(p => Normalize(p.ParameterName)).ToList()
                : visual.Source.InlineSelect is { } inlineSelect
                    ? ParameterScanner.Scan(inlineSelect).Select(Normalize)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<string>();
            dependencies[parameter] = parents;
            if (visual.Cascade.Mode == CascadeMode.Live && parents.Count == 0)
                Add(results, visual, $"LIVE cascading visual '{visual.Name}' must reference at least one parent parameter in its inline SELECT source.");
            foreach (var parent in parents.Where(parent => !producerCandidates.ContainsKey(parent)))
                Add(results, visual, $"Cascading visual '{visual.Name}' references {parent}, but no filter visual produces it.");
        }

        var participating = dependencies.Keys.Concat(dependencies.Values.SelectMany(p => p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in producerCandidates.Where(pair => participating.Contains(pair.Key) && pair.Value.Count > 1))
            Add(results, pair.Value[1], $"Parameter {pair.Key} is produced by more than one filter visual.");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new List<string>();
        string? Walk(string parameter)
        {
            var index = active.FindIndex(p => p.Equals(parameter, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return string.Join(" -> ", active.Skip(index).Append(parameter));
            if (!visited.Add(parameter) || !dependencies.TryGetValue(parameter, out var parents)) return null;
            active.Add(parameter);
            foreach (var parent in parents)
            {
                var cycle = Walk(parent);
                if (cycle != null) return cycle;
            }
            active.RemoveAt(active.Count - 1);
            return null;
        }
        foreach (var parameter in dependencies.Keys)
        {
            var cycle = Walk(parameter);
            if (cycle == null) continue;
            Add(results, producerCandidates[parameter][0], $"Cycle detected in cascading filter dependencies: {cycle}");
            break;
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private void Add(List<LintResult> results, CreateVisualStatement visual, string message) => results.Add(new LintResult
    {
        RuleName = Name,
        Code = "RPT-CASCADE",
        Severity = LintSeverity.Error,
        Message = message,
        LineNumber = visual.Line,
        ColumnNumber = visual.Column
    });

    private static string Normalize(string name) => name.StartsWith('@') ? name : "@" + name;
}
