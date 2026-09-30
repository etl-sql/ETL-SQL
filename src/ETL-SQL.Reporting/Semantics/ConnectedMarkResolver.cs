using System;
using System.Collections.Immutable;
using System.Linq;

namespace ETL_SQL.Reporting.Semantics.Runtime;

internal static class ConnectedMarkResolver
{
    internal static PlotPlan Attach(PlotPlan plan)
    {
        var layers = plan.Layers.Select(layer => layer with { Connections = ResolveGapConnections(layer) }).ToImmutableArray();
        var descriptions = layers.SelectMany(layer => layer.Connections.Select(connection =>
            new SemanticFallbackItem($"Row {connection.SourceRowIndex + 1}", Describe(connection),
                plan.Fallback.Items.Length + connection.SourceIndex)
            { Group = layer.Id, Detail = "outgoing connection" })).ToImmutableArray();
        var resolved = plan with
        {
            Schema = ChartContractVersions.ConnectedPlotPlanSchema,
            Version = ChartContractVersions.ConnectedPlotPlanVersion,
            Layers = layers,
            Fallback = plan.Fallback with { Items = plan.Fallback.Items.AddRange(descriptions) }
        };
        resolved.Validate();
        return resolved;
    }

    internal static string Describe(ResolvedMarkConnection connection) =>
        $"Row {connection.SourceRowIndex + 1} to row {connection.DestinationRowIndex + 1}; source-owned " +
        (connection.Encodings.IsDefaultOrEmpty ? "layer presentation" : string.Join(", ",
            connection.Encodings.Select(encoding => $"{encoding.Channel}: {PlotPlanResolver.Display(encoding.Value)}")));

    // Input is one already partitioned layer/facet with resolved coordinates and GAP handling.
    // Array indices retain identity even when row identifiers or coordinates coincide.
    internal static ImmutableArray<ResolvedMarkConnection> ResolveGapConnections(ResolvedMarkLayer layer)
    {
        if (layer.Mark is not (MarkKind.Line or MarkKind.Area))
            throw new ArgumentException("Connections require a LINE or AREA layer.", nameof(layer));
        if (layer.Data.IsDefaultOrEmpty) return [];

        var connections = ImmutableArray.CreateBuilder<ResolvedMarkConnection>();
        for (var index = 0; index + 1 < layer.Data.Length; index++)
        {
            var source = layer.Data[index];
            var destination = layer.Data[index + 1];
            if (source.IsGap || destination.IsGap) continue;

            var encodings = source.Encodings.IsDefaultOrEmpty
                ? ImmutableArray<ResolvedEncodingValue>.Empty
                : source.Encodings.Where(encoding => encoding.Channel is
                    ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity).ToImmutableArray();
            connections.Add(new ResolvedMarkConnection(index, index + 1, source.RowIndex,
                destination.RowIndex, encodings));
        }
        return connections.ToImmutable();
    }
}
