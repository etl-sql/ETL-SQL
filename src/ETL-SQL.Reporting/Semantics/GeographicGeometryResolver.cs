using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

internal static class GeographicGeometryResolver
{
    private const long MaximumBytes = 5 * 1024 * 1024;
    private const int MaximumFeatures = 10000;
    private const int MaximumCoordinates = 200000;
    private static readonly IReadOnlyDictionary<string, string> BuiltIns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["WORLD"] = "world",
        ["US_STATES"] = "us-states",
        ["US_COUNTIES"] = "us-counties",
        ["MN_COUNTIES"] = "mn-counties",
        ["CANADA_PROVINCES"] = "canada-provinces",
        ["EUROPE"] = "europe"
    };

    internal static ResolvedGeographicGeometry Resolve(GeographicCoordinateSpec spec, string? resolvedFile)
    {
        using var stream = Open(spec, resolvedFile);
        if (stream.Length > MaximumBytes)
            throw new InvalidDataException($"Geographic MAP_FILE exceeds the {MaximumBytes / 1024 / 1024} MiB limit.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "FeatureCollection" ||
            !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Geographic map source must be a GeoJSON FeatureCollection.");

        var output = ImmutableArray.CreateBuilder<GeographicFeature>();
        var coordinateCount = 0;
        foreach (var feature in features.EnumerateArray())
        {
            if (output.Count >= MaximumFeatures)
                throw new InvalidDataException($"Geographic map source exceeds the {MaximumFeatures:N0} feature limit.");
            var key = FeatureKey(feature, spec.FeatureKey);
            var rings = ImmutableArray.CreateBuilder<ImmutableArray<GeographicPoint>>();
            if (feature.TryGetProperty("geometry", out var geometry))
                ReadGeometry(geometry, rings, ref coordinateCount);
            if (rings.Count > 0) output.Add(new GeographicFeature(key, rings.ToImmutable()));
        }
        return new ResolvedGeographicGeometry(spec.Projection,
            spec.SourceKind == GeographicMapSourceKind.BuiltIn ? $"builtin:{spec.Source.ToUpperInvariant()}" : "resolved-file",
            spec.FeatureKey, output.ToImmutable());
    }

    internal static string ResolveMapFile(IExecutionContext context, GeographicCoordinateSpec spec)
    {
        if (spec.SourceKind != GeographicMapSourceKind.File)
            throw new InvalidOperationException("Only file-backed geographic sources have a map path.");
        if (!Path.GetExtension(spec.Source).Equals(".geojson", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Geographic MAP_FILE must use the .geojson extension.");
        return context.ResolvePath(spec.Source);
    }

    private static Stream Open(GeographicCoordinateSpec spec, string? resolvedFile)
    {
        if (spec.SourceKind == GeographicMapSourceKind.File)
        {
            if (string.IsNullOrWhiteSpace(resolvedFile) || !File.Exists(resolvedFile))
                throw new FileNotFoundException($"Geographic MAP_FILE not found: {spec.Source}");
            return new FileStream(resolvedFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        }
        if (!BuiltIns.TryGetValue(spec.Source.Replace('-', '_'), out var resource))
            throw new InvalidDataException($"Unknown geographic MAP_NAME '{spec.Source}'. Allowed values: {string.Join(", ", BuiltIns.Keys)}.");
        return Assembly.GetExecutingAssembly().GetManifestResourceStream($"maps.{resource}.geojson")
            ?? throw new InvalidDataException($"Built-in geographic map '{spec.Source}' is unavailable.");
    }

    private static string FeatureKey(JsonElement feature, string name)
    {
        if (!feature.TryGetProperty("properties", out var properties) ||
            !properties.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;
        return value.ToString();
    }

    private static void ReadGeometry(JsonElement geometry, ImmutableArray<ImmutableArray<GeographicPoint>>.Builder rings, ref int count)
    {
        if (!geometry.TryGetProperty("type", out var type) || !geometry.TryGetProperty("coordinates", out var coordinates)) return;
        if (type.GetString() == "Polygon") AddPolygon(coordinates, rings, ref count);
        else if (type.GetString() == "MultiPolygon")
            foreach (var polygon in coordinates.EnumerateArray()) AddPolygon(polygon, rings, ref count);
    }

    private static void AddPolygon(JsonElement polygon, ImmutableArray<ImmutableArray<GeographicPoint>>.Builder rings, ref int count)
    {
        foreach (var ringElement in polygon.EnumerateArray())
        {
            var ring = ImmutableArray.CreateBuilder<GeographicPoint>();
            foreach (var point in ringElement.EnumerateArray())
            {
                if (++count > MaximumCoordinates)
                    throw new InvalidDataException($"Geographic map source exceeds the {MaximumCoordinates:N0} coordinate limit.");
                if (point.GetArrayLength() < 2 || !point[0].TryGetDecimal(out var longitude) || !point[1].TryGetDecimal(out var latitude) ||
                    longitude is < -180m or > 180m || latitude is < -90m or > 90m)
                    throw new InvalidDataException("Geographic map source contains an invalid longitude/latitude coordinate.");
                ring.Add(new GeographicPoint(longitude, latitude));
            }
            if (ring.Count >= 3) rings.Add(ring.ToImmutable());
        }
    }
}
