using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ETL_SQL.SqlLogicTests
{
    public enum SltRecordType { Statement, Query, Halt, HashThreshold }
    public enum SltSortMode { None, NoSort, RowSort, ValueSort }

    /// <summary>One <c>skipif</c>/<c>onlyif</c> directive guarding the record that follows it.</summary>
    public readonly record struct SltCondition(bool OnlyIf, string Engine);

    public class SltRecord
    {
        public SltRecordType Type { get; set; }
        public string? Sql { get; set; }
        public bool ExpectSuccess { get; set; } = true;

        /// <summary>
        /// Text after <c>statement error</c>, when given: the failure's message must contain it
        /// (ignoring case). Null accepts any failure.
        /// </summary>
        public string? ExpectedError { get; set; }
        public string? ExpectedResult { get; set; }
        public string? ColumnTypes { get; set; }
        public string? Label { get; set; }
        public SltSortMode SortMode { get; set; }
        public int LineNumber { get; set; }

        /// <summary>
        /// Every <c>skipif</c>/<c>onlyif</c> directive stacked above the record, in file order.
        /// The record applies to an engine only when no <c>skipif</c> names it and every
        /// <c>onlyif</c> does.
        /// </summary>
        public List<SltCondition> Conditions { get; } = new();

        public bool AppliesTo(string engine) =>
            Conditions.All(c => c.OnlyIf == string.Equals(c.Engine, engine, StringComparison.OrdinalIgnoreCase));
    }

    public static class SltParser
    {
        public static IEnumerable<SltRecord> ParseFile(string path)
        {
            using var reader = new StreamReader(path);
            var buffer = new LineBuffer(reader);
            var pending = new List<SltCondition>();

            while (buffer.TryPeek(out var line))
            {
                var trimmed = line!.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                {
                    buffer.Consume();
                    continue;
                }

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();
                SltRecord? record = null;

                if (cmd == "skipif" || cmd == "onlyif")
                {
                    // Directives stack: collect them all and attach them to the next record.
                    pending.Add(new SltCondition(cmd == "onlyif", parts.Length > 1 ? parts[1].ToLowerInvariant() : ""));
                    buffer.Consume();
                    continue;
                }
                else if (cmd == "statement")
                {
                    record = new SltRecord { Type = SltRecordType.Statement, LineNumber = buffer.LineNumber };
                    record.ExpectSuccess = parts.Length > 1 && parts[1].ToLowerInvariant() == "ok";
                    if (!record.ExpectSuccess && parts.Length > 2) record.ExpectedError = string.Join(' ', parts.Skip(2));
                    buffer.Consume();
                    record.Sql = ReadSql(buffer);
                }
                else if (cmd == "query")
                {
                    record = new SltRecord { Type = SltRecordType.Query, LineNumber = buffer.LineNumber };
                    if (parts.Length > 1) record.ColumnTypes = parts[1];
                    int labelIdx = 2;
                    if (parts.Length > 2)
                    {
                        switch (parts[2].ToLowerInvariant())
                        {
                            case "nosort": record.SortMode = SltSortMode.NoSort; labelIdx = 3; break;
                            case "rowsort": record.SortMode = SltSortMode.RowSort; labelIdx = 3; break;
                            case "valuesort": record.SortMode = SltSortMode.ValueSort; labelIdx = 3; break;
                        }
                    }
                    if (parts.Length > labelIdx) record.Label = parts[labelIdx];
                    buffer.Consume();
                    record.Sql = ReadSql(buffer);
                    record.ExpectedResult = ReadResults(buffer);
                }
                else if (cmd == "halt")
                {
                    // A guarded halt ("onlyif mssql / halt") stops only the engines it applies to,
                    // so the reader keeps going and the caller decides via AppliesTo.
                    record = new SltRecord { Type = SltRecordType.Halt, LineNumber = buffer.LineNumber };
                    buffer.Consume();
                }
                else
                {
                    // hash-threshold and unknown commands: the directives above them guard nothing else.
                    buffer.Consume();
                    pending.Clear();
                    continue;
                }

                record.Conditions.AddRange(pending);
                pending.Clear();
                yield return record;
            }
        }
        private static string ReadSql(LineBuffer buffer)
        {
            var sb = new StringBuilder();
            while (buffer.TryPeek(out var line) && !string.IsNullOrWhiteSpace(line) && line!.Trim() != "----")
            {
                sb.AppendLine(line);
                buffer.Consume();
            }
            return sb.ToString().Trim();
        }

        private static string ReadResults(LineBuffer buffer)
        {
            if (!buffer.TryPeek(out var sep) || sep!.Trim() != "----") return "";
            buffer.Consume();
            var sb = new StringBuilder();
            while (buffer.TryPeek(out var line) && !string.IsNullOrWhiteSpace(line) && line!.Trim() != "----")
            {
                sb.AppendLine(line);
                buffer.Consume();
            }
            return sb.ToString();
        }

        private sealed class LineBuffer(StreamReader reader)
        {
            private string? _peeked;
            private bool _hasPeeked;
            public int LineNumber { get; private set; }

            public bool TryPeek(out string? line)
            {
                if (!_hasPeeked)
                {
                    _peeked = reader.ReadLine();
                    _hasPeeked = true;
                    if (_peeked != null) LineNumber++;
                }
                line = _peeked;
                return _peeked != null;
            }

            public void Consume()
            {
                _hasPeeked = false;
                _peeked = null;
            }
        }
    }
}
