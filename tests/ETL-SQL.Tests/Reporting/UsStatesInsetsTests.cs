using System;
using ETL_SQL.Reporting.Renderers;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public sealed class UsStatesInsetsTests
{
    [Theory]
    [InlineData(-122.33, 47.61)] // Seattle
    [InlineData(-80.19, 25.76)]  // Miami
    [InlineData(-66.99, 44.91)]  // Eastport, Maine
    [InlineData(-117.16, 32.72)] // San Diego
    public void LowerFortyEight_IsUnchanged(double lon, double lat)
    {
        Assert.Equal((lon, lat), UsStatesInsets.Apply(lon, lat));
    }

    [Fact]
    public void Anchorage_MovesBelowTheLowerFortyEight()
    {
        var (lon, lat) = UsStatesInsets.Apply(-149.90, 61.22);
        Assert.InRange(lon, -125, -105);
        Assert.InRange(lat, 22, 32);
    }

    [Fact]
    public void Honolulu_MovesBelowTheLowerFortyEight()
    {
        var (lon, lat) = UsStatesInsets.Apply(-157.86, 21.31);
        Assert.InRange(lon, -112, -98);
        Assert.InRange(lat, 22, 28);
    }

    [Fact]
    public void SanJuan_MovesIntoTheAtlanticBesideFlorida()
    {
        // At 18°N Puerto Rico set the map's bottom edge, well below the Florida Keys (24.5°N).
        var (lon, lat) = UsStatesInsets.Apply(-66.11, 18.47);
        Assert.InRange(lon, -78, -72);
        Assert.InRange(lat, 24.6, 27);
    }

    [Fact]
    public void AlaskaShrinks_HawaiiKeepsItsSize()
    {
        static double Distance((double Lon, double Lat) a, (double Lon, double Lat) b) =>
            Math.Sqrt(Math.Pow(a.Lon - b.Lon, 2) + Math.Pow(a.Lat - b.Lat, 2));

        (double, double) juneau = (-134.42, 58.30), nome = (-165.41, 64.50);
        var ratio = Distance(UsStatesInsets.Apply(juneau.Item1, juneau.Item2), UsStatesInsets.Apply(nome.Item1, nome.Item2)) / Distance(juneau, nome);
        Assert.InRange(ratio, 0.35 * 0.99, 0.35 * 1.01);

        (double, double) hilo = (-155.09, 19.72), lihue = (-159.37, 21.98);
        var hawaii = Distance(UsStatesInsets.Apply(hilo.Item1, hilo.Item2), UsStatesInsets.Apply(lihue.Item1, lihue.Item2));
        Assert.Equal(Distance(hilo, lihue), hawaii, 9);
    }

    [Theory]
    [InlineData(-129.5, 51.0, true)]
    [InlineData(-129.4, 51.0, false)]
    [InlineData(-129.5, 50.9, false)]
    [InlineData(-154.0, 23.0, true)]
    [InlineData(-153.9, 20.0, false)]
    [InlineData(-64.4, 18.3, true)]  // US Virgin Islands move with Puerto Rico
    [InlineData(-64.3, 18.3, false)]
    public void BoxEdges(double lon, double lat, bool moves)
    {
        Assert.Equal(moves, UsStatesInsets.Apply(lon, lat) != (lon, lat));
    }

    [Fact]
    public void DecimalOverload_MatchesDouble()
    {
        var (lon, lat) = UsStatesInsets.Apply(-149.90m, 61.22m);
        var expected = UsStatesInsets.Apply(-149.90, 61.22);
        Assert.Equal(expected.Lon, (double)lon, 6);
        Assert.Equal(expected.Lat, (double)lat, 6);
        Assert.Equal((-122.33m, 47.61m), UsStatesInsets.Apply(-122.33m, 47.61m));
    }

    [Theory]
    [InlineData("US_STATES", true)]
    [InlineData("us-states", true)]
    [InlineData("Us_States", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("US_COUNTIES", false)]
    [InlineData("WORLD", false)]
    public void IsBuiltInUsStates_MatchesTheBundledMapName(string? name, bool expected)
    {
        Assert.Equal(expected, UsStatesInsets.IsBuiltInUsStates(name));
    }
}
