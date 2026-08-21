using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting;

namespace ETL_SQL.Tests.Reporting.TerminalSemantics;

/// <summary>
/// Test-side domain abstraction representing a strongly-typed chart dataset.
/// Provides building blocks for semantic test projections without altering production runtime code.
/// </summary>
public record TestChartColumn(string Name, Type DataType);

public record TestChartDataSet(
    IReadOnlyList<TestChartColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public static TestChartDataSet Create(
        (string Name, Type Type)[] schema,
        params object?[][] rows)
    {
        var cols = schema.Select(s => new TestChartColumn(s.Name, s.Type)).ToList();
        var rowList = rows.Select(r => (IReadOnlyList<object?>)r.ToList()).ToList();
        return new TestChartDataSet(cols, rowList);
    }
}

/// <summary>
/// Test-side chart specification descriptor (ChartSpec model).
/// </summary>
public record TestChartSpec(
    string Name,
    string VisualType,
    string Title,
    TestChartDataSet DataSet,
    IReadOnlyDictionary<string, string> Mappings,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlyList<OverlayManifest>? Overlays = null);

/// <summary>
/// Reusable test-side fixture builder that lowers test specs into VisualManifest instances.
/// </summary>
public static class TerminalSemanticFixtureBuilder
{
    public static VisualManifest BuildVisualManifest(TestChartSpec spec)
    {
        var vm = new VisualManifest
        {
            Name = spec.Name,
            VisualType = spec.VisualType,
            Columns = spec.DataSet.Columns.Select(c => c.Name).ToList(),
            Rows = spec.DataSet.Rows
                .Select(r => r.Select(cell => cell?.ToString()).ToList())
                .ToList(),
            Options = new Dictionary<string, string>(spec.Options, StringComparer.OrdinalIgnoreCase),
            Overlays = spec.Overlays?.ToList()
        };

        vm.Options["TITLE"] = spec.Title;
        vm.Options["title"] = spec.Title;

        foreach (var (role, col) in spec.Mappings)
        {
            vm.Options[$"mapping:{role.ToLowerInvariant()}"] = col;
        }

        return vm;
    }

    public static ReportManifest BuildReportManifest(params TestChartSpec[] specs)
    {
        var visuals = specs.Select(BuildVisualManifest).ToList();
        var page = new PageManifest
        {
            Name = "DefaultPage",
            Title = "Terminal Semantic Test Page",
            Structure = string.Join(" / ", specs.Select((_, i) => ((char)('A' + i)).ToString())),
            SlotMap = specs.Select((s, i) => new KeyValuePair<string, string>(((char)('A' + i)).ToString(), s.Name))
                .ToDictionary(kv => kv.Key, kv => kv.Value)
        };

        return new ReportManifest
        {
            Title = "Terminal Semantic Test Report",
            Visuals = visuals,
            Pages = [page]
        };
    }
}
