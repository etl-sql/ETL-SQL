using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Neo4j.Driver;

namespace ETL_SQL.Connectors.Neo4j
{
    public class Neo4jDataSource : IDatabaseSource, ITransactionalDataSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ETL_SQL.Common.ILogger _logger;
        private readonly IExecutionContext? _context;
        private readonly string? _uriUser;
        private readonly string? _uriPassword;
        private readonly bool _ownsDriver;
        private readonly bool _ownsTransaction;
        private IDriver? _driver;
        private IAsyncSession? _transactionSession;
        private IAsyncTransaction? _activeTransaction;

        public Neo4jDataSource(
            IExecutionContext context,
            string connectionString,
            string? tableName = null,
            Dictionary<string, string>? options = null,
            IDriver? driver = null,
            bool ownsDriver = false,
            IAsyncSession? transactionSession = null,
            IAsyncTransaction? activeTransaction = null,
            bool ownsTransaction = true)
        {
            _context = context;
            _logger = context.Logger;
            _tableName = tableName;
            _options = options;
            _driver = driver;
            _ownsDriver = driver == null || ownsDriver;
            _transactionSession = transactionSession;
            _activeTransaction = activeTransaction;
            _ownsTransaction = ownsTransaction;

            string connStr = connectionString;
            if (options != null && string.IsNullOrEmpty(connStr))
            {
                var conn = new Neo4jConnector();
                connStr = conn.BuildConnectionString(options);
            }
            _connectionString = StripUserInfo(connStr, out _uriUser, out _uriPassword);
        }

        public string Path => _tableName ?? _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "NEO4J";
        public string ConnectionString => _connectionString;
        public string Dialect => "NEO4J";
        public bool SupportsSqlPushdown => false;

        private IDriver GetDriver()
        {
            if (_driver != null) return _driver;

            var connectorOptions = new Dictionary<string, string>(_options ?? new(), StringComparer.OrdinalIgnoreCase);

            string connStr = _connectionString;
            if (string.IsNullOrEmpty(connStr))
            {
                var conn = new Neo4jConnector();
                connStr = conn.BuildConnectionString(connectorOptions);
                connStr = StripUserInfo(connStr, out _, out _);
            }

            connectorOptions.TryGetValue("USER", out var user);
            connectorOptions.TryGetValue("PASSWORD", out var password);
            user ??= _uriUser;
            password ??= _uriPassword;

            if (password != null && password.StartsWith("ENC:") && _context != null)
            {
                password = _context.DecryptValue(password);
            }

            IAuthToken authToken = AuthTokens.None;
            if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(password))
            {
                authToken = AuthTokens.Basic(user ?? "", password ?? "");
            }

            int timeoutSecs = 30;
            if (connectorOptions.TryGetValue("TIMEOUT_SECONDS", out var timeoutStr) && int.TryParse(timeoutStr, out var parsedSecs) && parsedSecs > 0)
            {
                timeoutSecs = parsedSecs;
            }

