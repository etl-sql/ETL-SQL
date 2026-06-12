using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ETL_SQL.SqlLogicTests
{
    public enum SltRecordType { Statement, Query, Halt, HashThreshold, SkipIf, OnlyIf }
    public enum SltSortMode { None, NoSort, RowSort, ValueSort }

    public class SltRecord
    {
        public SltRecordType Type { get; set; }
        public string? Sql { get; set; }
        public bool ExpectSuccess { get; set; } = true;
        public string? ExpectedResult { get; set; }
        public string? ColumnTypes { get; set; }
        public string? Label { get; set; }
        public SltSortMode SortMode { get; set; }
        public int LineNumber { get; set; }
        /// <summary>Engine name from skipif/onlyif directive. Used by the runner to decide whether to execute.</summary>
        public string? EngineCondition { get; set; }
    }

    public static class SltParser
    {
        public static IEnumerable<SltRecord> ParseFile(string path)
        {
            using var reader = new StreamReader(path);
            var buffer = new LineBuffer(reader);

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

                if (cmd == "statement")
                {
                    var record = new SltRecord { Type = SltRecordType.Statement, LineNumber = buffer.LineNumber };
                    record.ExpectSuccess = parts.Length > 1 && parts[1].ToLowerInvariant() == "ok";
                    buffer.Consume();
                    record.Sql = ReadSql(buffer);
                    yield return record;
                }
                else if (cmd == "query")
                {
                    var record = new SltRecord { Type = SltRecordType.Query, LineNumber = buffer.LineNumber };
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
                    yield return record;
                }
                else if (cmd == "halt")
                {
                    yield return new SltRecord { Type = SltRecordType.Halt, LineNumber = buffer.LineNumber };
                    yield break;
                }
                else if (cmd == "skipif" || cmd == "onlyif")
                {
                    var engineName = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
                    var recordType = cmd == "skipif" ? SltRecordType.SkipIf : SltRecordType.OnlyIf;
                    buffer.Consume();
                    while (buffer.TryPeek(out var blank) && string.IsNullOrWhiteSpace(blank)) buffer.Consume();
                    if (buffer.TryPeek(out var innerLine))
                    {
                        var innerParts = innerLine!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var innerCmd = innerParts[0].ToLowerInvariant();
                        if (innerCmd == "statement")
                        {
                            var record = new SltRecord { Type = recordType, LineNumber = buffer.LineNumber, EngineCondition = engineName };
                            record.ExpectSuccess = innerParts.Length > 1 && innerParts[1].ToLowerInvariant() == "ok";
                            buffer.Consume();
                            record.Sql = ReadSql(buffer);
                            yield return record;
                        }
                        else if (innerCmd == "query")
                        {
                            var record = new SltRecord { Type = recordType, LineNumber = buffer.LineNumber, EngineCondition = engineName };
                            if (innerParts.Length > 1) record.ColumnTypes = innerParts[1];
                            int labelIdx = 2;
                            if (innerParts.Length > 2)
                            {
                                switch (innerParts[2].ToLowerInvariant())
                                {
                                    case "nosort": record.SortMode = SltSortMode.NoSort; labelIdx = 3; break;
                                    case "rowsort": record.SortMode = SltSortMode.RowSort; labelIdx = 3; break;
                                    case "valuesort": record.SortMode = SltSortMode.ValueSort; labelIdx = 3; break;
                                }
                            }
                            if (innerParts.Length > labelIdx) record.Label = innerParts[labelIdx];
                            buffer.Consume();
                            record.Sql = ReadSql(buffer);
                            record.ExpectedResult = ReadResults(buffer);
                            yield return record;
                        }
                        else
                        {
                            buffer.Consume();
                        }
                    }
                }
                else if (cmd == "hash-threshold")
                {
                    buffer.Consume();
                }
                else
                {
                    buffer.Consume();
                }
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
