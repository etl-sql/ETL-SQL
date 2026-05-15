using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ETL_SQL.SqlLogicTests
{
    public enum SltRecordType { Statement, Query, Halt, HashThreshold, SkipIf, OnlyIf }

    public class SltRecord
    {
        public SltRecordType Type { get; set; }
        public string? Sql { get; set; }
        public bool ExpectSuccess { get; set; } = true;
        public string? ExpectedResult { get; set; }
        public string? ColumnTypes { get; set; }
        public string? Label { get; set; }
        public int LineNumber { get; set; }
        /// <summary>Engine name from skipif/onlyif directive. Used by the runner to decide whether to execute.</summary>
        public string? EngineCondition { get; set; }
    }

    public static class SltParser
    {
        public static IEnumerable<SltRecord> ParseFile(string path)
        {
            var lines = File.ReadAllLines(path);
            int i = 0;
            while (i < lines.Length)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                {
                    i++;
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();

                if (cmd == "statement")
                {
                    var record = new SltRecord { Type = SltRecordType.Statement, LineNumber = i + 1 };
                    record.ExpectSuccess = parts[1].ToLowerInvariant() == "ok";
                    i++;
                    record.Sql = ReadSql(lines, ref i);
                    yield return record;
                }
                else if (cmd == "query")
                {
                    var record = new SltRecord { Type = SltRecordType.Query, LineNumber = i + 1 };
                    if (parts.Length > 1) record.ColumnTypes = parts[1];
                    if (parts.Length > 2) record.Label = parts[2];
                    i++;
                    record.Sql = ReadSql(lines, ref i);
                    record.ExpectedResult = ReadResults(lines, ref i);
                    yield return record;
                }
                else if (cmd == "halt")
                {
                    yield return new SltRecord { Type = SltRecordType.Halt, LineNumber = i + 1 };
                    yield break;
                }
                else if (cmd == "skipif" || cmd == "onlyif")
                {
                    var engineName = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
                    var recordType = cmd == "skipif" ? SltRecordType.SkipIf : SltRecordType.OnlyIf;
                    i++;
                    // Consume and yield the following statement/query so the runner can decide whether to run it
                    while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
                    if (i < lines.Length)
                    {
                        var innerLine = lines[i].Trim();
                        var innerParts = innerLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var innerCmd = innerParts[0].ToLowerInvariant();
                        if (innerCmd == "statement")
                        {
                            var record = new SltRecord { Type = recordType, LineNumber = i + 1, EngineCondition = engineName };
                            record.ExpectSuccess = innerParts.Length > 1 && innerParts[1].ToLowerInvariant() == "ok";
                            i++;
                            record.Sql = ReadSql(lines, ref i);
                            yield return record;
                        }
                        else if (innerCmd == "query")
                        {
                            var record = new SltRecord { Type = recordType, LineNumber = i + 1, EngineCondition = engineName };
                            if (innerParts.Length > 1) record.ColumnTypes = innerParts[1];
                            if (innerParts.Length > 2) record.Label = innerParts[2];
                            i++;
                            record.Sql = ReadSql(lines, ref i);
                            record.ExpectedResult = ReadResults(lines, ref i);
                            yield return record;
                        }
                        else
                        {
                            i++; // Unknown inner directive, skip it
                        }
                    }
                }
                else if (cmd == "hash-threshold")
                {
                    i++; // hash-threshold is not implemented; consume the line
                }
                else
                {
                    i++;
                }
            }
        }

        private static string ReadSql(string[] lines, ref int i)
        {
            var sb = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Trim() != "----")
            {
                sb.AppendLine(lines[i]);
                i++;
            }
            return sb.ToString().Trim();
        }

        private static string ReadResults(string[] lines, ref int i)
        {
            if (i >= lines.Length || lines[i].Trim() != "----") return "";
            i++; // skip ----
            var sb = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Trim() != "----")
            {
                sb.AppendLine(lines[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
