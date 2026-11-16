namespace ETL_SQL.Connectors.Shared;

/// <summary>
/// Reads boolean connector options. CREATE CONNECTION stores an unquoted boolean literal as ON/OFF
/// (<c>ENCRYPT = TRUE</c> arrives as <c>"ON"</c>), while quoted values and imported connection
/// strings arrive as TRUE/FALSE, so a connector that compares against "TRUE" alone turns the
/// documented <c>USE_SSL = TRUE</c> into false.
/// </summary>
public static class ConnectorOptionValues
{
    /// <summary>True for TRUE, ON, YES or 1 (any case, surrounding spaces ignored).</summary>
    public static bool IsTrue(string? value) =>
        value?.Trim().ToUpperInvariant() is "TRUE" or "ON" or "YES" or "1";

    /// <summary>The named option as a boolean, or <paramref name="fallback"/> when it is absent.</summary>
    public static bool IsTrue(IReadOnlyDictionary<string, string>? options, string key, bool fallback = false) =>
        options != null && options.TryGetValue(key, out var value) ? IsTrue(value) : fallback;
}
