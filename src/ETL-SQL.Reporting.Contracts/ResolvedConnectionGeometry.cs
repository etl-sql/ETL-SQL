using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

public enum ConnectedInterpolationKind { Smooth, StepBefore, StepAfter }

/// <summary>A point in the original semantic X/Y space, before coordinate mapping.</summary>
public readonly record struct ResolvedConnectionPoint(decimal X, decimal Y);

/// <summary>Resolved upper and optional lower boundaries of one source-owned connection.</summary>
public sealed record ResolvedConnectionGeometry(
    ImmutableArray<ResolvedConnectionPoint> Upper,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ImmutableArray<ResolvedConnectionPoint> Lower,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Cubic)
{
    public bool Equals(ResolvedConnectionGeometry? other) => other is not null && Cubic == other.Cubic &&
        Same(Upper, other.Upper) && Same(Lower, other.Lower);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Cubic);
        hash.Add(Upper.IsDefault);
        hash.Add(Lower.IsDefault);
        if (!Upper.IsDefault) foreach (var point in Upper) hash.Add(point);
        if (!Lower.IsDefault) foreach (var point in Lower) hash.Add(point);
        return hash.ToHashCode();
    }

    private static bool Same(ImmutableArray<ResolvedConnectionPoint> first, ImmutableArray<ResolvedConnectionPoint> second) =>
        first.IsDefault == second.IsDefault && (first.IsDefault || first.SequenceEqual(second));

    public static bool Supports(string? value) => value?.Trim().ToUpperInvariant() is "LINEAR" or "SMOOTH" or "STEP_BEFORE" or "STEP_AFTER";

    public static string Name(ConnectedInterpolationKind kind) => kind switch
    {
        ConnectedInterpolationKind.Smooth => "SMOOTH",
        ConnectedInterpolationKind.StepBefore => "STEP_BEFORE",
        ConnectedInterpolationKind.StepAfter => "STEP_AFTER",
        _ => throw new InvalidDataException("Unsupported connected interpolation.")
    };

    public static ConnectedInterpolationKind? Kind(ImmutableArray<StyleToken> style) => style.IsDefaultOrEmpty ? null :
        style.FirstOrDefault(token => token.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase))?.Value.Trim().ToUpperInvariant() switch
        {
            "SMOOTH" => ConnectedInterpolationKind.Smooth,
            "STEP_BEFORE" => ConnectedInterpolationKind.StepBefore,
            "STEP_AFTER" => ConnectedInterpolationKind.StepAfter,
            _ => null
        };

    /// <summary>Resolves each eligible pair using only neighbors from its own uninterrupted run.</summary>
    public static ImmutableArray<ResolvedConnectionGeometry> Resolve(ResolvedMarkLayer layer)
    {
        if (layer.ConnectionInterpolation is not { } interpolation || !Enum.IsDefined(interpolation))
            throw new InvalidDataException("Connection geometry requires declared supported interpolation.");
        if (layer.Connections.IsDefault) throw new InvalidDataException("Connection geometry requires typed connections.");
        var incoming = new Dictionary<(string? Facet, int Index), ResolvedMarkConnection>();
        var outgoing = new Dictionary<(string? Facet, int Index), ResolvedMarkConnection>();
        foreach (var connection in layer.Connections)
        {
            if (connection.SourceIndex < 0 || connection.DestinationIndex <= connection.SourceIndex || connection.DestinationIndex >= layer.Data.Length ||
                !incoming.TryAdd((connection.FacetId, connection.DestinationIndex), connection) || !outgoing.TryAdd((connection.FacetId, connection.SourceIndex), connection))
                throw new InvalidDataException("Interpolated connections require valid unique endpoints within each facet.");
        }
        var coordinates = new ResolvedConnectionCoordinates?[layer.Data.Length];
        ResolvedConnectionPoint Point(int index, bool lower)
        {
            var point = coordinates[index] ??= ResolvedMarkConnection.Coordinates(layer.Data[index], layer.AreaRibbon)
                ?? throw new InvalidDataException("Interpolated connections require complete semantic endpoints.");
            return new(point.X, lower ? layer.AreaRibbon ? point.YStart!.Value : 0m
                : (layer.AreaRibbon ? point.YEnd : point.Y)!.Value);
        }
        var result = ImmutableArray.CreateBuilder<ResolvedConnectionGeometry>(layer.Connections.Length);
        foreach (var connection in layer.Connections)
        {
            incoming.TryGetValue((connection.FacetId, connection.SourceIndex), out var previous);
            outgoing.TryGetValue((connection.FacetId, connection.DestinationIndex), out var next);
            var cubic = interpolation == ConnectedInterpolationKind.Smooth && (previous is not null || next is not null);
            ImmutableArray<ResolvedConnectionPoint> Boundary(bool lower)
            {
                var start = Point(connection.SourceIndex, lower);
                var end = Point(connection.DestinationIndex, lower);
                if (interpolation is ConnectedInterpolationKind.StepBefore or ConnectedInterpolationKind.StepAfter)
                {
                    var corner = CartesianInterpolation.StepCorner((start.X, start.Y), (end.X, end.Y), interpolation == ConnectedInterpolationKind.StepBefore);
                    return [start, new(corner.X, corner.Y), end];
                }
                if (!cubic) return [start, end];
                var before = previous is null ? start : Point(previous.SourceIndex, lower);
                var after = next is null ? end : Point(next.DestinationIndex, lower);
                var controls = CartesianInterpolation.CubicControls((before.X, before.Y), (start.X, start.Y), (end.X, end.Y), (after.X, after.Y));
                return [start, new(controls.First.X, controls.First.Y), new(controls.Second.X, controls.Second.Y), end];
            }
            result.Add(new(Boundary(false), layer.Mark == MarkKind.Area ? Boundary(true) : default, cubic));
        }
        return result.MoveToImmutable();
    }
}
