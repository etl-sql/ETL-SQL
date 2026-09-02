namespace ETL_SQL.Core;

/// <summary>The portable point-marker shapes shared by named and CUSTOM charts.</summary>
public static class PointShapeVocabulary
{
    public const string Default = "CIRCLE";
    public const string DisplayList = "CIRCLE, SQUARE, TRIANGLE, DIAMOND, CROSS, STAR";

    public static bool IsSupported(string? value) => value?.Trim().ToUpperInvariant() is
        "CIRCLE" or "SQUARE" or "TRIANGLE" or "DIAMOND" or "CROSS" or "STAR";

    public static string NormalizeOrDefault(string? value) => IsSupported(value)
        ? value!.Trim().ToUpperInvariant()
        : Default;
}
