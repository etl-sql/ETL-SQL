using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

/// <summary>
/// Guards the mirrored enum families the CUSTOM chart path bridges between Core's AST and the
/// renderer-neutral chart contract.
/// </summary>
/// <remarks>
/// These used to be bridged by <c>Enum.Parse&lt;T&gt;(value.ToString())</c>, so adding or renaming a member
/// on either side produced an <c>ArgumentException</c> at render time — surfaced as an error string inside
/// a rendered report — instead of failing the build or a test. The bridges are now explicit
/// arm-per-member switches; these tests are the gate that keeps the families aligned.
/// </remarks>
public sealed class AdvancedChartEnumBridgeParityTests
{
    [Fact]
    public void EveryMarkKindBridgesBothWays()
    {
        foreach (var value in Enum.GetValues<AdvancedChartMarkKind>())
            Assert.Equal(value, AdvancedChartEnumBridge.Mark(AdvancedChartEnumBridge.Mark(value)));
        foreach (var value in Enum.GetValues<MarkKind>())
            Assert.Equal(value, AdvancedChartEnumBridge.Mark(AdvancedChartEnumBridge.Mark(value)));
    }

    [Fact]
    public void EveryDataKindBridgesBothWays()
    {
        foreach (var value in Enum.GetValues<AdvancedChartDataKind>())
            Assert.Equal(value, AdvancedChartEnumBridge.DataKind(AdvancedChartEnumBridge.DataKind(value)));
        foreach (var value in Enum.GetValues<DataSemanticKind>())
            Assert.Equal(value, AdvancedChartEnumBridge.DataKind(AdvancedChartEnumBridge.DataKind(value)));
    }

    [Fact]
    public void EveryGrammarChannelBridgesBothWays()
    {
        foreach (var value in Enum.GetValues<AdvancedChartChannel>())
            Assert.Equal(value, AdvancedChartEnumBridge.Channel(AdvancedChartEnumBridge.Channel(value)));
    }

    /// <summary>
    /// <see cref="FieldChannel"/> is deliberately the wider family because facet channels are synthesized
    /// by lowering instead of authored as encodings. The reverse bridge returns null for exactly those
    /// members, so widening either family without widening the bridge fails here.
    /// </summary>
    [Fact]
    public void ContractChannelsWithoutGrammarCounterpartsAreExactlyTheKnownSet()
    {
        var unmapped = Enum.GetValues<FieldChannel>()
            .Where(channel => AdvancedChartEnumBridge.Channel(channel) is null)
            .ToList();

        Assert.Equal(
            [FieldChannel.Row, FieldChannel.Column, FieldChannel.Wrap],
            unmapped);

        foreach (var channel in Enum.GetValues<FieldChannel>().Except(unmapped))
            Assert.Equal(channel, AdvancedChartEnumBridge.Channel(AdvancedChartEnumBridge.Channel(channel)!.Value));
    }

    [Theory]
    [MemberData(nameof(NameAlignedFamilies))]
    public void MirroredFamiliesStayMemberForMemberAligned(Type ast, Type contract)
    {
        Assert.Equal(Enum.GetNames(ast).Order().ToList(), Enum.GetNames(contract).Order().ToList());
    }

    /// <summary>
    /// The families whose members are mirrored one-for-one by name. Channel and sort direction are
    /// excluded on purpose: channel is intentionally asymmetric (see above) and sort direction is the one
    /// family that is not name-aligned — the grammar's <c>SOURCE</c> is the contract's <c>None</c>.
    /// </summary>
    public static TheoryData<Type, Type> NameAlignedFamilies() => new()
    {
        { typeof(AdvancedChartMarkKind), typeof(MarkKind) },
        { typeof(AdvancedChartDataKind), typeof(DataSemanticKind) },
        { typeof(AdvancedChartScaleKind), typeof(ScaleKind) },
        { typeof(AdvancedChartResolutionMode), typeof(ScaleResolutionMode) },
        { typeof(AdvancedChartAxisRole), typeof(AxisRole) },
        { typeof(AdvancedChartStackMode), typeof(StackMode) },
        { typeof(AdvancedChartTickOrientation), typeof(TickOrientation) },
        { typeof(AdvancedChartPositionKind), typeof(PositionAdjustmentKind) },
        { typeof(AdvancedChartPositionUnit), typeof(PositionAdjustmentUnit) },
        { typeof(AdvancedChartConditionChannel), typeof(ConditionalEncodingChannel) },
        { typeof(AdvancedChartColorRangeKind), typeof(ColorRangeKind) }
    };

    [Fact]
    public void EveryRemainingBridgeCoversItsWholeSourceFamily()
    {
        var bridges = new List<Action>
        {
            () => Consume(Enum.GetValues<AdvancedChartScaleKind>().Select(AdvancedChartEnumBridge.Scale)),
            () => Consume(Enum.GetValues<AdvancedChartResolutionMode>().Select(AdvancedChartEnumBridge.Resolution)),
            () => Consume(Enum.GetValues<AdvancedChartAxisRole>().Select(AdvancedChartEnumBridge.Axis)),
            () => Consume(Enum.GetValues<AdvancedChartSortDirection>().Select(AdvancedChartEnumBridge.Sort)),
            () => Consume(Enum.GetValues<AdvancedChartStackMode>().Select(AdvancedChartEnumBridge.Stack)),
            () => Consume(Enum.GetValues<AdvancedChartTickOrientation>().Select(AdvancedChartEnumBridge.Tick)),
            () => Consume(Enum.GetValues<AdvancedChartPositionKind>().Select(AdvancedChartEnumBridge.Position)),
            () => Consume(Enum.GetValues<AdvancedChartPositionUnit>().Select(AdvancedChartEnumBridge.Unit)),
            () => Consume(Enum.GetValues<AdvancedChartConditionChannel>().Select(AdvancedChartEnumBridge.Condition)),
            () => Consume(Enum.GetValues<AdvancedChartColorRangeKind>().Select(AdvancedChartEnumBridge.ColorRange)),
            () => Consume(Enum.GetValues<AdvancedChartCoordinateKind>().Select(AdvancedChartEnumBridge.Coordinate))
        };

        foreach (var bridge in bridges) bridge();
    }

    /// <summary>
    /// Geographic composition is now part of the grammar, so every coordinate member maps one-to-one.
    /// </summary>
    [Fact]
    public void CoordinateBridgeCoversEveryGrammarCoordinate()
    {
        Assert.Equal(
            [CoordinateKind.Cartesian, CoordinateKind.TransposedCartesian, CoordinateKind.Polar, CoordinateKind.Geographic],
            Enum.GetValues<AdvancedChartCoordinateKind>().Select(AdvancedChartEnumBridge.Coordinate).ToList());
    }

    /// <summary>Sort direction is asymmetric by design; pin the mapping so a rename cannot silently invert it.</summary>
    [Fact]
    public void SortDirectionMapsSourceOrderOntoNone()
    {
        Assert.Equal(SortDirection.None, AdvancedChartEnumBridge.Sort(AdvancedChartSortDirection.Source));
        Assert.Equal(SortDirection.Ascending, AdvancedChartEnumBridge.Sort(AdvancedChartSortDirection.Ascending));
        Assert.Equal(SortDirection.Descending, AdvancedChartEnumBridge.Sort(AdvancedChartSortDirection.Descending));
    }

    private static void Consume<T>(IEnumerable<T> values)
    {
        foreach (var value in values) Assert.NotNull(value);
    }
}
