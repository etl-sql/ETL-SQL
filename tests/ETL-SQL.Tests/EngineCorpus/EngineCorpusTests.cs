using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.EngineCorpus
{
    /// <summary>
    /// Runs every <c>tests/engine_corpus/*.etest</c> file.
    ///
    /// <para>One test per corpus file rather than one for the lot, so a failure names the file and
    /// the others still run. Each file gets a fresh evaluator and its own directory, because a
    /// corpus that shares state between files reports the order it ran in as much as the behaviour
    /// it tested.</para>
    /// </summary>
    public class EngineCorpusTests
    {
        [Theory]
        [MemberData(nameof(CorpusFiles))]
        public async Task CorpusFilePasses(string fileName)
        {
            var path = Path.Combine(CorpusRoot(), fileName);
            var records = EngineCorpusParser.ParseFile(path);

            Assert.True(records.Count > 0, $"{fileName} parsed to no records at all.");

            var directory = Path.Combine(
                Path.GetTempPath(), "etlsql-engine-corpus", Path.GetFileNameWithoutExtension(fileName),
                Guid.NewGuid().ToString("n")[..8]);
            Directory.CreateDirectory(directory);

            var provider = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();

            try
            {
                foreach (var record in records)
                    await RunRecord(record, evaluator, directory, fileName);
            }
            finally
            {
                await evaluator.DisposeAsync();
                await provider.DisposeAsync();
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }

        private static async Task RunRecord(
            EngineRecord record, Evaluator evaluator, string directory, string fileName)
        {
            var where = $"{fileName} line {record.LineNumber}";

            switch (record.Kind)
            {
                case EngineRecordKind.File:
                    File.WriteAllText(Path.Combine(directory, record.Name!), record.Body);
                    return;

                case EngineRecordKind.StatementOk:
                {
                    var error = await RunCatching(evaluator, record.Body, directory);
                    Assert.True(error == null, $"{where}: expected success but got:\n  {error}\n\nSQL:\n{record.Body}");
                    return;
                }

                case EngineRecordKind.StatementError:
                {
                    var error = await RunCatching(evaluator, record.Body, directory);
                    Assert.True(error != null,
                        $"{where}: expected an error but the statement succeeded.\n\nSQL:\n{record.Body}\n\n"
                        + "A load that accepts what it should reject is the failure mode this corpus "
                        + "exists to catch: it reports exactly what a clean load reports.");
                    if (record.ExpectedError != null)
                    {
                        Assert.True(error!.Contains(record.ExpectedError, StringComparison.OrdinalIgnoreCase),
                            $"{where}: expected the error to mention '{record.ExpectedError}' but got:\n  {error}");
                    }
                    return;
                }

                case EngineRecordKind.Query:
                {
                    var error = await RunCatching(evaluator, record.Body, directory);
                    Assert.True(error == null, $"{where}: query failed:\n  {error}\n\nSQL:\n{record.Body}");

                    var actual = RenderRows(evaluator.LastResult);
                    var expected = record.ExpectedRows!;

                    Assert.True(actual.SequenceEqual(expected, StringComparer.Ordinal),
                        $"{where}: result mismatch.\n\nSQL:\n{record.Body}\n\n"
                        + $"expected ({expected.Count} rows):\n  {string.Join("\n  ", expected)}\n\n"
                        + $"actual ({actual.Count} rows):\n  {string.Join("\n  ", actual)}");
                    return;
                }
            }
        }

        private static async Task<string?> RunCatching(Evaluator evaluator, string sql, string directory)
        {
            var expanded = sql.Replace("${dir}", directory.Replace("\\", "\\\\"));
            try
            {
                await evaluator.Evaluate(new Lexer(expanded).TokenizeToScript());
                return null;
            }
            catch (Exception ex)
            {
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Rows as pipe-joined values. Numbers are normalized because every integral type is a
        /// decimal at runtime, so an expected <c>2</c> would otherwise have to be written
        /// <c>2.000</c> to match, which records the storage type rather than the answer.
        /// </summary>
        private static IReadOnlyList<string> RenderRows(DataTable? table)
        {
            if (table == null) return Array.Empty<string>();

            return table.Rows
                .Select(row => string.Join("|", table.ColumnNames.Select(c => RenderValue(row[c]))))
                .ToList();
        }

        private static string RenderValue(object? value) => value switch
        {
            null => "NULL",
            decimal d => d == Math.Truncate(d) && Math.Abs(d) < 1e18m
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString(CultureInfo.InvariantCulture),
            double f => f.ToString("G15", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? "NULL"
        };

        public static TheoryData<string> CorpusFiles()
        {
            var data = new TheoryData<string>();
            var root = CorpusRoot();
            if (!Directory.Exists(root)) return data;

            foreach (var file in Directory.GetFiles(root, "*.etest").OrderBy(f => f, StringComparer.Ordinal))
                data.Add(Path.GetFileName(file));

            return data;
        }

        private static string CorpusRoot()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "engine_corpus"));
            if (Directory.Exists(candidate)) return candidate;

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "engine_corpus"));
        }
    }
}