            _driver = GraphDatabase.Driver(connStr, authToken, o => o.WithConnectionTimeout(TimeSpan.FromSeconds(timeoutSecs)));
            return _driver;
        }

        public async Task<string> GetVersionAsync()
        {
            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";

            try
            {
                var session = driver.AsyncSession(o => o.WithDatabase(database));
                await using (session)
                {
                    var result = await session.RunAsync("CALL dbms.components() YIELD name, versions, edition RETURN versions[0] AS version, edition");
                    if (await result.FetchAsync())
                    {
                        var version = result.Current["version"]?.ToString() ?? "Unknown";
                        var edition = result.Current["edition"]?.ToString() ?? "Unknown";
                        return $"Neo4j Connector v1.0 (Connected - Server Version: {version} {edition})";
                    }
                }
                return "Neo4j Connector v1.0 (Connected)";
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
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
            var schemaSampleSize = GetNonNegativeIntOption("SCHEMA_SAMPLE_SIZE", 1000);

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
                        var labelIdentifier = QuoteCypherIdentifier(actualLabel);
                        var query = schemaSampleSize == 0
                            ? $"MATCH (n:{labelIdentifier}) UNWIND keys(n) AS prop RETURN DISTINCT prop ORDER BY prop"
                            : $"MATCH (n:{labelIdentifier}) WITH n LIMIT $sampleSize UNWIND keys(n) AS prop RETURN DISTINCT prop ORDER BY prop";
                        var result = schemaSampleSize == 0
                            ? await session.RunAsync(query)
                            : await session.RunAsync(query, new { sampleSize = schemaSampleSize });
                        while (await result.FetchAsync())
                        {
                            var propName = result.Current["prop"]?.ToString();
                            if (propName != null && !columns.Contains(propName))
                            {
                                columns.Add(propName);
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
                        var typeIdentifier = QuoteCypherIdentifier(actualType);
                        var query = schemaSampleSize == 0
                            ? $"MATCH ()-[r:{typeIdentifier}]->() UNWIND keys(r) AS prop RETURN DISTINCT prop ORDER BY prop"
                            : $"MATCH ()-[r:{typeIdentifier}]->() WITH r LIMIT $sampleSize UNWIND keys(r) AS prop RETURN DISTINCT prop ORDER BY prop";
                        var result = schemaSampleSize == 0
                            ? await session.RunAsync(query)
                            : await session.RunAsync(query, new { sampleSize = schemaSampleSize });
                        while (await result.FetchAsync())
                        {
                            var propName = result.Current["prop"]?.ToString();
                            if (propName != null && !columns.Contains(propName))
                            {
                                columns.Add(propName);
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
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
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
                    var labelIdentifier = QuoteCypherIdentifier(actualLabel);
                    var query = $"MATCH (n:{labelIdentifier}) RETURN n";

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
                    var typeIdentifier = QuoteCypherIdentifier(actualType);
                    var query = $"MATCH (from)-[r:{typeIdentifier}]->(to) RETURN r, elementId(from) AS _from_id, labels(from) AS _from_labels, elementId(to) AS _to_id, labels(to) AS _to_labels";

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
                if (_tableName.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase))
                {
                    var label = _tableName.Substring(5);
                    var actualLabel = await ResolveActualLabelOrTypeAsync(label, isNode: true);
                    var labelIdentifier = QuoteCypherIdentifier(actualLabel);
                    var keyColumns = GetOptionList("KEY_COLUMNS");
                    var mergeKey = keyColumns.Count > 0
                        ? "{ " + string.Join(", ", keyColumns.Select(c => $"{QuoteCypherIdentifier(c)}: row.{QuoteCypherIdentifier(c)}")) + " }"
                        : "";
                    var writeCypher = keyColumns.Count > 0
                        ? $"UNWIND $rows AS row MERGE (n:{labelIdentifier} {mergeKey}) SET n += row"
                        : $"UNWIND $rows AS row CREATE (n:{labelIdentifier}) SET n += row";

                    async Task WriteNodesAsync(IAsyncQueryRunner tx)
                    {
                        if (!append)
                        {
                            await tx.RunAsync($"MATCH (n:{labelIdentifier}) DETACH DELETE n");
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
                                    propDict[col] = NormalizePropertyValue(row[col], col);
                                }
                                foreach (var keyColumn in keyColumns)
                                {
                                    if (!propDict.ContainsKey(keyColumn) || propDict[keyColumn] == null)
                                    {
                                        throw new ExecutionException($"Neo4j NODE write requires non-null KEY_COLUMNS value '{keyColumn}'.");
                                    }
                                }
                                rowsList.Add(propDict);
                            }

                            await tx.RunAsync(writeCypher, new { rows = rowsList });
                        }
                    }

                    if (_activeTransaction != null)
                    {
                        await WriteNodesAsync(_activeTransaction);
                    }
                    else
                    {
                        var session = driver.AsyncSession(o => o.WithDatabase(database));
                        await using (session)
                        {
                            await session.ExecuteWriteAsync(WriteNodesAsync);
                        }
                    }
                }
                else if (_tableName.StartsWith("EDGE_", StringComparison.OrdinalIgnoreCase))
                {
                    var relType = _tableName.Substring(5);
                    var actualType = await ResolveActualLabelOrTypeAsync(relType, isNode: false);
                    var typeIdentifier = QuoteCypherIdentifier(actualType);
                    var edgeKeyColumns = GetOptionList("KEY_COLUMNS");
                    var fromLabel = GetOption("FROM_LABEL");
                    var toLabel = GetOption("TO_LABEL");
                    var fromKeyColumn = GetOption("FROM_KEY_COLUMN") ?? "id";
                    var toKeyColumn = GetOption("TO_KEY_COLUMN") ?? "id";
                    var canResolveByKey = !string.IsNullOrEmpty(fromLabel) && !string.IsNullOrEmpty(toLabel);
                    var skipMissingEndpoints = GetBooleanOption("SKIP_MISSING_ENDPOINTS", defaultValue: false);
                    var edgeMergeCypher = edgeKeyColumns.Count > 0
                        ? "MERGE (from)-[r:" + typeIdentifier + " { " + string.Join(", ", edgeKeyColumns.Select(c => $"{QuoteCypherIdentifier(c)}: row.properties.{QuoteCypherIdentifier(c)}")) + " }]->(to)"
                        : $"CREATE (from)-[r:{typeIdentifier}]->(to)";

                    async Task WriteEdgesAsync(IAsyncQueryRunner tx)
                    {
                        if (!append)
                        {
                            await tx.RunAsync($"MATCH ()-[r:{typeIdentifier}]->() DELETE r");
                        }

                        await foreach (var batch in batches)
                        {
                            if (batch.Rows.Count == 0) continue;

                            var rowsList = new List<object>();
                            foreach (var row in batch.Rows)
                            {
                                var fromId = row["_from_id"]?.ToString();
                                var toId = row["_to_id"]?.ToString();
                                var fromKey = row["_from_key"]?.ToString();
                                var toKey = row["_to_key"]?.ToString();
                                if (!canResolveByKey && (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)))
                                {
                                    if (skipMissingEndpoints) continue;
                                    throw new ExecutionException("Neo4j EDGE write requires non-null _from_id and _to_id values when FROM_LABEL and TO_LABEL are not set.");
                                }
                                if (canResolveByKey && (string.IsNullOrEmpty(fromKey) || string.IsNullOrEmpty(toKey)))
                                {
                                    if (skipMissingEndpoints) continue;
                                    throw new ExecutionException("Neo4j EDGE write requires non-null _from_key and _to_key values when FROM_LABEL and TO_LABEL are set.");
                                }

                                var propDict = new Dictionary<string, object?>();
                                foreach (var col in batch.ColumnNames)
                                {
                                    if (col == "_id" || col == "_from_id" || col == "_to_id" || col == "_from_key" || col == "_to_key" || col == "_from_label" || col == "_to_label") continue;
                                    propDict[col] = NormalizePropertyValue(row[col], col);
                                }
                                foreach (var keyColumn in edgeKeyColumns)
                                {
                                    if (!propDict.ContainsKey(keyColumn) || propDict[keyColumn] == null)
                                    {
                                        throw new ExecutionException($"Neo4j EDGE write requires non-null KEY_COLUMNS value '{keyColumn}'.");
                                    }
                                }

                                rowsList.Add(new
                                {
                                    fromId = fromId,
                                    toId = toId,
                                    fromKey = fromKey,
                                    toKey = toKey,
                                    properties = propDict
                                });
                            }

                            if (rowsList.Count == 0) continue;

                            if (canResolveByKey)
                            {
                                var cursor = await tx.RunAsync($@"
                                UNWIND $rows AS row
                                MATCH (from:{QuoteCypherIdentifier(fromLabel!)}) WHERE from.{QuoteCypherIdentifier(fromKeyColumn)} = row.fromKey
                                MATCH (to:{QuoteCypherIdentifier(toLabel!)}) WHERE to.{QuoteCypherIdentifier(toKeyColumn)} = row.toKey
                                {edgeMergeCypher}
                                SET r += row.properties
                                RETURN count(r) AS written", new { rows = rowsList });
                                await ValidateEdgeWriteCountAsync(cursor, rowsList.Count, skipMissingEndpoints);
                            }
                            else
                            {
                                var cursor = await tx.RunAsync($@"
                                UNWIND $rows AS row
                                MATCH (from) WHERE elementId(from) = row.fromId
                                MATCH (to) WHERE elementId(to) = row.toId
                                {edgeMergeCypher}
                                SET r += row.properties
                                RETURN count(r) AS written", new { rows = rowsList });
                                await ValidateEdgeWriteCountAsync(cursor, rowsList.Count, skipMissingEndpoints);
                            }
                        }
                    }

                    if (_activeTransaction != null)
                    {
                        await WriteEdgesAsync(_activeTransaction);
                    }
                    else
                    {
                        var session = driver.AsyncSession(o => o.WithDatabase(database));
                        await using (session)
                        {
                            await session.ExecuteWriteAsync(WriteEdgesAsync);
                        }
                    }
                }
                else
                {
                    throw new ExecutionException("Neo4j writes require a virtual table named NODE_<LABEL> or EDGE_<TYPE>.");
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
                if (!string.IsNullOrEmpty(_tableName) && _tableName.StartsWith("NODE_", StringComparison.OrdinalIgnoreCase))
                {
                    var label = _tableName.Substring(5);
                    var actualLabel = await ResolveActualLabelOrTypeAsync(label, isNode: true);
                    var labelIdentifier = QuoteCypherIdentifier(actualLabel);
                    await RunWriteAsync(driver, database, async tx =>
                    {
                        await tx.RunAsync($"MATCH (n:{labelIdentifier}) DETACH DELETE n");
                    });
                    return;
                }

                if (!string.IsNullOrEmpty(_tableName) && _tableName.StartsWith("EDGE_", StringComparison.OrdinalIgnoreCase))
                {
                    var relType = _tableName.Substring(5);
                    var actualType = await ResolveActualLabelOrTypeAsync(relType, isNode: false);
                    var typeIdentifier = QuoteCypherIdentifier(actualType);
                    await RunWriteAsync(driver, database, async tx =>
                    {
                        await tx.RunAsync($"MATCH ()-[r:{typeIdentifier}]->() DELETE r");
                    });
                    return;
                }

                if (!string.IsNullOrEmpty(_tableName))
                {
                    throw new ExecutionException("Neo4j truncate requires a virtual table named NODE_<LABEL> or EDGE_<TYPE>.");
                }

                await RunWriteAsync(driver, database, async tx =>
                {
                    await tx.RunAsync("MATCH (n) DETACH DELETE n");
                });
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
        }

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ConnectorExceptionWrapper.WrapAsync(ExecuteRawSqlCore(sql, parameters), "Neo4j", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(string sql, IEnumerable<object?>? parameters = null)
        {
            if (_context != null && _context.IsWhatIf && IsMutatingCypher(sql))
            {
                yield break;
            }

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

            if (_activeTransaction != null)
            {
                var resultCursor = await _activeTransaction.RunAsync(cypherQuery, paramDict);
                await foreach (var batch in ReadResultCursorAsync(resultCursor))
                {
                    yield return batch;
                }
                yield break;
            }

            var session = driver.AsyncSession(o => o.WithDatabase(database));
            await using (session)
            {
                var resultCursor = await session.RunAsync(cypherQuery, paramDict);
                await foreach (var batch in ReadResultCursorAsync(resultCursor))
                {
                    yield return batch;
                }
            }
        }

        private async IAsyncEnumerable<DataTable> ReadResultCursorAsync(IResultCursor resultCursor)
        {
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

        private static async Task ValidateEdgeWriteCountAsync(IResultCursor cursor, int expectedRows, bool skipMissingEndpoints)
        {
            if (!await cursor.FetchAsync()) return;

            var written = Convert.ToInt64(cursor.Current["written"]);
            if (!skipMissingEndpoints && written != expectedRows)
            {
                throw new ExecutionException($"Neo4j EDGE write matched {written} relationship endpoint pair(s) for {expectedRows} input row(s). Verify endpoint IDs or endpoint key labels/columns.");
            }
        }

        private static object? NormalizePropertyValue(object? value, string columnName)
        {
            if (value == null || value == DBNull.Value) return null;
            if (value is string or bool or int or long or float or double) return value;
            if (value is short or byte or sbyte or uint or ushort) return Convert.ToInt64(value);
            if (value is ulong ulongValue)
            {
                if (ulongValue > long.MaxValue)
                {
                    throw new ExecutionException($"Neo4j property '{columnName}' contains an unsigned integer larger than Neo4j signed integer storage supports.");
                }
                return Convert.ToInt64(ulongValue);
            }
            if (value is decimal decimalValue) return Convert.ToDouble(decimalValue);
            if (value is char charValue) return charValue.ToString();
            if (value is Guid guidValue) return guidValue.ToString();
            if (value is DateTime dateTimeValue) return dateTimeValue.ToString("O");
            if (value is DateTimeOffset dateTimeOffsetValue) return dateTimeOffsetValue.ToString("O");
            if (value is TimeSpan timeSpanValue) return timeSpanValue.ToString("c");
            if (value is Row rowValue) return SerializePropertyValue(rowValue.Columns, columnName);
            if (value is JsonElement jsonValue) return jsonValue.GetRawText();
            if (value is IDictionary dictionaryValue) return SerializePropertyValue(DictionaryToObject(dictionaryValue), columnName);
            if (value is IEnumerable enumerableValue && value is not byte[])
            {
                var list = new List<object?>();
                foreach (var item in enumerableValue)
                {
                    list.Add(NormalizePropertyValue(item, columnName));
                }
                return list;
            }
            if (value.GetType().IsEnum) return value.ToString();

            return SerializePropertyValue(value, columnName);
        }

        private static Dictionary<string, object?> DictionaryToObject(IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString() ?? "";
                result[key] = NormalizePropertyValue(entry.Value, key);
            }
            return result;
        }

        private static string SerializePropertyValue(object value, string columnName)
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException)
            {
                throw new ExecutionException($"Neo4j property '{columnName}' contains a value type that cannot be serialized for graph storage.");
            }
        }

        private static string StripUserInfo(string connectionString, out string? user, out string? password)
        {
            user = null;
            password = null;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            try
            {
                var uri = new Uri(connectionString);
                if (string.IsNullOrEmpty(uri.UserInfo))
                {
                    return connectionString;
                }

                var parts = uri.UserInfo.Split(new[] { ':' }, 2);
                user = Uri.UnescapeDataString(parts[0]);
                if (parts.Length > 1)
                {
                    password = Uri.UnescapeDataString(parts[1]);
                }

                var builder = new UriBuilder(uri)
                {
                    UserName = "",
                    Password = ""
                };
                return builder.Uri.ToString();
            }
            catch
            {
                return connectionString;
            }
        }

        private static string QuoteCypherIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                throw new ExecutionException("Neo4j label or relationship type cannot be empty.");
            }

            return "`" + identifier.Replace("`", "``") + "`";
        }

        private static bool IsMutatingCypher(string cypher)
        {
            if (string.IsNullOrWhiteSpace(cypher)) return false;

            var scrubbed = Regex.Replace(cypher, @"(?s)/\*.*?\*/|//.*?$|'(?:\\.|''|[^'])*'|""(?:\\.|""""|[^""])*""", " ", RegexOptions.Multiline);
            if (Regex.IsMatch(scrubbed, @"^\s*CALL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && !Regex.IsMatch(scrubbed, @"^\s*CALL\s+(db\.labels|db\.relationshipTypes|dbms\.components)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }

            return Regex.IsMatch(
                scrubbed,
                @"\b(CREATE|MERGE|DELETE|DETACH|SET|REMOVE|DROP|LOAD|FOREACH)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private string? GetOption(string key)
        {
            return _options != null && _options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }

        private List<string> GetOptionList(string key)
        {
            var value = GetOption(key);
            if (value == null) return new List<string>();

            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        private int GetNonNegativeIntOption(string key, int defaultValue)
        {
            var value = GetOption(key);
            return int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : defaultValue;
        }

        private bool GetBooleanOption(string key, bool defaultValue)
        {
            var value = GetOption(key);
            if (value == null) return defaultValue;
            if (value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return false;
            throw new ExecutionException($"Neo4j option {key} must be TRUE or FALSE.");
        }

        private async Task RunWriteAsync(IDriver driver, string database, Func<IAsyncQueryRunner, Task> action)
        {
            if (_activeTransaction != null)
            {
                await action(_activeTransaction);
                return;
            }

            var session = driver.AsyncSession(o => o.WithDatabase(database));
            await using (session)
            {
                await session.ExecuteWriteAsync(action);
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public async Task BeginTransactionAsync()
        {
            if (_activeTransaction != null) return;

            var driver = GetDriver();
            var database = _options?.GetValueOrDefault("DATABASE", "neo4j") ?? "neo4j";
            var session = driver.AsyncSession(o => o.WithDatabase(database));
            try
            {
                _transactionSession = session;
                _activeTransaction = await session.BeginTransactionAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                await session.DisposeAsync();
                _transactionSession = null;
                _activeTransaction = null;
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
        }

        public async Task CommitAsync()
        {
            if (_activeTransaction == null) return;

            try
            {
                await _activeTransaction.CommitAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
            finally
            {
                await DisposeTransactionAsync();
            }
        }

        public async Task RollbackAsync()
        {
            if (_activeTransaction == null) return;

            try
            {
                await _activeTransaction.RollbackAsync();
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
            finally
            {
                await DisposeTransactionAsync();
            }
        }

        private async Task DisposeTransactionAsync()
        {
            if (_activeTransaction != null)
            {
                await _activeTransaction.DisposeAsync();
                _activeTransaction = null;
            }

            if (_transactionSession != null)
            {
                await _transactionSession.DisposeAsync();
                _transactionSession = null;
            }
        }

        public IDataSource WithTable(string tableName)
        {
            return new Neo4jDataSource(
                _context!,
                _connectionString,
                tableName,
                _options,
                _driver,
                ownsDriver: _driver == null,
                transactionSession: _transactionSession,
                activeTransaction: _activeTransaction,
                ownsTransaction: false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_ownsTransaction && _activeTransaction != null)
            {
                await RollbackAsync();
            }

            if (_driver != null && _ownsDriver)
            {
                await _driver.DisposeAsync();
                _driver = null;
            }
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is Neo4jException or InvalidOperationException;
    }
}
