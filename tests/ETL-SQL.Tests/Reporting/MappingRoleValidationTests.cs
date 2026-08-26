using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics.Runtime;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Validates that <see cref="NamedVisualChartLowerer"/> rejects unrecognised MAPPINGS roles
/// with a clear error message instead of silently dropping them.
/// </summary>
public class MappingRoleValidationTests
{
    private static readonly NamedVisualChartLowerer Lowerer = new();

    private static CreateVisualStatement MakeVisual(VisualType type, params (string role, string column)[] mappings) =>
        new()
        {
            Name = "TestVisual",
            VisualType = type,
            Source = new VisualSourceExpression { TempTableName = "#data" },
            Mappings = mappings.Select(m => new VisualMapping { Role = m.role, Column = m.column }).ToList(),
        };

    private static VisualManifest MakeManifest(params string[] columns) => new()
    {
        Name = "TestVisual",
        VisualType = "BAR",
        Columns = columns.ToList(),
        Rows = [["a", "1"]],
        Options = new Dictionary<string, string>(),
    };

    // ── The original bug: CATEGORY/VALUE on BAR produces a wrong chart ──

    [Fact]
    public void Bar_CategoryRole_Rejected()
    {
        var stmt = MakeVisual(VisualType.Bar, ("CATEGORY", "Region"), ("VALUE", "Revenue"));
        var manifest = MakeManifest("Region", "Revenue");
        var ex = Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(stmt, manifest));
        Assert.Contains("CATEGORY", ex.Message);
        Assert.Contains("Valid roles", ex.Message);
    }

    [Fact]
    public void Bar_CanonicalXY_Accepted()
    {
        var stmt = MakeVisual(VisualType.Bar, ("X", "Region"), ("Y", "Revenue"));
        var manifest = MakeManifest("Region", "Revenue");
        var spec = Lowerer.Lower(stmt, manifest);
        Assert.NotNull(spec);
        Assert.NotEmpty(spec.Bindings);
    }

    // ── Every named visual type rejects a bogus role ────────────────────

    public static IEnumerable<object[]> AllNamedVisualTypes()
    {
        foreach (var type in new[]
        {
            VisualType.Bar, VisualType.HorizontalBar, VisualType.Line,
            VisualType.Scatter, VisualType.Bubble, VisualType.Pie, VisualType.Donut,
            VisualType.Funnel, VisualType.Gauge, VisualType.HeatMap,
            VisualType.Waterfall, VisualType.BoxPlot, VisualType.Candlestick,
            VisualType.Gantt, VisualType.Trellis, VisualType.Radar, VisualType.Combo,
        })
            yield return [type];
    }

    [Theory]
    [MemberData(nameof(AllNamedVisualTypes))]
    public void UnknownRole_Rejected_ForEveryNamedVisualType(VisualType type)
    {
        var stmt = MakeVisual(type, ("BOGUS_NONEXISTENT", "Col1"));
        var manifest = MakeManifest("Col1");
        var ex = Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(stmt, manifest));
        Assert.Contains("BOGUS_NONEXISTENT", ex.Message);
        Assert.Contains("Valid roles", ex.Message);
    }

    // ── ValidRolesFor returns non-empty for every supported type ─────────

    [Theory]
    [MemberData(nameof(AllNamedVisualTypes))]
    public void ValidRolesFor_ReturnsNonEmpty_ForEveryNamedType(VisualType type)
    {
        var roles = NamedVisualChartLowerer.ValidRolesFor(type);
        Assert.NotEmpty(roles);
    }

    // ── Type-specific alias acceptance ──────────────────────────────────

    [Theory]
    [InlineData(VisualType.Funnel, "NAME")]
    [InlineData(VisualType.Funnel, "CATEGORY")]
    [InlineData(VisualType.Funnel, "VALUE")]
    [InlineData(VisualType.Gauge, "VALUE")]
    [InlineData(VisualType.Gauge, "LABEL")]
    [InlineData(VisualType.Waterfall, "NAME")]
    [InlineData(VisualType.Waterfall, "VALUE")]
    [InlineData(VisualType.Waterfall, "TOTAL")]
    [InlineData(VisualType.Gantt, "START")]
    [InlineData(VisualType.Gantt, "END")]
    [InlineData(VisualType.Gantt, "PROGRESS")]
    [InlineData(VisualType.Trellis, "FACET")]
    [InlineData(VisualType.BoxPlot, "LOW")]
    [InlineData(VisualType.BoxPlot, "Q1")]
    [InlineData(VisualType.BoxPlot, "MEDIAN")]
    [InlineData(VisualType.BoxPlot, "Q3")]
    [InlineData(VisualType.BoxPlot, "HIGH")]
    [InlineData(VisualType.Candlestick, "OPEN")]
    [InlineData(VisualType.Candlestick, "HIGH")]
    [InlineData(VisualType.Candlestick, "LOW")]
    [InlineData(VisualType.Candlestick, "CLOSE")]
    [InlineData(VisualType.Pie, "LABEL")]
    [InlineData(VisualType.Pie, "VALUE")]
    [InlineData(VisualType.Donut, "CATEGORY")]
    [InlineData(VisualType.Donut, "VALUE")]
    public void TypeSpecificRole_Accepted(VisualType type, string role)
    {
        var stmt = MakeVisual(type, (role, "Col1"));
        var manifest = MakeManifest("Col1");
        var spec = Lowerer.Lower(stmt, manifest);
        Assert.NotNull(spec);
    }

    // ── Common roles accepted everywhere ────────────────────────────────

    [Theory]
    [InlineData("SERIES")]
    [InlineData("COLOR")]
    [InlineData("TOOLTIP")]
    public void CommonRole_Accepted_OnBar(string role)
    {
        var stmt = MakeVisual(VisualType.Bar, ("X", "Cat"), ("Y", "Val"), (role, "Extra"));
        var manifest = MakeManifest("Cat", "Val", "Extra");
        var spec = Lowerer.Lower(stmt, manifest);
        Assert.NotNull(spec);
    }

    // ── Cross-type confusion: BAR role on PIE and vice versa ────────────

    [Fact]
    public void Bar_LabelRole_Rejected()
    {
        var stmt = MakeVisual(VisualType.Bar, ("LABEL", "Region"), ("Y", "Revenue"));
        var manifest = MakeManifest("Region", "Revenue");
        var ex = Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(stmt, manifest));
        Assert.Contains("LABEL", ex.Message);
    }

    [Fact]
    public void Pie_YRole_Rejected()
    {
        var stmt = MakeVisual(VisualType.Pie, ("Y", "Region"), ("VALUE", "Revenue"));
        var manifest = MakeManifest("Region", "Revenue");
        var ex = Assert.Throws<InvalidOperationException>(() => Lowerer.Lower(stmt, manifest));
        Assert.Contains("'Y'", ex.Message);
    }
}
