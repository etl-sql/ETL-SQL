using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

/// <summary>A source-owned outgoing connection within one resolved layer and facet.</summary>
public sealed record ResolvedMarkConnection(
    int SourceIndex,
    int DestinationIndex,
    int SourceRowIndex,
    int DestinationRowIndex,
    ImmutableArray<ResolvedEncodingValue> Encodings)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FacetId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedConnectionGeometry? Geometry { get; init; }

    public static bool ConnectsAcrossNulls(ResolvedMarkLayer layer) => !layer.Style.IsDefaultOrEmpty &&
        layer.Style.Any(style => style.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase) && style.Value.Equals("CONNECT", StringComparison.OrdinalIgnoreCase));

    public static bool FillsNullsWithZero(ResolvedMarkLayer layer) => !layer.Style.IsDefaultOrEmpty &&
        layer.Style.Any(style => style.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase) && style.Value.Equals("ZERO", StringComparison.OrdinalIgnoreCase));

    public static ResolvedConnectionCoordinates? Coordinates(ResolvedDatum datum, bool ribbon) => datum.IsGap
        ? null : datum.ConnectionCoordinates ?? ResolvedConnectionCoordinates.FromRaw(datum, ribbon, false);

    public static bool HasCompleteCoordinates(ResolvedDatum datum, bool ribbon, bool confidence = false)
    {
        bool Numeric(FieldChannel channel)
        {
            foreach (var value in datum.Channels)
                if (value.Channel == channel) return value.Value.Kind is ChartValueKind.Integer or ChartValueKind.Decimal or ChartValueKind.FloatingPoint;
            return false;
        }
        if (datum.ConnectionCoordinates is { } resolved)
            return !datum.IsGap && (ribbon ? resolved.YStart.HasValue && resolved.YEnd.HasValue : resolved.Y.HasValue);
        return !datum.IsGap && Numeric(FieldChannel.X) &&
            (ribbon ? Numeric(confidence ? FieldChannel.ConfidenceLow : FieldChannel.YStart) && Numeric(confidence ? FieldChannel.ConfidenceHigh : FieldChannel.YEnd) : Numeric(FieldChannel.Y));
    }

    /// <summary>Pairs are adjacent in the eligible run; CONNECT skips incomplete raw rows.</summary>
    public static IEnumerable<(int SourceIndex, int DestinationIndex)> ExpectedPairs(ResolvedMarkLayer layer, IReadOnlySet<int>? facetRows = null)
    {
        var connect = ConnectsAcrossNulls(layer);
        var previous = -1;
        for (var index = 0; index < layer.Data.Length; index++)
        {
            var datum = layer.Data[index];
            if (facetRows is not null && !facetRows.Contains(datum.RowIndex)) continue;
            if (datum.IsGap) { previous = -1; continue; }
            if (connect && !HasCompleteCoordinates(datum, layer.AreaRibbon)) continue;
            if (previous >= 0) yield return (previous, index);
            previous = index;
        }
    }

    /// <summary>Validates source ownership and adjacency after the declared null policy.</summary>
    public void Validate(ResolvedMarkLayer layer, IReadOnlySet<int>? facetRows = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Mark is not (MarkKind.Line or MarkKind.Area))
            throw new InvalidDataException("Connections require a LINE or AREA layer.");
        var connect = ConnectsAcrossNulls(layer);
        if ((FacetId is null) != (facetRows is null))
            throw new InvalidDataException("Facet connections require their declared facet's row membership.");
        if (layer.Data.IsDefaultOrEmpty || SourceIndex < 0 || SourceIndex >= layer.Data.Length - 1 ||
            DestinationIndex <= SourceIndex || DestinationIndex >= layer.Data.Length || facetRows is null && !connect && DestinationIndex != SourceIndex + 1)
            throw new InvalidDataException("A connection must reference adjacent eligible rows in its resolved layer.");
        var source = layer.Data[SourceIndex];
        var destination = layer.Data[DestinationIndex];
        if (source.IsGap || destination.IsGap || source.RowIndex != SourceRowIndex || destination.RowIndex != DestinationRowIndex)
            throw new InvalidDataException("Connection endpoints must match non-gap rows in their resolved layer.");
        if (facetRows is not null && (!facetRows.Contains(source.RowIndex) || !facetRows.Contains(destination.RowIndex)))
            throw new InvalidDataException("Connection endpoints must remain inside their declared facet.");
        if (connect || facetRows is not null || FillsNullsWithZero(layer))
        {
            if (!HasCompleteCoordinates(source, layer.AreaRibbon) || !HasCompleteCoordinates(destination, layer.AreaRibbon))
                throw new InvalidDataException("CONNECT endpoints require complete numeric coordinates.");
            for (var index = SourceIndex + 1; index < DestinationIndex; index++)
                if ((facetRows is null || facetRows.Contains(layer.Data[index].RowIndex)) &&
                    (layer.Data[index].IsGap || !connect || HasCompleteCoordinates(layer.Data[index], layer.AreaRibbon)))
                    throw new InvalidDataException("CONNECT may skip only incomplete rows, without crossing gaps.");
        }
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
