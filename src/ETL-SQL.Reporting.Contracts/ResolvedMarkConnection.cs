using System.Collections.Immutable;

namespace ETL_SQL.Reporting.Semantics;

/// <summary>A source-owned outgoing connection within one resolved layer and facet.</summary>
public sealed record ResolvedMarkConnection(
    int SourceIndex,
    int DestinationIndex,
    int SourceRowIndex,
    int DestinationRowIndex,
    ImmutableArray<ResolvedEncodingValue> Encodings)
{
    /// <summary>Validates the initial adjacent-row, GAP-handling connection contract.</summary>
    public void Validate(ResolvedMarkLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Mark is not (MarkKind.Line or MarkKind.Area))
            throw new InvalidDataException("Connections require a LINE or AREA layer.");
        if (layer.Data.IsDefaultOrEmpty || SourceIndex < 0 || SourceIndex >= layer.Data.Length - 1 ||
            DestinationIndex != SourceIndex + 1)
            throw new InvalidDataException("A GAP connection must reference adjacent rows in its resolved layer.");
        var source = layer.Data[SourceIndex];
        var destination = layer.Data[DestinationIndex];
        if (source.IsGap || destination.IsGap || source.RowIndex != SourceRowIndex || destination.RowIndex != DestinationRowIndex)
            throw new InvalidDataException("Connection endpoints must match non-gap rows in their resolved layer.");
        if (Encodings.IsDefault)
            throw new InvalidDataException("Connection presentation must be an initialized array.");
        var expected = source.Encodings.IsDefaultOrEmpty
            ? []
            : source.Encodings.Where(encoding => encoding.Channel is ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity).ToArray();
        if (!Encodings.SequenceEqual(expected))
            throw new InvalidDataException("Connection presentation must match its source row's COLOR and OPACITY.");
        ChartContractValidation.RequireUnique(Encodings.Select(encoding => encoding.Channel.ToString()), "connection presentation channel");
        foreach (var encoding in Encodings) encoding.Value.Validate();
    }
}
