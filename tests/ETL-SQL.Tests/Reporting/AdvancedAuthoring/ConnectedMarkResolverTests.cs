using System.Collections.Immutable;
using System.Text.Json;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Reporting.Semantics.Runtime;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public sealed class ConnectedMarkResolverTests
{
    [Theory]
    [InlineData(MarkKind.Line)]
    [InlineData(MarkKind.Area)]
    public void SourceOwnsTheWholeConnection(MarkKind mark)
    {
        var rows = ImmutableArray.Create(Row(7, "red"), Row(2, "blue"), Row(9, "green"));
        var connections = ConnectedMarkResolver.ResolveConnections(Layer(mark, rows));
        Assert.Equal(new[] { (7, 2), (2, 9) }, connections.Select(connection =>
            (connection.SourceRowIndex, connection.DestinationRowIndex)));
        Assert.Equal(new[] { "red", "blue" }, connections.Select(connection =>
            Assert.Single(connection.Encodings).Value.Text));
        Assert.Equal("green", Assert.Single(rows[2].Encodings).Value.Text);
        Assert.All(connections, connection => connection.Validate(Layer(mark, rows)));
    }

    [Fact]
    public void MissingSourceStyleDoesNotBorrowDestinationStyle()
    {
        var source = Row(0, null);
        var destination = Row(1, "red");
        var connection = Assert.Single(ConnectedMarkResolver.ResolveConnections(
            Layer(MarkKind.Line, [source, destination])));
        Assert.Empty(connection.Encodings);
    }

    [Fact]
    public void GapBreaksConnectivityWithoutRemovingRows()
    {
        var rows = ImmutableArray.Create(Row(0, "red"), Row(1, "blue") with { IsGap = true },
            Row(2, "green"), Row(3, "orange"));
        var connection = Assert.Single(ConnectedMarkResolver.ResolveConnections(Layer(MarkKind.Line, rows)));
        Assert.Equal((2, 3), (connection.SourceIndex, connection.DestinationIndex));
        Assert.Equal(4, rows.Length);
        Assert.True(rows[1].IsGap);
    }

    [Fact]
    public void CoincidentRowsRetainArrayIdentityAndTransparentConnections()
    {
        var row = Row(4, "red") with
        {
            Encodings = [new(ConditionalEncodingChannel.Opacity, ChartValue.From(0m)),
                new(ConditionalEncodingChannel.Text, ChartValue.From("point label")),
                new(ConditionalEncodingChannel.Size, ChartValue.From(5m))]
        };
        var connections = ConnectedMarkResolver.ResolveConnections(Layer(MarkKind.Area, [row, row, row]));
        Assert.Equal(new[] { (0, 1), (1, 2) }, connections.Select(connection =>
            (connection.SourceIndex, connection.DestinationIndex)));
        Assert.All(connections, connection =>
        {
            var encoding = Assert.Single(connection.Encodings);
            Assert.Equal(ConditionalEncodingChannel.Opacity, encoding.Channel);
            Assert.Equal(0m, encoding.Value.Decimal);
        });
        Assert.Equal(3, row.Encodings.Length);
    }

    [Fact]
    public void EmptySingletonAndSeparateLayersHaveNoConnections()
    {
        Assert.Empty(ConnectedMarkResolver.ResolveConnections(Layer(MarkKind.Line, [])));
        Assert.Empty(ConnectedMarkResolver.ResolveConnections(Layer(MarkKind.Line, [Row(0, "red")])));
        Assert.Empty(ConnectedMarkResolver.ResolveConnections(Layer(MarkKind.Line, [Row(1, "blue")])));
        Assert.Throws<ArgumentException>(() => ConnectedMarkResolver.ResolveConnections(Layer(MarkKind.Point, [])));
    }

    [Fact]
    public void ContractRoundTripPreservesEndpointOwnership()
    {
        var layer = Layer(MarkKind.Line, [Row(5, "red"), Row(8, "blue")]);
        var connection = Assert.Single(ConnectedMarkResolver.ResolveConnections(layer));
        var json = JsonSerializer.Serialize(connection);
        var restored = Assert.IsType<ResolvedMarkConnection>(JsonSerializer.Deserialize<ResolvedMarkConnection>(json));
        restored.Validate(layer);
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Equal((5, 8), (restored.SourceRowIndex, restored.DestinationRowIndex));
        Assert.Equal("red", Assert.Single(restored.Encodings).Value.Text);
    }

    [Fact]
    public void ContractRejectsTamperedEndpointsAndDestinationPresentation()
    {
        var layer = Layer(MarkKind.Line, [Row(5, "red"), Row(8, "blue"), Row(9, "green")]);
        var connection = ConnectedMarkResolver.ResolveConnections(layer)[0];
        var invalid = new[]
        {
            connection with { SourceIndex = -1 },
            connection with { SourceIndex = int.MaxValue },
            connection with { DestinationIndex = 2 },
            connection with { DestinationIndex = -1 },
            connection with { SourceRowIndex = 8 },
            connection with { DestinationRowIndex = 5 },
            connection with { Encodings = layer.Data[1].Encodings },
            connection with { Encodings = default },
            connection with { Encodings = [] }
        };
        Assert.All(invalid, candidate => Assert.Throws<InvalidDataException>(() => candidate.Validate(layer)));
        Assert.Throws<InvalidDataException>(() => connection.Validate(layer with
        {
            Data = layer.Data.SetItem(1, layer.Data[1] with { IsGap = true })
        }));
        Assert.Throws<InvalidDataException>(() => connection.Validate(layer with { Mark = MarkKind.Point }));
    }

    private static ResolvedMarkLayer Layer(MarkKind mark, ImmutableArray<ResolvedDatum> rows) =>
        new("connected", mark, 0, null, rows);

    private static ResolvedDatum Row(int index, string? color) => new(index,
        [new(FieldChannel.X, ChartValue.From(1m), null), new(FieldChannel.Y, ChartValue.From(2m), null)], false)
    {
        Encodings = color is null ? [] : [new(ConditionalEncodingChannel.Color, ChartValue.From(color))]
    };
}
