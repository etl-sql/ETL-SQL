using System;
using System.Collections.Immutable;
using System.Linq;

namespace ETL_SQL.Reporting.Semantics.Runtime;

internal static class ConnectedMarkResolver
{
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
