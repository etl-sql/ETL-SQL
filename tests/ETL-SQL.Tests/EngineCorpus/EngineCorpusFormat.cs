using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ETL_SQL.Tests.EngineCorpus
{
    public enum EngineRecordKind
    {
        /// <summary>Writes an embedded fixture file into the run directory.</summary>
        File,
        /// <summary>Runs the file with a minimal portal dataset registry and at-rest key.</summary>
        Portal,
        /// <summary>Asserts that a path beneath the run directory exists.</summary>
        FileExists,
        /// <summary>Asserts that a file beneath the run directory contains the expected text.</summary>
        FileContains,
        /// <summary>Runs SQL that must succeed.</summary>
        StatementOk,
        /// <summary>Runs SQL that must fail, with a message containing the expected substring.</summary>
        StatementError,
        /// <summary>Runs SQL and compares the result rows to the recorded ones.</summary>
        Query
    }

    public sealed record EngineRecord(
        EngineRecordKind Kind,
        string Body,
        int LineNumber,
        string? Name = null,
        string? ExpectedError = null,
        IReadOnlyList<string>? ExpectedRows = null);

    /// <summary>
    /// A corpus format for the surface SLT cannot describe.
    ///
    /// <para>The SQLite corpus is a <i>conformance</i> corpus: it asks whether <c>SELECT a+b</c>
    /// returns the right values, and it is good at that. It has no way to express a file on disk, a
    /// connector, or a load — so <c>BULK INSERT</c>, <c>EXPORT</c>, <c>DATASET</c>, quarantine
    /// routing and <c>EXPECT</c> have zero corpus coverage between them, not by oversight but by
    /// construction. ETL-SQL is an engine that speaks SQL rather than a database, and that surface
    /// is most of what makes it one.</para>
    ///
    /// <para>The format deliberately mirrors SLT's — <c>statement ok</c>, <c>query</c>, results
    /// after a <c>----</c> rule — so that the two read alike, with one addition: a <c>file</c>
    /// record embeds a fixture directly in the test, which is what makes a load testable without a
    /// checked-in binary or a fixture directory to keep in sync. <c>${dir}</c> in any SQL body
    /// expands to the run directory, escaped for a SQL string literal.</para>
    /// </summary>
    public static class EngineCorpusParser
    {
        private const string Rule = "----";

        public static IReadOnlyList<EngineRecord> ParseFile(string path) =>
            Parse(File.ReadAllLines(path));

        public static IReadOnlyList<EngineRecord> Parse(IReadOnlyList<string> lines)
        {
            var records = new List<EngineRecord>();
            int i = 0;

            while (i < lines.Count)
            {
                var line = lines[i];

                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                {
                    i++;
                    continue;
                }

                int startLine = i + 1;
                var directive = line.Trim();

                if (directive.Equals("portal", StringComparison.OrdinalIgnoreCase))
                {
                    records.Add(new EngineRecord(EngineRecordKind.Portal, "", startLine));
                    i++;
                    continue;
                }

                if (directive.StartsWith("assert file exists ", StringComparison.OrdinalIgnoreCase))
                {
                    records.Add(new EngineRecord(
                        EngineRecordKind.FileExists,
                        "",
                        startLine,
                        Name: directive["assert file exists ".Length..].Trim()));
                    i++;
                    continue;
                }

                if (directive.StartsWith("assert file contains ", StringComparison.OrdinalIgnoreCase))
                {
                    var argument = directive["assert file contains ".Length..].Trim();
                    var separator = argument.IndexOf(' ');
                    if (separator <= 0 || separator == argument.Length - 1)
                        throw new FormatException(
                            $"Line {startLine}: assert file contains requires '<path> <expected text>'.");
                    records.Add(new EngineRecord(
                        EngineRecordKind.FileContains,
                        argument[(separator + 1)..],
                        startLine,
                        Name: argument[..separator]));
                    i++;
                    continue;
                }

                if (directive.StartsWith("file ", StringComparison.OrdinalIgnoreCase))
                {
                    var name = directive[5..].Trim();
                    i++;
                    var content = ReadUntilRule(lines, ref i);
                    records.Add(new EngineRecord(
                        EngineRecordKind.File, string.Join(Environment.NewLine, content), startLine, Name: name));
                    continue;
                }

                if (directive.Equals("statement ok", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    records.Add(new EngineRecord(
                        EngineRecordKind.StatementOk, ReadBody(lines, ref i), startLine));
                    continue;
                }

                if (directive.StartsWith("statement error", StringComparison.OrdinalIgnoreCase))
                {
                    var expected = directive["statement error".Length..].Trim();
                    i++;
                    records.Add(new EngineRecord(
                        EngineRecordKind.StatementError, ReadBody(lines, ref i), startLine,
                        ExpectedError: expected.Length == 0 ? null : expected));
                    continue;
                }

                if (directive.Equals("query", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    var sql = ReadUntilRule(lines, ref i);
                    var expected = ReadBody(lines, ref i);
                    records.Add(new EngineRecord(
                        EngineRecordKind.Query, string.Join(Environment.NewLine, sql), startLine,
                        ExpectedRows: expected.Length == 0
                            ? Array.Empty<string>()
                            : expected.Split('\n').Select(r => r.TrimEnd('\r')).ToArray()));
                    continue;
                }

                throw new FormatException(
                    $"Line {startLine}: unrecognized directive '{directive}'. "
                    + "Expected one of: portal, file <name>, assert file exists <path>, "
                    + "assert file contains <path> <text>, statement ok, statement error [substring], query.");
            }

            return records;
        }

        /// <summary>Lines up to the next <c>----</c> rule, which is consumed.</summary>
        private static List<string> ReadUntilRule(IReadOnlyList<string> lines, ref int i)
        {
            var body = new List<string>();
            while (i < lines.Count && lines[i].Trim() != Rule)
            {
                body.Add(lines[i]);
                i++;
            }
            if (i < lines.Count) i++; // consume the rule
            return body;
        }

        /// <summary>Lines up to the next blank line, which is consumed.</summary>
        private static string ReadBody(IReadOnlyList<string> lines, ref int i)
        {
            var body = new List<string>();
            while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
            {
                body.Add(lines[i]);
                i++;
            }
            return string.Join("\n", body).Trim();
        }
    }
}
