using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Data;
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

            if (records.Any(record => record.Kind == EngineRecordKind.Portal))
            {
                evaluator.DatasetRegistry = new CorpusDatasetRegistry(directory);
                evaluator.DatasetCallerContext = "IsAdmin=true";
                evaluator.DatasetAtRestKey = "engine-corpus-at-rest-key";
            }

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
                case EngineRecordKind.Portal:
                    return;

                case EngineRecordKind.File:
                    File.WriteAllText(ResolveCorpusPath(directory, record.Name!, where), record.Body);
                    return;

                case EngineRecordKind.FileExists:
                {
                    var path = ResolveCorpusPath(directory, record.Name!, where);
                    Assert.True(File.Exists(path), $"{where}: expected file to exist: {path}");
                    return;
                }

                case EngineRecordKind.FileContains:
                {
                    var path = ResolveCorpusPath(directory, record.Name!, where);
                    Assert.True(File.Exists(path), $"{where}: expected file to exist: {path}");
                    var contents = await File.ReadAllTextAsync(path);
                    Assert.Contains(record.Body, contents, StringComparison.Ordinal);
                    return;
                }

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

        private static string ResolveCorpusPath(string directory, string relativePath, string where)
        {
            var candidate = Path.Combine(directory, relativePath);
            Assert.True(
                SafePath.TryResolveWithinRoot(directory, candidate, out var resolved),
                $"{where}: corpus path must remain beneath its run directory: {relativePath}");
            return resolved;
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

        private sealed class CorpusDatasetRegistry(string root) : IDatasetRegistry
        {
            private readonly Dictionary<string, DatasetMetadata> _items = new(StringComparer.OrdinalIgnoreCase);
            private int _nextId = 1;

            public Task<int> RegisterOrUpdate(DatasetMetadata metadata)
            {
                if (metadata.Id == 0) metadata.Id = _nextId++;
                _items[metadata.Name] = metadata;
                return Task.FromResult(metadata.Id);
            }

            public Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "") =>
                Task.FromResult(_items.TryGetValue(name, out var value) ? value : null);

            public Task<bool> Exists(string name) => Task.FromResult(_items.ContainsKey(name));
            public Task<bool> CanEditAsync(string name, string callerPermissions) => Task.FromResult(_items.ContainsKey(name));
            public Task<bool> CanRefreshAsync(string name, string callerPermissions) => Task.FromResult(_items.ContainsKey(name));
            public Task SetStale(string name) => Task.CompletedTask;
            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions) =>
                Task.FromResult<IEnumerable<DatasetMetadata>>(_items.Values.ToList());
            public Task Delete(string name)
            {
                _items.Remove(name);
                return Task.CompletedTask;
            }
            public Task RegisterRefreshJobAsync(int reportId, string orchestratorJobName, string refreshInterval) =>
                Task.CompletedTask;
            public Task<DatasetPublishTarget?> AuthorizePublishAsync(string targetFolderPath, string callerPermissions) =>
                Task.FromResult<DatasetPublishTarget?>(new DatasetPublishTarget(1, targetFolderPath, 1));
            public Task AuditPublishAsync(int? userId, string datasetName, string targetFolderPath, bool succeeded, string? failureReason = null) =>
                Task.CompletedTask;
            public string BuildDatasetFilePath(int datasetId, string name) =>
                Path.Combine(root, $"{name.TrimStart('&', '#')}_{datasetId}.parquet");
        }
    }
}
