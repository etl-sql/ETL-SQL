using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Shared;

namespace ETL_SQL.Connectors.Neo4j
{
    public class Neo4jDataSource : IDatabaseSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ETL_SQL.Common.ILogger _logger;
        private readonly IExecutionContext? _context;
        private IDriver? _driver;

        public Neo4jDataSource(IExecutionContext context, string connectionString, string? tableName = null, Dictionary<string, string>? options = null, IDriver? driver = null)
        {
            _context = context;
            _logger = context.Logger;
            _tableName = tableName;
            _options = options;
            _driver = driver;

            string connStr = connectionString;
            if (options != null && string.IsNullOrEmpty(connStr))
            {
                var decryptedOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
                if (decryptedOptions.TryGetValue("PASSWORD", out var pwd) && pwd.StartsWith("ENC:") && context != null)
                {
                    decryptedOptions["PASSWORD"] = context.DecryptValue(pwd) ?? "";
                }

                var conn = new Neo4jConnector();
                connStr = conn.BuildConnectionString(decryptedOptions);
            }
            _connectionString = connStr;
        }

        public string Path => _tableName ?? _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "NEO4J";
        public string ConnectionString => _connectionString;
        public string Dialect => "NEO4J";
        public bool SupportsSqlPushdown => true;

        private IDriver GetDriver()
        {
            if (_driver != null) return _driver;

            var decryptedOptions = new Dictionary<string, string>(_options ?? new(), StringComparer.OrdinalIgnoreCase);
            if (decryptedOptions.TryGetValue("PASSWORD", out var pwd) && pwd.StartsWith("ENC:") && _context != null)
            {
                decryptedOptions["PASSWORD"] = _context.DecryptValue(pwd) ?? "";
            }

            string connStr = _connectionString;
            if (string.IsNullOrEmpty(connStr))
            {
                var conn = new Neo4jConnector();
                connStr = conn.BuildConnectionString(decryptedOptions);
            }

            decryptedOptions.TryGetValue("USER", out var user);
            decryptedOptions.TryGetValue("PASSWORD", out var password);

            IAuthToken authToken = AuthTokens.None;
            if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(password))
            {
                authToken = AuthTokens.Basic(user ?? "", password ?? "");
            }

            int timeoutSecs = 30;
            if (decryptedOptions.TryGetValue("TIMEOUT_SECONDS", out var timeoutStr) && int.TryParse(timeoutStr, out var parsedSecs))
            {
                timeoutSecs = parsedSecs;
            }

