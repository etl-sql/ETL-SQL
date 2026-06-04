#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MiniExcelLibs;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Writes tabular data to a real OOXML <c>.xlsx</c> workbook via MiniExcel.
    ///
    /// The point of using a true xlsx writer (vs. CSV-opened-in-Excel) is
    /// <b>typed cells</b>: numbers and dates are coerced to CLR types so Excel
    /// stores them as numbers/dates instead of mangling them (leading zeros
    /// stripped, long IDs → scientific notation, "1-2"/"MAR1" coerced to dates).
    /// Columns always line up because every cell is addressed, not delimited.
    /// </summary>
    public static class XlsxWriter
    {
        public sealed record Column(string Name, string? Type);

        public sealed record Sheet(
            string Name,
            IReadOnlyList<Column> Columns,
            IEnumerable<IDictionary<string, object?>> Rows);

        /// <summary>Write a single-sheet workbook.</summary>
        public static Task WriteAsync(
            Stream output,
            IReadOnlyList<Column> columns,
            IEnumerable<IDictionary<string, object?>> rows,
            string sheetName = "Data",
            CancellationToken ct = default)
            => WriteWorkbookAsync(output, new[] { new Sheet(sheetName, columns, rows) }, ct);

        /// <summary>Write a workbook with one worksheet per <see cref="Sheet"/>.</summary>
        public static async Task WriteWorkbookAsync(
            Stream output,
            IReadOnlyList<Sheet> sheets,
            CancellationToken ct = default)
        {
            // MiniExcel writes one worksheet per entry when the value is a
            // Dictionary<string, object> of sheetName -> rows.
            var book = new Dictionary<string, object>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in sheets)
            {
                var name = UniqueSheetName(SanitizeSheetName(sheet.Name), usedNames);
                book[name] = Materialize(sheet.Columns, sheet.Rows);
            }

            await MiniExcel.SaveAsAsync(output, book, printHeader: true, excelType: ExcelType.XLSX, cancellationToken: ct);
        }

        // Project each source row onto the column order with per-column type coercion.
        // NOTE: MiniExcel infers headers from the first row's keys, so a sheet with
        // zero rows produces an empty worksheet (acceptable; exports almost always
        // carry data).
        private static IEnumerable<Dictionary<string, object?>> Materialize(
            IReadOnlyList<Column> columns, IEnumerable<IDictionary<string, object?>> rows)
        {
            foreach (var row in rows)
            {
                var mapped = new Dictionary<string, object?>(columns.Count);
                foreach (var col in columns)
                {
                    row.TryGetValue(col.Name, out var raw);
                    mapped[col.Name] = Coerce(raw, col.Type);
                }
                yield return mapped;
            }
        }

        private enum Kind { Text, Number, Date }

        private static Kind GuessKind(string? type)
        {
            var t = (type ?? string.Empty).ToLowerInvariant();
            if (t.Contains("int") || t.Contains("float") || t.Contains("double") ||
                t.Contains("decimal") || t.Contains("numeric") || t.Contains("real") ||
                t.Contains("money") || t.Contains("number"))
                return Kind.Number;
            if (t.Contains("date") || t.Contains("time"))
                return Kind.Date;
            return Kind.Text;
        }

        private static object? Coerce(object? raw, string? type)
        {
            if (raw is null) return null;

            switch (GuessKind(type))
            {
                case Kind.Number:
                    object? num = raw switch
                    {
                        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => raw,
                        _ => decimal.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
                    };
                    if (num is null) return NeutralizeFormula(raw.ToString());   // not actually numeric -> keep text
                    // Integers beyond Excel's ~15 significant digits (e.g. 18-digit IDs)
                    // lose precision / show as scientific notation -> keep them as text.
                    if (IsIntegral(num) &&
                        Math.Abs(Convert.ToDecimal(num, CultureInfo.InvariantCulture)) >= 1_000_000_000_000_000m)
                        return NeutralizeFormula(raw.ToString());
                    return num;

                case Kind.Date:
                    return raw switch
                    {
                        DateTime dt => dt,
                        DateTimeOffset dto => dto.DateTime,
                        _ => DateTime.TryParse(raw.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                            ? parsed
                            : NeutralizeFormula(raw.ToString()),
                    };

                default:
                    return NeutralizeFormula(raw.ToString());
            }
        }

        // Excel interprets a cell beginning with =,+,-,@ (or a leading tab/CR) as a formula.
        // Prefix a single quote so text cells render literally — blocks formula injection
        // (e.g. =HYPERLINK/WEBSERVICE exfiltration) from stored report/dataset content. Only
        // applies to text cells; typed number/date cells are emitted as CLR values, untouched.
        private static string? NeutralizeFormula(string? s) =>
            !string.IsNullOrEmpty(s) && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
                ? "'" + s
                : s;

        private static bool IsIntegral(object? n) =>
            n is sbyte or byte or short or ushort or int or uint or long or ulong
            || (n is decimal d && decimal.Truncate(d) == d);

        // Excel sheet names: <= 31 chars, none of [ ] : * ? / \, and unique per book.
        private static string SanitizeSheetName(string? name)
        {
            var s = string.IsNullOrWhiteSpace(name) ? "Sheet" : name!;
            foreach (var c in new[] { '[', ']', ':', '*', '?', '/', '\\' })
                s = s.Replace(c, ' ');
            s = s.Trim();
            if (s.Length == 0) s = "Sheet";
            return s.Length > 31 ? s.Substring(0, 31) : s;
        }

        private static string UniqueSheetName(string baseName, HashSet<string> used)
            => NameDeduplicator.MakeUnique(baseName, used, maxLength: 31);
    }

    /// <summary>
    /// Makes names unique within a set, appending <c>" (n)"</c> on collision and honoring an
    /// optional max length (Excel sheet names cap at 31 chars). Shared by sheet-name and
    /// column-name de-duplication so the algorithm lives in one place. Collision-safe: a generated
    /// name that clashes with a later raw name is itself bumped, because the set tracks every
    /// emitted name (not just a per-base counter).
    /// </summary>
    internal static class NameDeduplicator
    {
        public static string MakeUnique(string baseName, ISet<string> used, int maxLength = int.MaxValue)
        {
            string Fit(string s) => s.Length > maxLength ? s.Substring(0, maxLength) : s;

            var name = Fit(baseName);
            var i = 2;
            while (!used.Add(name))
            {
                var suffix = $" ({i++})";
                var trimmed = baseName.Length + suffix.Length > maxLength
                    ? baseName.Substring(0, Math.Max(0, maxLength - suffix.Length))
                    : baseName;
                name = trimmed + suffix;
            }
            return name;
        }
    }
}
