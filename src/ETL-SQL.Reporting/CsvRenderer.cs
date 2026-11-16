using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ETL_SQL.Reporting
{
    public sealed class CsvRenderer
    {
        public string Render(ReportManifest manifest)
        {
            return RenderVisuals(SelectTableVisuals(manifest, skipErrored: false), includeVisualNamesWhenMultiple: false);
        }

        public string Render(ReportManifest manifest, string? visualName, bool includeVisualNamesWhenMultiple)
        {
            return RenderVisuals(SelectExportVisuals(manifest, visualName), includeVisualNamesWhenMultiple);
        }

        public IReadOnlyList<VisualManifest> SelectExportVisuals(ReportManifest manifest, string? visualName)
        {
            if (!string.IsNullOrWhiteSpace(visualName))
            {
                return manifest.Visuals
                    .Where(v => string.Equals(v.Name, visualName, StringComparison.OrdinalIgnoreCase))
                    .Take(1)
                    .ToList();
            }

            return SelectTableVisuals(manifest, skipErrored: true);
        }

        private static IReadOnlyList<VisualManifest> SelectTableVisuals(ReportManifest manifest, bool skipErrored)
        {
            return manifest.Visuals
                .Where(v => string.Equals(v.VisualType, "TABLE", StringComparison.OrdinalIgnoreCase)
                         && (!skipErrored || v.Error is null)
                         && v.Columns.Count > 0)
                .ToList();
        }

        private static string RenderVisuals(IReadOnlyList<VisualManifest> visuals, bool includeVisualNamesWhenMultiple)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var visual in visuals)
            {
                if (!first)
                    sb.AppendLine().AppendLine();
                first = false;

                if (includeVisualNamesWhenMultiple && visuals.Count > 1)
                    sb.AppendLine(CsvField(visual.Name));

                sb.AppendLine(string.Join(",", visual.Columns.Select(CsvField)));

                foreach (var row in visual.Rows)
                {
                    sb.AppendLine(string.Join(",", visual.Columns.Select((_, ci) =>
                        CsvField(ci < row.Count ? row[ci] : null))));
                }
            }

            return sb.ToString();
        }

        private static string CsvField(string? value)
        {
            if (value is null)
                return string.Empty;

            value = NeutralizeFormula(value);

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }

        // Excel/Sheets read a cell beginning with =, +, -, @ (or a leading tab/CR) as a formula, so
        // text such as =HYPERLINK(...) gets a leading quote and renders as literal text. Manifest
        // cells carry numbers in invariant form, and a number ("-5", "+1.5E3") is left untouched so
        // a negative value never turns into text.
        private static string NeutralizeFormula(string value) =>
            value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
                && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? "'" + value
                : value;
    }
}
