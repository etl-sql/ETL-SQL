#nullable enable
using System;
using System.Globalization;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Tidies a raw table cell for static export (PDF/Markdown): trims midnight time
    /// noise and rounds full-precision numbers. PDF can additionally request a hard
    /// length cap because narrow cells otherwise overflow and overlap neighbours.
    /// </summary>
    internal static class ReportCellFormatter
    {
        public static string FormatCell(string? raw)
            => FormatCell(raw, maxLength: null);

        public static string FormatCellForPdf(string? raw)
            => FormatCell(raw, maxLength: 40);

        private static string FormatCell(string? raw, int? maxLength)
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

            return maxLength is > 0 && text.Length > maxLength.Value
                ? text.Substring(0, maxLength.Value - 1) + "…"
                : text;
        }
    }
}
