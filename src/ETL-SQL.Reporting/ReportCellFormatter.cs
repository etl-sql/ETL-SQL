#nullable enable
using System;
using System.Globalization;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Tidies a raw table cell for static export (PDF/Markdown): trims midnight time
    /// noise and rounds full-precision numbers (which otherwise overflow narrow cells
    /// and overlap neighbours), with a hard length cap as a final safety net.
    /// </summary>
    internal static class ReportCellFormatter
    {
        public static string FormatCell(string? raw)
        {
            var text = raw ?? "";
            if (text.Length == 0) return text;

            if (text.EndsWith(" 00:00:00", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 9);

            if (decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                                 CultureInfo.InvariantCulture, out var d)
                && d != decimal.Truncate(d))
            {
                text = Math.Round(d, 2).ToString("0.##", CultureInfo.InvariantCulture);
            }

            return text.Length > 40 ? text.Substring(0, 39) + "…" : text;
        }
    }
}
