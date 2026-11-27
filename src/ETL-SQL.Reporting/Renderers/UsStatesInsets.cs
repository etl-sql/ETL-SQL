using System;

namespace ETL_SQL.Reporting.Renderers;

/// <summary>
/// Moves Alaska and Hawaii below the lower 48, and Puerto Rico beside Florida, on the bundled US
/// states map, as US maps usually draw them: at their true positions Alaska takes much of the card
/// and Puerto Rico stretches it downward, so the lower 48 shrink.
/// Renderers apply it to outlines and markers alike, before projecting, so markers stay on their
/// states. Points outside both boxes come back unchanged.
/// </summary>
internal static class UsStatesInsets
{
    // Boxes from the bundled us-states.geojson (Alaska lon -178.1..-130.0, lat 51.6..71.4; Hawaii
    // lon -159.8..-154.8, lat 18.9..22.2). No lower-48 point lies west of -125°.
    private const double AlaskaMaxLon = -129.5, AlaskaMinLat = 51.0;
    private const double AlaskaCenterLon = -154.05, AlaskaCenterLat = 61.48;
    private const double AlaskaScale = 0.35;
    private const double AlaskaInsetLon = -117.0, AlaskaInsetLat = 27.5;

    private const double HawaiiMinLon = -161.0, HawaiiMaxLon = -154.0, HawaiiMinLat = 18.0, HawaiiMaxLat = 23.0;
    private const double HawaiiCenterLon = -157.29, HawaiiCenterLat = 20.59;
    private const double HawaiiInsetLon = -105.0, HawaiiInsetLat = 24.5;

    // Puerto Rico (lon -67.3..-65.6, lat 17.9..18.5) would otherwise set the map's bottom edge
    // well below the Florida Keys; it moves into the Atlantic east of Florida, at its own size.
    // The box reaches east to -64.4 so US Virgin Islands markers (lon -65.1..-64.5) move with it.
    private const double PuertoRicoMinLon = -68.0, PuertoRicoMaxLon = -64.4, PuertoRicoMinLat = 17.5, PuertoRicoMaxLat = 19.0;
    private const double PuertoRicoCenterLon = -66.45, PuertoRicoCenterLat = 18.22;
    private const double PuertoRicoInsetLon = -75.0, PuertoRicoInsetLat = 25.5;

    public static (double Lon, double Lat) Apply(double lon, double lat)
    {
        if (lon <= AlaskaMaxLon && lat >= AlaskaMinLat)
            return (AlaskaInsetLon + (lon - AlaskaCenterLon) * AlaskaScale,
                AlaskaInsetLat + (lat - AlaskaCenterLat) * AlaskaScale);
        if (lon is >= HawaiiMinLon and <= HawaiiMaxLon && lat is >= HawaiiMinLat and <= HawaiiMaxLat)
            return (HawaiiInsetLon + (lon - HawaiiCenterLon), HawaiiInsetLat + (lat - HawaiiCenterLat));
        if (lon is >= PuertoRicoMinLon and <= PuertoRicoMaxLon && lat is >= PuertoRicoMinLat and <= PuertoRicoMaxLat)
            return (PuertoRicoInsetLon + (lon - PuertoRicoCenterLon), PuertoRicoInsetLat + (lat - PuertoRicoCenterLat));
        return (lon, lat);
    }

    public static (decimal Lon, decimal Lat) Apply(decimal lon, decimal lat)
    {
        var (x, y) = Apply((double)lon, (double)lat);
        return x == (double)lon && y == (double)lat ? (lon, lat) : ((decimal)x, (decimal)y);
    }

    /// <summary>True for the bundled map's name in any spelling MAP_NAME accepts.</summary>
    public static bool IsBuiltInUsStates(string? mapName) =>
        !string.IsNullOrWhiteSpace(mapName) &&
        mapName.Trim().Replace('-', '_').Equals("US_STATES", StringComparison.OrdinalIgnoreCase);
}
