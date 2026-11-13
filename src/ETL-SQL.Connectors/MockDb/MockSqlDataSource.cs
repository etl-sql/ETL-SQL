using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.MockDb
{
    public class MockSqlDataSource : IDatabaseSource
    {
        private readonly string _connectionString;
        public string ConnectionString => _connectionString;
        private readonly string _dialect;
        private readonly Dictionary<string, DataTable> _mockTables = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger _logger;
        private readonly IExecutionContext _context;
        public string Path => "MOCK";
        private readonly Dictionary<string, string>? _options;
        public string ConnectorType => "MOCKDB";
        public Dictionary<string, string>? Options => _options ?? new Dictionary<string, string>();
        public IDataSource WithTable(string tableName)
        {
            _activeTable = tableName;
            return this;
        }
        private string? _activeTable;

        public string Dialect => _dialect;

        // MOCKDB holds DataTables in memory; there is no SQL engine behind it. ExecuteRawSql is a
        // string matcher that understands a projection and one `WHERE col = val` and silently drops
        // everything else, so claiming pushdown handed it whole statements it could not honour:
        // a GROUP BY came back ungrouped and an aggregate came back as a column of nulls, with no
        // error to say so. Reporting the truth routes those statements through the engine's own
        // execution path, which is where grouping and aggregation actually live.
        public bool SupportsSqlPushdown => false;

        private readonly IMockDataSeeder _seeder;
        private readonly Lazy<Task> _initTask;

        public MockSqlDataSource(IExecutionContext context, string connectionString, string dialect, Dictionary<string, string>? options = null, IMockDataSeeder? seeder = null)
        {
            _context = context;
            _connectionString = connectionString;
            _dialect = dialect;
            _options = options;
            _logger = context.Logger;
            _seeder = seeder ?? new MockDataSeeder();

            // Schema discovery must not generate the million-row fixture.
            _initTask = new(() => _seeder.SeedDataAsync(_mockTables, new Random(42)),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }


        private async Task EnsureInitialized(CancellationToken cancellationToken = default)
        {
            cancellationToken = EffectiveCancellationToken(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // COMPAT_BREAK: 0.22 — a completed failed seed must never expose partial rows.
                await _initTask.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                throw new ExecutionException("MOCKDB data initialization failed.");
            }
        }

        public Task<string> GetVersionAsync() => Task.FromResult("Mock SQL Server 2022 v16.0");
        public HashSet<string> GetSupportedFunctions() => new();

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ReadBatches(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            await EnsureInitialized(effectiveCancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(_activeTable) && _mockTables.TryGetValue(_activeTable, out var table))
            {
                yield return table;
            }
            else if (_mockTables.Count > 0)
            {
                yield return _mockTables.Values.First();
            }
            else
            {
                var dt = new DataTable();
                dt.SetColumns(new[] { "ID", "Name" });
                yield return dt;
            }
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();

            // Only a table the script created keeps written rows. The seeded fixture stays fixed, so
            // every test that reads Users or Sales still sees the same rows.
            if (string.IsNullOrEmpty(_activeTable) || !_createdTables.TryGetValue(_activeTable, out var target))
                return;

            if (!append) target.Rows.Clear();
            await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
            {
                foreach (var row in batch.Rows)
                {
                    var copy = new Row();
                    foreach (var column in target.ColumnNames) copy[column] = row[column];
                    await target.AddRowAsync(copy);
                }
            }
        }

        // Tables created through CREATE TABLE. MOCKDB passed that statement to ExecuteRawSql, which
        // ignored it, so a later read of the new table silently returned the first seeded table.
        private readonly Dictionary<string, DataTable> _createdTables = new(StringComparer.OrdinalIgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex CreateTablePattern = new(
            @"^CREATE\s+TABLE\s+(?<name>[^\s(]+)\s*\((?<columns>.*)\)\s*;?\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

        private static readonly System.Text.RegularExpressions.Regex DropTablePattern = new(
            @"^DROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?<name>[^\s;]+)\s*;?\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static string BareTableName(string name) => name.Split('.').Last();

        /// <summary>Column names of a CREATE TABLE body; commas inside a type such as DECIMAL(10,2) are not separators.</summary>
        private static List<string> DeclaredColumnNames(string body)
        {
            var names = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i <= body.Length; i++)
            {
                if (i < body.Length && body[i] == '(') depth++;
                else if (i < body.Length && body[i] == ')') depth--;
                else if (i == body.Length || (body[i] == ',' && depth == 0))
                {
                    var definition = body[start..i].Trim();
                    start = i + 1;
                    if (definition.Length == 0) continue;
                    var first = definition.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
                    // A table constraint (PRIMARY KEY (...), CONSTRAINT x ...) names no column.
                    if (first.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
                        || first.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase)
                        || first.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase)
                        || first.Equals("FOREIGN", StringComparison.OrdinalIgnoreCase)
                        || first.Equals("CHECK", StringComparison.OrdinalIgnoreCase))
                        continue;
                    names.Add(first.Trim('"', '`'));
                }
            }
            return names;
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (!string.IsNullOrWhiteSpace(_activeTable))
            {
                var declaredColumns = await GetColumnsAsync(_activeTable);
                if (declaredColumns.Any()) return declaredColumns;
            }

            await EnsureInitialized();
            await using var enumerator = ReadBatches(1).GetAsyncEnumerator();
            if (await enumerator.MoveNextAsync())
            {
                return enumerator.Current.ColumnNames;
            }
            return new List<string> { "ID", "Name" };
        }
        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ExecuteRawSql(sql, parameters, CancellationToken.None);

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(
            string sql,
            IEnumerable<object?>? parameters,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            await EnsureInitialized(effectiveCancellationToken);
            effectiveCancellationToken.ThrowIfCancellationRequested();
            var trimmedSql = sql.Trim();
            var normSql = trimmedSql.Replace("[", "").Replace("]", "").Replace("\r", " ").Replace("\n", " ");

            var create = CreateTablePattern.Match(normSql);
            if (create.Success)
            {
                var name = BareTableName(create.Groups["name"].Value);
                var created = new DataTable();
                created.SetColumns(DeclaredColumnNames(create.Groups["columns"].Value));
                _createdTables[name] = created;
                _mockTables[name] = created;
                yield break;
            }

            var drop = DropTablePattern.Match(normSql);
            if (drop.Success)
            {
                var name = BareTableName(drop.Groups["name"].Value);
                if (_createdTables.Remove(name)) _mockTables.Remove(name);
                yield break;
            }

            DataTable? source = null;
            foreach (var tableName in _mockTables.Keys)
            {
                if (normSql.Contains(tableName, StringComparison.OrdinalIgnoreCase))
                {
                    source = _mockTables[tableName];
                    break;
                }
            }

            if (source == null && parameters != null && parameters.Any())
            {
                string processedSql = ETL_SQL.Core.Common.ParameterUtility.ProcessParameters(sql);
                var dt = new DataTable();
                dt.SetColumns(new[] { "ParameterValue", "ProcessedSql" });
                foreach (var p in parameters)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    await dt.AddRowAsync(new Row { ["ParameterValue"] = p, ["ProcessedSql"] = processedSql });
                }
                yield return dt;
                yield break;
            }

            if (source != null)
            {
                if (normSql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !normSql.Contains("*"))
                {
                    var fromIdx = normSql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
                    if (fromIdx > 7)
                    {
                        var colsPart = normSql.Substring(7, fromIdx - 7);
                        var columnNames = colsPart.Split(',')
                           .Select(c =>
                           {
                               var trimmed = c.Trim();
                               var lastWord = trimmed.Split(' ').Last();
                               return lastWord.Split('.').Last();
                           })
                           .ToList();

                        var filtered = new DataTable();
                        filtered.SetColumns(columnNames);
                        var whereMatch = System.Text.RegularExpressions.Regex.Match(normSql, @"WHERE\s+(?<col>[\w\.]+)\s*=\s*(?<val>[^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        foreach (var row in source.Rows)
                        {
                            effectiveCancellationToken.ThrowIfCancellationRequested();
                            bool match = true;
                            if (whereMatch.Success)
                            {
                                var colName = whereMatch.Groups["col"].Value.Split('.').Last();
                                var valStr = whereMatch.Groups["val"].Value.Trim().Trim('\'', '"');

                                // Simplified Mock parameter replacement
                                if (valStr.StartsWith("?") || valStr.StartsWith("@"))
                                {
                                    // Handle ?1 or @pn from parameters
                                    int pIdx = -1;
                                    if (valStr.StartsWith("?"))
                                    {
                                        if (int.TryParse(valStr.Substring(1), out var pNum)) pIdx = pNum - 1;
                                    }
                                    else if (valStr.StartsWith("@p", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (int.TryParse(valStr.Substring(2), out var pNum)) pIdx = pNum;
                                    }

                                    var pList = parameters?.ToList();
                                    if (pList != null && pIdx >= 0 && pIdx < pList.Count)
                                    {
                                        valStr = pList[pIdx]?.ToString() ?? "";
                                    }
                                }

                                if (row[colName]?.ToString() != valStr) match = false;
                            }

                            if (match)
                            {
                                var newRow = new Row();
                                foreach (var colName in columnNames)
                                {
                                    newRow[colName] = row[colName];
                                }
                                await filtered.AddRowAsync(newRow);
                                if (whereMatch.Success) break;
                            }
                        }
                        yield return filtered;
                        yield break;
                    }
                }
                yield return source;
            }
            else
            {
                var dt = new DataTable();
                dt.SetColumns(new[] { "ID", "Name" });
                yield return dt;
            }
        }
        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            var declared = _seeder.GetDeclaredSchema();
            if (declared.Count > 0)
            {
                return declared.Keys.Where(k => !k.Contains(".")).AsEnumerable();
            }

            await EnsureInitialized();
            return _mockTables.Keys.Where(k => !k.Contains(".")).AsEnumerable();
        }
        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            var declared = _seeder.GetDeclaredSchema();
            if (declared.TryGetValue(tableName, out var columns))
            {
                return columns.Select(c => c.ColumnName);
            }

            await EnsureInitialized();
            if (_mockTables.TryGetValue(tableName, out var dt))
            {
                return dt.ColumnNames;
            }
            return Enumerable.Empty<string>();
        }
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public async Task TruncateAsync()
        {
            await EnsureInitialized();
            if (!string.IsNullOrEmpty(_activeTable) && _mockTables.TryGetValue(_activeTable, out var table))
            {
                table.Rows.Clear();
            }
            await Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
        }

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : _context.CancellationToken;

        /// <summary>
        /// Serves the seeder's declared column types. Without this the metadata layer falls back to
        /// column names with a type of <c>ANY</c>, which is what the schema and session explorers
        /// displayed for every MOCKDB column — the default development loop.
        /// </summary>
        public ICatalogMetadataProvider? GetCatalogProvider()
        {
            var schema = _seeder.GetDeclaredSchema();
            return schema.Count == 0 ? null : new MockCatalogProvider(schema);
        }

        private sealed class MockCatalogProvider(IReadOnlyDictionary<string, IReadOnlyList<CatalogColumn>> schema)
            : ICatalogMetadataProvider
        {
            public Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(
                string schemaName, string tableName, CancellationToken ct = default)
            {
                // Callers split a qualified name before calling, but the seeder publishes some tables
                // under qualified keys ("hr.departments", "DemoDb.dbo.Employee"). Try the rejoined
                // name first, then the bare one, so both spellings resolve to real types.
                if (!string.IsNullOrEmpty(schemaName)
                    && schema.TryGetValue($"{schemaName}.{tableName}", out var qualified))
                {
                    return Task.FromResult(qualified);
                }

                return Task.FromResult(schema.TryGetValue(tableName, out var columns)
                    ? columns
                    : (IReadOnlyList<CatalogColumn>)[]);
            }

            // MockDb declares no foreign keys; an empty list is accurate, not a stub.
            public Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(
                string schemaName, string tableName, CancellationToken ct = default) =>
                Task.FromResult((IReadOnlyList<CatalogRelationship>)[]);
        }
    }
}
