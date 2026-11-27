using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// The server-side map resolver rejects a whole map when any point falls outside
/// longitude ±180 / latitude ±90, so one antimeridian-crossing island breaks every visual
/// that uses that map (an Aleutian ring at -187° once broke all US_STATES maps).
/// </summary>
public class BuiltInMapCoordinateTests
{
    public static IEnumerable<object[]> BundledMaps()
    {
        var dir = Path.Combine(FindRepositoryRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "maps");
        foreach (var file in Directory.GetFiles(dir, "*.geojson"))
            yield return new object[] { Path.GetFileName(file) };
    }

    [Theory]
    [MemberData(nameof(BundledMaps))]
    public void BundledMap_AllCoordinatesAreInRange(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "ETL-SQL.ReportRuntime", "Resources", "Shared", "maps", fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 32 });
        var bad = new List<string>();
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object) continue;
            var name = feature.TryGetProperty("properties", out var p) && p.TryGetProperty("name", out var n) ? n.ToString() : "?";
            Walk(geometry.GetProperty("coordinates"), name, bad);
        }
        Assert.True(bad.Count == 0, $"{fileName}: {bad.Count} out-of-range points, e.g. {string.Join("; ", bad.GetRange(0, Math.Min(3, bad.Count)))}");
    }

    private static void Walk(JsonElement element, string name, List<string> bad)
    {
        if (element.GetArrayLength() > 0 && element[0].ValueKind == JsonValueKind.Number)
        {
            var lon = element[0].GetDecimal();
            var lat = element[1].GetDecimal();
            if (lon is < -180m or > 180m || lat is < -90m or > 90m) bad.Add($"{name} [{lon}, {lat}]");
            return;
        }
        foreach (var child in element.EnumerateArray()) Walk(child, name, bad);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[] { sourceFilePath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(File.Exists(start) ? Path.GetDirectoryName(start)! : start);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "tests")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
