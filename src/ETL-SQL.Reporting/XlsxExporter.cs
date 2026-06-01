#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Exports a report's TABLE visuals to a native <c>.xlsx</c> workbook — one
    /// worksheet per visual (a real advantage over the single-stream CSV export).
    ///
    /// Report cells are already display strings (formatted as the user sees them),
    /// so they are written as text: columns line up because each cell is addressed,
    /// and Excel will not re-coerce the displayed values. (Typed numeric/date cells
    /// are produced for the dataset viewer export, where real column types exist.)
    /// </summary>
    public sealed class XlsxExporter
    {
        private readonly CsvRenderer _selector = new();

        public async Task<byte[]> ExportAsync(ReportManifest manifest, string? visualName, CancellationToken ct = default)
        {
            var visuals = _selector.SelectExportVisuals(manifest, visualName);

            var sheets = new List<XlsxWriter.Sheet>();
            foreach (var v in visuals)
            {
                var names = Uniquify(v.Columns);
                var columns = names.Select(n => new XlsxWriter.Column(n, null)).ToList();

                var rows = v.Rows.Select(r =>
                {
                    IDictionary<string, object?> dict = new Dictionary<string, object?>(names.Count);
                    for (int ci = 0; ci < names.Count; ci++)
                        dict[names[ci]] = ci < r.Count ? r[ci] : null;
                    return dict;
                });

                sheets.Add(new XlsxWriter.Sheet(v.Name, columns, rows));
            }

            using var ms = new MemoryStream();
            await XlsxWriter.WriteWorkbookAsync(ms, sheets, ct);
            return ms.ToArray();
        }

        // Excel headers (and our row dictionaries) need unique, non-empty column keys.
        private static List<string> Uniquify(IReadOnlyList<string> names)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(names.Count);
            foreach (var raw in names)
            {
                var name = string.IsNullOrEmpty(raw) ? "Column" : raw;
                if (seen.TryGetValue(name, out var count))
                {
                    seen[name] = count + 1;
                    name = $"{name} ({count + 1})";
                }
                else
                {
                    seen[name] = 1;
                }
                result.Add(name);
            }
            return result;
        }
    }
}
