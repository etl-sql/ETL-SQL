using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Tests.Reporting.CascadingSlicers;

public record SlicerDependencyNode(
    string VisualName,
    string BoundParameter,
    IReadOnlyList<string> ParentParameters,
    string VisualType = "SLICER");

public record DependencyGraphAnalysis(
    IReadOnlyList<string> TopologicalOrder,
    bool HasCycles,
    IReadOnlyList<IReadOnlyList<string>> Cycles,
    IReadOnlyList<string> RootParameters,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DownstreamMap);

public class CascadingSlicerDependencyGraph
{
    private readonly Dictionary<string, SlicerDependencyNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _paramToDependents = new(StringComparer.OrdinalIgnoreCase);

    public void AddNode(SlicerDependencyNode node)
    {
        _nodes[node.BoundParameter] = node;

        foreach (var parent in node.ParentParameters)
        {
            if (!_paramToDependents.TryGetValue(parent, out var deps))
            {
                deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _paramToDependents[parent] = deps;
            }
            deps.Add(node.BoundParameter);
        }
    }

    public DependencyGraphAnalysis Analyze()
    {
        var allParams = _nodes.Keys.ToList();
        var inDegree = allParams.ToDictionary(p => p, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var node in _nodes.Values)
        {
            foreach (var parent in node.ParentParameters)
            {
                if (inDegree.ContainsKey(node.BoundParameter))
                {
                    inDegree[node.BoundParameter]++;
                }
            }
        }

        var rootParams = inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
        var queue = new Queue<string>(rootParams);
        var topoOrder = new List<string>();

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            topoOrder.Add(curr);

            if (_paramToDependents.TryGetValue(curr, out var children))
            {
                foreach (var child in children)
                {
                    inDegree[child]--;
                    if (inDegree[child] == 0)
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        bool hasCycles = topoOrder.Count < allParams.Count;
        var cycles = new List<IReadOnlyList<string>>();

        if (hasCycles)
        {
            cycles = FindCycles();
        }

        var downstreamMap = allParams.ToDictionary(
            p => p,
            p => (IReadOnlyList<string>)GetDescendants(p).ToList(),
            StringComparer.OrdinalIgnoreCase);

        return new DependencyGraphAnalysis(
            TopologicalOrder: topoOrder,
            HasCycles: hasCycles,
            Cycles: cycles,
            RootParameters: rootParams,
            DownstreamMap: downstreamMap);
    }

    public IReadOnlySet<string> GetDescendants(string paramName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(paramName);

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (_paramToDependents.TryGetValue(curr, out var children))
            {
                foreach (var child in children)
                {
                    if (result.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }
        }

        return result;
    }

    private List<IReadOnlyList<string>> FindCycles()
    {
        var cycles = new List<IReadOnlyList<string>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var nodeParam in _nodes.Keys)
        {
            if (!visited.Contains(nodeParam))
            {
                DfsCycle(nodeParam, visited, inStack, path, cycles);
            }
        }

        return cycles;
    }

    private void DfsCycle(
        string current,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path,
        List<IReadOnlyList<string>> cycles)
    {
        visited.Add(current);
        inStack.Add(current);
        path.Add(current);

        if (_paramToDependents.TryGetValue(current, out var children))
        {
            foreach (var child in children)
            {
                if (!visited.Contains(child))
                {
                    DfsCycle(child, visited, inStack, path, cycles);
                }
                else if (inStack.Contains(child))
                {
                    // Cycle detected: extract subpath from child to current
                    int cycleStart = path.IndexOf(child);
                    if (cycleStart >= 0)
                    {
                        var cyclePath = path.Skip(cycleStart).Concat(new[] { child }).ToList();
                        cycles.Add(cyclePath);
                    }
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(current);
    }
}
