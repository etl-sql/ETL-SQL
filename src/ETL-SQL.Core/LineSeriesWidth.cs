using System.Globalization;

namespace ETL_SQL.Core;

/// <summary>Validation and normalization for portable line-series widths.</summary>
public static class LineSeriesWidth
{
    public const decimal Minimum = 0.1m;
    public const decimal Maximum = 10m;

    public static bool TryNormalize(string? value, out string normalized)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var width) &&
            width is >= Minimum and <= Maximum)
        {
            normalized = width.ToString("0.############################", CultureInfo.InvariantCulture);
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