            _driver = GraphDatabase.Driver(connStr, authToken, o => o.WithConnectionTimeout(TimeSpan.FromSeconds(timeoutSecs)));
            return _driver;
        }

        public async Task<string> GetVersionAsync()
        {
            var conn = new Neo4jConnector();
            return await conn.GetVersionAsync(_context!, _connectionString);
        }

        public HashSet<string> GetSupportedFunctions() => new();

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            var tables = new List<string>();
            try
            {
                var session = driver.AsyncSession(o => o.WithDatabase(database));
                await using (session)
                {
                    var labelResult = await session.RunAsync("CALL db.labels() YIELD label");
                    while (await labelResult.FetchAsync())
                    {
                        var label = labelResult.Current["label"]?.ToString();
                        if (!string.IsNullOrEmpty(label))
                        {
                            tables.Add($"NODE_{label.ToUpperInvariant()}");
                        }
                    }

                    var relResult = await session.RunAsync("CALL db.relationshipTypes() YIELD relationshipType");
                    while (await relResult.FetchAsync())
                    {
                        var relType = relResult.Current["relationshipType"]?.ToString();
                        if (!string.IsNullOrEmpty(relType))
                        {
                            tables.Add($"EDGE_{relType.ToUpperInvariant()}");
                        }
                    }
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
            return tables;
        }

        public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());

        public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsInternalAsync(_tableName ?? "");

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => GetColumnsInternalAsync(tableName);

        private async Task<IEnumerable<string>> GetColumnsInternalAsync(string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) return Enumerable.Empty<string>();

            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            var columns = new List<string>();

            if (tableName.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase))
            {
                columns.Add("_id");
                columns.Add("_labels");

                var label = tableName.Substring(5);
                var actualLabel = await ResolveActualLabelOrTypeAsync(label, isNode: true);

                try
                {
                    var session = driver.AsyncSession(o => o.WithDatabase(database));
                    await using (session)
                    {
                        var query = $"MATCH (n:`{actualLabel}`) RETURN keys(n) AS props LIMIT 1";
                        var result = await session.RunAsync(query);
                        if (await result.FetchAsync())
                        {
                            var props = result.Current["props"] as IEnumerable<object>;
                            if (props != null)
                            {
                                foreach (var p in props)
                                {
                                    var propName = p.ToString();
                                    if (propName != null && !columns.Contains(propName))
                                    {
                                        columns.Add(propName);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ShouldWrapProviderException(ex))
                {
                    throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
                }
            }
            else if (tableName.StartsWith("EDGE_", StringComparison.OrdinalIgnoreCase))
            {
                columns.Add("_id");
                columns.Add("_from_id");
                columns.Add("_to_id");
                columns.Add("_from_label");
                columns.Add("_to_label");

                var relType = tableName.Substring(5);
                var actualType = await ResolveActualLabelOrTypeAsync(relType, isNode: false);

                try
                {
                    var session = driver.AsyncSession(o => o.WithDatabase(database));
                    await using (session)
                    {
                        var query = $"MATCH ()-[r:`{actualType}`]->() RETURN keys(r) AS props LIMIT 1";
                        var result = await session.RunAsync(query);
                        if (await result.FetchAsync())
                        {
                            var props = result.Current["props"] as IEnumerable<object>;
                            if (props != null)
                            {
                                foreach (var p in props)
                                {
                                    var propName = p.ToString();
                                    if (propName != null && !columns.Contains(propName))
                                    {
                                        columns.Add(propName);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ShouldWrapProviderException(ex))
                {
                    throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
                }
            }

            return columns;
        }

        private async Task<string> ResolveActualLabelOrTypeAsync(string name, bool isNode)
        {
            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            try
            {
                var session = driver.AsyncSession(o => o.WithDatabase(database));
                await using (session)
                {
                    if (isNode)
                    {
                        var result = await session.RunAsync("CALL db.labels() YIELD label");
                        while (await result.FetchAsync())
                        {
                            var label = result.Current["label"]?.ToString();
                            if (string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
                            {
                                return label!;
                            }
                        }
                    }
                    else
                    {
                        var result = await session.RunAsync("CALL db.relationshipTypes() YIELD relationshipType");
                        while (await result.FetchAsync())
                        {
                            var relType = result.Current["relationshipType"]?.ToString();
                            if (string.Equals(relType, name, StringComparison.OrdinalIgnoreCase))
                            {
                                return relType!;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return name;
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "Neo4j", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Neo4j data source read.");

            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            var columnNames = (await GetColumnsInternalAsync(_tableName)).ToList();

            var currentBatch = new DataTable();
            currentBatch.SetColumns(columnNames);

            var session = driver.AsyncSession(o => o.WithDatabase(database));
            await using (session)
            {
                if (_tableName.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase))
                {
                    var label = _tableName.Substring(5);
                    var actualLabel = await ResolveActualLabelOrTypeAsync(label, isNode: true);
                    var query = $"MATCH (n:`{actualLabel}`) RETURN n";

                    var result = await session.RunAsync(query);
                    while (await result.FetchAsync())
                    {
                        var node = result.Current["n"] as INode;
                        if (node == null) continue;

                        var row = currentBatch.NewRow();
                        row["_id"] = node.ElementId;
                        row["_labels"] = string.Join(",", node.Labels);

                        foreach (var col in columnNames)
                        {
                            if (col == "_id" || col == "_labels") continue;
                            if (node.Properties.TryGetValue(col, out var val))
                            {
                                row[col] = val;
                            }
                            else
                            {
                                row[col] = null;
                            }
                        }

                        await currentBatch.AddRowAsync(row);

                        if (currentBatch.Rows.Count >= batchSize)
                        {
                            yield return currentBatch;
                            currentBatch = new DataTable();
                            currentBatch.SetColumns(columnNames);
                        }
                    }
                }
                else if (_tableName.StartsWith("EDGE_", StringComparison.OrdinalIgnoreCase))
                {
                    var relType = _tableName.Substring(5);
                    var actualType = await ResolveActualLabelOrTypeAsync(relType, isNode: false);
                    var query = $"MATCH (from)-[r:`{actualType}`]->(to) RETURN r, elementId(from) AS _from_id, labels(from) AS _from_labels, elementId(to) AS _to_id, labels(to) AS _to_labels";

                    var result = await session.RunAsync(query);
                    while (await result.FetchAsync())
                    {
                        var rel = result.Current["r"] as IRelationship;
                        if (rel == null) continue;

                        var fromId = result.Current["_from_id"]?.ToString();
                        var toId = result.Current["_to_id"]?.ToString();
                        var fromLabels = result.Current["_from_labels"] as IEnumerable<object>;
                        var toLabels = result.Current["_to_labels"] as IEnumerable<object>;

                        var row = currentBatch.NewRow();
                        row["_id"] = rel.ElementId;
                        row["_from_id"] = fromId;
                        row["_to_id"] = toId;
                        row["_from_label"] = fromLabels != null ? string.Join(",", fromLabels) : null;
                        row["_to_label"] = toLabels != null ? string.Join(",", toLabels) : null;

                        foreach (var col in columnNames)
                        {
                            if (col == "_id" || col == "_from_id" || col == "_to_id" || col == "_from_label" || col == "_to_label") continue;
                            if (rel.Properties.TryGetValue(col, out var val))
                            {
                                row[col] = val;
                            }
                            else
                            {
                                row[col] = null;
                            }
                        }

                        await currentBatch.AddRowAsync(row);

                        if (currentBatch.Rows.Count >= batchSize)
                        {
                            yield return currentBatch;
                            currentBatch = new DataTable();
                            currentBatch.SetColumns(columnNames);
                        }
                    }
                }
            }

            if (currentBatch.Rows.Count > 0)
            {
                yield return currentBatch;
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (_context != null && _context.IsWhatIf) return;

            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for Neo4j data source write.");

            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            try
            {
                var session = driver.AsyncSession(o => o.WithDatabase(database));
                await using (session)
                {
                    if (_tableName.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase))
                    {
                        var label = _tableName.Substring(5);
                        var actualLabel = await ResolveActualLabelOrTypeAsync(label, isNode: true);

                        if (!append)
                        {
                            await session.ExecuteWriteAsync(async tx =>
                            {
                                await tx.RunAsync($"MATCH (n:`{actualLabel}`) DETACH DELETE n");
                            });
                        }

                        await foreach (var batch in batches)
                        {
                            if (batch.Rows.Count == 0) continue;

                            var rowsList = new List<Dictionary<string, object?>>();
                            foreach (var row in batch.Rows)
                            {
                                var propDict = new Dictionary<string, object?>();
                                foreach (var col in batch.ColumnNames)
                                {
                                    if (col == "_id" || col == "_labels") continue;
                                    propDict[col] = row[col];
                                }
                                rowsList.Add(propDict);
                            }

                            await session.ExecuteWriteAsync(async tx =>
                            {
                                await tx.RunAsync($"UNWIND $rows AS row CREATE (n:`{actualLabel}`) SET n += row", new { rows = rowsList });
                            });
                        }
                    }
                    else if (_tableName.StartsWith("EDGE_", StringComparison.OrdinalIgnoreCase))
                    {
                        var relType = _tableName.Substring(5);
                        var actualType = await ResolveActualLabelOrTypeAsync(relType, isNode: false);

                        if (!append)
                        {
                            await session.ExecuteWriteAsync(async tx =>
                            {
                                await tx.RunAsync($"MATCH ()-[r:`{actualType}`]->() DELETE r");
                            });
                        }

                        await foreach (var batch in batches)
                        {
                            if (batch.Rows.Count == 0) continue;

                            var rowsList = new List<object>();
                            foreach (var row in batch.Rows)
                            {
                                var fromId = row["_from_id"]?.ToString();
                                var toId = row["_to_id"]?.ToString();
                                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) continue;

                                var propDict = new Dictionary<string, object?>();
                                foreach (var col in batch.ColumnNames)
                                {
                                    if (col == "_id" || col == "_from_id" || col == "_to_id" || col == "_from_label" || col == "_to_label") continue;
                                    propDict[col] = row[col];
                                }

                                rowsList.Add(new
                                {
                                    fromId = fromId,
                                    toId = toId,
                                    properties = propDict
                                });
                            }

                            if (rowsList.Count == 0) continue;

                            await session.ExecuteWriteAsync(async tx =>
                            {
                                await tx.RunAsync($@"
                                    UNWIND $rows AS row
                                    MATCH (from) WHERE elementId(from) = row.fromId
                                    MATCH (to) WHERE elementId(to) = row.toId
                                    CREATE (from)-[r:`{actualType}`]->(to)
                                    SET r += row.properties", new { rows = rowsList });
                            });
                        }
                    }
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
        }

        public async Task TruncateAsync()
        {
            if (_context != null && _context.IsWhatIf) return;

            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            try
            {
                var session = driver.AsyncSession(o => o.WithDatabase(database));
                await using (session)
                {
                    await session.ExecuteWriteAsync(async tx =>
                    {
                        await tx.RunAsync("MATCH (n) DETACH DELETE n");
                    });
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
        }

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            var paramDict = new Dictionary<string, object?>();
            string cypherQuery = sql;

            int paramIndex = 1;
            while (cypherQuery.Contains("?"))
            {
                int qIdx = cypherQuery.IndexOf('?');
                if (qIdx + 1 < cypherQuery.Length && char.IsDigit(cypherQuery[qIdx + 1]))
                {
                    break;
                }
                var paramName = $"p{paramIndex}";
                cypherQuery = cypherQuery.Substring(0, qIdx) + $"${paramName}" + cypherQuery.Substring(qIdx + 1);

                if (parameters != null)
                {
                    var list = parameters.ToList();
                    if (paramIndex - 1 < list.Count)
                    {
                        paramDict[paramName] = list[paramIndex - 1];
                    }
                    else
                    {
                        paramDict[paramName] = null;
                    }
                }
                paramIndex++;
            }

            if (parameters != null)
            {
                var list = parameters.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    var paramName = $"p{i + 1}";
                    cypherQuery = cypherQuery.Replace($"?{i + 1}", $"${paramName}");
                    paramDict[paramName] = list[i];
                }
            }

            var session = driver.AsyncSession(o => o.WithDatabase(database));
            await using (session)
            {
                IResultCursor resultCursor;
                try
                {
                    resultCursor = await session.RunAsync(cypherQuery, paramDict);
                }
                catch (Exception ex) when (ShouldWrapProviderException(ex))
                {
                    throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
                }

                var keys = (await resultCursor.KeysAsync()).ToList();
                var currentBatch = new DataTable();
                currentBatch.SetColumns(keys);

                while (await resultCursor.FetchAsync())
                {
                    var row = currentBatch.NewRow();
                    foreach (var key in keys)
                    {
                        row[key] = ConvertNeo4jValue(resultCursor.Current[key]);
                    }
                    await currentBatch.AddRowAsync(row);

                    if (currentBatch.Rows.Count >= 10000)
                    {
                        yield return currentBatch;
                        currentBatch = new DataTable();
                        currentBatch.SetColumns(keys);
                    }
                }

                if (currentBatch.Rows.Count > 0)
                {
                    yield return currentBatch;
                }
            }
        }

        private object? ConvertNeo4jValue(object? val)
        {
            if (val == null) return null;
            if (val is INode node)
            {
                var dict = new Dictionary<string, object?>(node.Properties);
                dict["_id"] = node.ElementId;
                dict["_labels"] = node.Labels;
                return System.Text.Json.JsonSerializer.Serialize(dict);
            }
            if (val is IRelationship rel)
            {
                var dict = new Dictionary<string, object?>(rel.Properties);
                dict["_id"] = rel.ElementId;
                dict["_from_id"] = rel.StartNodeElementId;
                dict["_to_id"] = rel.EndNodeElementId;
                dict["_type"] = rel.Type;
                return System.Text.Json.JsonSerializer.Serialize(dict);
            }
            if (val is IDictionary<string, object> dictVal)
            {
                return System.Text.Json.JsonSerializer.Serialize(dictVal);
            }
            if (val is IEnumerable<object> listVal)
            {
                return System.Text.Json.JsonSerializer.Serialize(listVal);
            }
            return val;
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public IDataSource WithTable(string tableName)
        {
            return new Neo4jDataSource(_context!, _connectionString, tableName, _options, _driver);
        }

        public async ValueTask DisposeAsync()
        {
            if (_driver != null)
            {
                await _driver.DisposeAsync();
                _driver = null;
            }
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is Neo4jException or InvalidOperationException;
    }
}
