using System.Globalization;

namespace ETL_SQL.Core;

/// <summary>Validation and normalization for portable point-marker strokes.</summary>
public static class PointMarkerStroke
{
    public static bool IsPortableColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    public static bool TryNormalizeWidth(string? value, out string normalized)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var width) && width >= 0m)
        {
            normalized = width.ToString("0.############################", CultureInfo.InvariantCulture);
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
