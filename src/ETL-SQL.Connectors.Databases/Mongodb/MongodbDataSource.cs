using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ETL_SQL.Connectors.Mongodb
{
    public class MongodbDataSource : IDatabaseSource
    {
        private string _connectionString;
        private readonly string _databaseName;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly IReadOnlyList<string> _keyColumns;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private IMongoClient? _client;

        internal static IReadOnlyList<string> ParseKeyColumns(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ExecutionException("MONGODB KEY_COLUMNS must be a non-empty comma-separated list of column names.");

            var parts = raw.Split(',');
            var result = new List<string>(parts.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    throw new ExecutionException("MONGODB KEY_COLUMNS contains an empty column name.");

                if (seen.Add(trimmed))
                    result.Add(trimmed);
            }

            return result;
        }

        public MongodbDataSource(IExecutionContext context, string connectionString, string databaseName, string? tableName = null, Dictionary<string, string>? options = null, IMongoClient? client = null)
        {
            _context = context;
            _logger = context.Logger;
            _tableName = tableName;
            _options = options;
            _keyColumns = options != null && options.TryGetValue("KEY_COLUMNS", out var rawKeys)
                ? ParseKeyColumns(rawKeys)
                : Array.Empty<string>();
            _client = client;
            _databaseName = databaseName;

            string connStr = connectionString;
            if (options != null && string.IsNullOrEmpty(connStr))
            {
                var decryptedOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
                if (decryptedOptions.TryGetValue("PASSWORD", out var pwd) && pwd.StartsWith("ENC:") && context != null)
                {
                    decryptedOptions["PASSWORD"] = context.DecryptValue(pwd) ?? "";
                }

                var conn = new MongodbConnector();
                connStr = conn.BuildConnectionString(decryptedOptions);
            }
            _connectionString = connStr;
        }

        public string Path => _tableName ?? _databaseName;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "MONGODB";
        public string ConnectionString => _connectionString;
        public string Dialect => "MONGODB";
        public bool SupportsSqlPushdown => false;

        public HashSet<string> GetSupportedFunctions() => new();

        public Task<string> GetVersionAsync() =>
            new MongodbConnector().GetVersionAsync(_context!, _connectionString);

        public Task<IEnumerable<string>> GetViewsAsync() =>
            Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) =>
            WithTable(tableName).GetColumnsAsync();

        public Task<IEnumerable<string>> GetColumnsAsync(string tableName, CancellationToken cancellationToken) =>
            WithTable(tableName).GetColumnsAsync(cancellationToken);

        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) =>
            ExecuteRawSql(sql, parameters, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ExecuteRawSql(
            string sql,
            IEnumerable<object?>? parameters,
            CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ExecuteRawSqlCore(sql, cancellationToken),
                "MongoDB",
                ShouldWrapProviderException);

        private sealed record ParsedMongoCommand(
            string Collection,
            BsonDocument? Filter,
            BsonDocument? Projection,
            BsonDocument? Sort,
            int? Limit,
            int? Skip,
            IReadOnlyList<BsonDocument>? Pipeline);

        private static readonly HashSet<string> AllowedTopLevelKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "collection", "filter", "pipeline", "projection", "sort", "limit", "skip"
        };

        private static readonly HashSet<string> ForbiddenPipelineStages = new(StringComparer.OrdinalIgnoreCase)
        {
            "$out", "$merge"
        };

        private ParsedMongoCommand ParseAndValidateCommand(string commandJson)
        {
            if (string.IsNullOrWhiteSpace(commandJson))
                throw new ExecutionException("MongoDB EXECUTE command cannot be empty.");

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(commandJson);
            }
            catch (JsonException ex)
            {
                long line = (ex.LineNumber ?? 0) + 1;
                long col = (ex.BytePositionInLine ?? 0) + 1;
                throw new ExecutionException($"Invalid MongoDB EXECUTE JSON at line {line}, column {col}.");
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ExecutionException("MongoDB EXECUTE command must be a JSON object.");

                string? collection = null;
                JsonElement? filterElem = null;
                JsonElement? pipelineElem = null;
                JsonElement? projectionElem = null;
                JsonElement? sortElem = null;
                int? limit = null;
                int? skip = null;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!AllowedTopLevelKeys.Contains(prop.Name))
                    {
                        throw new ExecutionException(
                            $"MongoDB EXECUTE pass-through is read-only and does not allow key '{prop.Name}'. " +
                            "Allowed keys: collection, filter, pipeline, projection, sort, limit, skip.");
                    }

                    switch (prop.Name.ToLowerInvariant())
                    {
                        case "collection":
                            if (prop.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(prop.Value.GetString()))
                                throw new ExecutionException("MongoDB EXECUTE 'collection' must be a non-empty string.");
                            collection = prop.Value.GetString()!.Trim();
                            break;

                        case "filter":
                            if (prop.Value.ValueKind != JsonValueKind.Object)
                                throw new ExecutionException("MongoDB EXECUTE 'filter' must be a JSON object.");
                            filterElem = prop.Value;
                            break;

                        case "pipeline":
                            if (prop.Value.ValueKind != JsonValueKind.Array)
                                throw new ExecutionException("MongoDB EXECUTE 'pipeline' must be a JSON array of stage objects.");
                            pipelineElem = prop.Value;
                            break;

                        case "projection":
                            if (prop.Value.ValueKind != JsonValueKind.Object)
                                throw new ExecutionException("MongoDB EXECUTE 'projection' must be a JSON object.");
                            projectionElem = prop.Value;
                            break;

                        case "sort":
                            if (prop.Value.ValueKind != JsonValueKind.Object)
                                throw new ExecutionException("MongoDB EXECUTE 'sort' must be a JSON object.");
                            sortElem = prop.Value;
                            break;

                        case "limit":
                            if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out var lim) || lim <= 0)
                                throw new ExecutionException("MongoDB EXECUTE 'limit' must be a positive integer.");
                            limit = lim;
                            break;

                        case "skip":
                            if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out var sk) || sk < 0)
                                throw new ExecutionException("MongoDB EXECUTE 'skip' must be a non-negative integer.");
                            skip = sk;
                            break;
                    }
                }

                collection ??= _tableName;
                if (string.IsNullOrWhiteSpace(collection))
                    throw new ExecutionException("MongoDB EXECUTE command must specify 'collection' (or set COLLECTION on the connection).");

                if (filterElem.HasValue && pipelineElem.HasValue)
                    throw new ExecutionException("MongoDB EXECUTE command cannot specify both 'filter' and 'pipeline'.");

                if (!filterElem.HasValue && !pipelineElem.HasValue)
                    throw new ExecutionException("MongoDB EXECUTE command must specify either 'filter' or 'pipeline'.");

                if (pipelineElem.HasValue)
                {
                    if (projectionElem.HasValue || sortElem.HasValue || limit.HasValue || skip.HasValue)
                        throw new ExecutionException("MongoDB EXECUTE 'projection', 'sort', 'limit', and 'skip' are only valid with 'filter'; use pipeline stages instead.");

                    var stages = new List<BsonDocument>();
                    foreach (var stage in pipelineElem.Value.EnumerateArray())
                    {
                        if (stage.ValueKind != JsonValueKind.Object)
                            throw new ExecutionException("Each stage in MongoDB EXECUTE 'pipeline' must be a JSON object.");

                        RejectWriteStagesRecursive(stage);
                        stages.Add(BsonDocument.Parse(stage.GetRawText()));
                    }

                    return new ParsedMongoCommand(collection, null, null, null, null, null, stages);
                }

                var filterDoc = BsonDocument.Parse(filterElem!.Value.GetRawText());
                var projectionDoc = projectionElem.HasValue ? BsonDocument.Parse(projectionElem.Value.GetRawText()) : null;
                var sortDoc = sortElem.HasValue ? BsonDocument.Parse(sortElem.Value.GetRawText()) : null;

                return new ParsedMongoCommand(collection, filterDoc, projectionDoc, sortDoc, limit, skip, null);
            }
        }

        private static void RejectWriteStagesRecursive(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (ForbiddenPipelineStages.Contains(prop.Name))
                    {
                        throw new ExecutionException(
                            $"MongoDB EXECUTE pass-through is read-only; pipeline stage '{prop.Name}' is not allowed.");
                    }

                    if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                        RejectWriteStagesRecursive(prop.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                        RejectWriteStagesRecursive(item);
                }
            }
        }

        private async IAsyncEnumerable<DataTable> ExecuteRawSqlCore(
            string commandJson,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var parsed = ParseAndValidateCommand(commandJson);
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);

            if (_context != null && _context.IsWhatIf)
            {
                string mode = parsed.Pipeline != null ? "aggregate" : "find";
                _logger.WriteLine($"WHAT IF: Would execute MongoDB {mode} on collection '{parsed.Collection}'.", ConsoleColor.Yellow);
                yield break;
            }

            var client = GetClient();
            var db = client.GetDatabase(_databaseName);
            var coll = db.GetCollection<BsonDocument>(parsed.Collection);

            IAsyncCursor<BsonDocument> cursor;
            if (parsed.Pipeline != null)
            {
                var pipelineDef = PipelineDefinition<BsonDocument, BsonDocument>.Create(parsed.Pipeline);
                cursor = await coll.AggregateAsync(pipelineDef, options: null, cancellationToken: effectiveCancellationToken);
            }
            else
            {
                var findOptions = new FindOptions<BsonDocument>
                {
                    Projection = parsed.Projection,
                    Sort = parsed.Sort,
                    Limit = parsed.Limit,
                    Skip = parsed.Skip
                };
                cursor = await coll.FindAsync(parsed.Filter!, findOptions, effectiveCancellationToken);
            }

            const int batchSize = 10000;
            var docs = new List<BsonDocument>(Math.Min(batchSize, 256));

            using (cursor)
            {
                while (await cursor.MoveNextAsync(effectiveCancellationToken))
                {
                    foreach (var doc in cursor.Current)
                    {
                        docs.Add(doc);
                        if (docs.Count >= batchSize)
                        {
                            yield return await BuildBatchFromDocumentsAsync(docs);
                            docs.Clear();
                        }
                    }
                }
            }

            if (docs.Count > 0)
            {
                yield return await BuildBatchFromDocumentsAsync(docs);
            }
        }

        private async Task<DataTable> BuildBatchFromDocumentsAsync(List<BsonDocument> docs)
        {
            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var doc in docs)
            {
                foreach (var name in doc.Names)
                {
                    if (seen.Add(name))
                        columns.Add(name);
                }
            }

            var batch = new DataTable();
            batch.SetColumns(columns);

            foreach (var doc in docs)
            {
                var row = batch.NewRow();
                foreach (var col in columns)
                {
                    row[col] = doc.TryGetValue(col, out var val) ? ConvertBsonValue(val) : null;
                }
                await batch.AddRowAsync(row);
            }

            return batch;
        }

        private IMongoClient GetClient()
        {
            if (_client != null) return _client;
            _client = new MongoClient(BuildClientSettings());
            return _client;
        }

        /// <summary>
        /// Driver settings from the connection URL, with TIMEOUT_SECONDS applied to server selection
        /// and connect. The option was advertised but never reached the driver.
        /// </summary>
        internal MongoClientSettings BuildClientSettings()
        {
            var settings = MongoClientSettings.FromConnectionString(_connectionString);
            if (_options != null
                && _options.TryGetValue("TIMEOUT_SECONDS", out var raw)
                && int.TryParse(raw, out var seconds)
                && seconds > 0)
            {
                var timeout = TimeSpan.FromSeconds(seconds);
                settings.ServerSelectionTimeout = timeout;
                settings.ConnectTimeout = timeout;
            }
            return settings;
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ReadBatches(batchSize, CancellationToken.None);

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize, CancellationToken cancellationToken) =>
            ConnectorExceptionWrapper.WrapAsync(
                ReadBatchesCore(batchSize, cancellationToken),
                "MongoDB",
                ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No collection specified for MongoDB data source read.");
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);

            var client = GetClient();
            var db = client.GetDatabase(_databaseName);
            var coll = db.GetCollection<BsonDocument>(_tableName);

            var columnNames = (await GetColumnsInternalAsync(effectiveCancellationToken)).ToList();

            IAsyncCursor<BsonDocument> cursor;
            try
            {
                cursor = await coll.FindAsync(
                    new BsonDocument(),
                    options: null,
                    cancellationToken: effectiveCancellationToken);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MongoDB", ex);
            }

            var currentBatch = new DataTable();
            currentBatch.SetColumns(columnNames);

            using (cursor)
            {
                while (await cursor.MoveNextAsync(effectiveCancellationToken))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var row = currentBatch.NewRow();
                        foreach (var col in columnNames)
                        {
                            if (doc.TryGetValue(col, out var val))
                            {
                                row[col] = ConvertBsonValue(val);
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

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            WriteBatches(batches, append, CancellationToken.None);

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append, CancellationToken cancellationToken)
        {
            var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
            if (_context != null && _context.IsWhatIf) return;

            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No collection specified for MongoDB data source write.");

            try
            {
                // A malformed URL fails in the MongoClient constructor (MongoConfigurationException),
                // so the client is created inside the wrapped region.
                var client = GetClient();
                var db = client.GetDatabase(_databaseName);
                var coll = db.GetCollection<BsonDocument>(_tableName);

                if (!append)
                {
                    await db.DropCollectionAsync(_tableName, effectiveCancellationToken);
                }

                long rowOrdinal = 0;
                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                {
                    if (batch.Rows.Count == 0) continue;

                    if (_keyColumns.Count == 0)
                    {
                        var docs = new List<BsonDocument>(batch.Rows.Count);
                        foreach (var row in batch.Rows)
                        {
                            var doc = new BsonDocument();
                            foreach (var col in batch.ColumnNames)
                            {
                                doc[col] = ConvertToBsonValue(row[col]);
                            }
                            docs.Add(doc);
                        }

                        await coll.InsertManyAsync(docs, options: null, cancellationToken: effectiveCancellationToken);
                    }
                    else
                    {
                        var resolvedKeys = ResolveBatchKeyColumns(batch.ColumnNames, _keyColumns);
                        var writes = new List<WriteModel<BsonDocument>>(batch.Rows.Count);

                        foreach (var row in batch.Rows)
                        {
                            rowOrdinal++;
                            var filter = new BsonDocument();
                            foreach (var keyCol in resolvedKeys)
                            {
                                var keyVal = row[keyCol];
                                if (keyVal == null || keyVal == DBNull.Value)
                                    throw new ExecutionException($"MongoDB upsert failed at row {rowOrdinal}: key column '{keyCol}' is NULL.");

                                filter[keyCol] = ConvertToBsonValue(keyVal);
                            }

                            var doc = new BsonDocument();
                            foreach (var col in batch.ColumnNames)
                            {
                                doc[col] = ConvertToBsonValue(row[col]);
                            }

                            writes.Add(new ReplaceOneModel<BsonDocument>(filter, doc) { IsUpsert = true });
                        }

                        await coll.BulkWriteAsync(writes, options: null, cancellationToken: effectiveCancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MongoDB", ex);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsAsync(CancellationToken.None);

        public Task<IEnumerable<string>> GetColumnsAsync(CancellationToken cancellationToken) =>
            GetColumnsInternalAsync(cancellationToken);

        public async Task<IEnumerable<string>> GetTablesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var effectiveCancellationToken = EffectiveCancellationToken(cancellationToken);
                var client = GetClient();
                var db = client.GetDatabase(_databaseName);
                using var cursor = await db.ListCollectionNamesAsync(cancellationToken: effectiveCancellationToken);
                return await cursor.ToListAsync(effectiveCancellationToken);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MongoDB", ex);
            }
        }

        public Task<IEnumerable<string>> GetTablesAsync() => GetTablesAsync(CancellationToken.None);

        private async Task<IEnumerable<string>> GetColumnsInternalAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();
            try
            {
                var client = GetClient();
                var db = client.GetDatabase(_databaseName);
                var coll = db.GetCollection<BsonDocument>(_tableName);
                var firstDoc = await coll
                    .Find(new BsonDocument())
                    .FirstOrDefaultAsync(EffectiveCancellationToken(cancellationToken));
                if (firstDoc != null)
                {
                    return firstDoc.Names;
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MongoDB", ex);
            }
            return new[] { "_id" };
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public IDataSource WithTable(string tableName)
        {
            return new MongodbDataSource(_context!, _connectionString, _databaseName, tableName, _options, _client);
        }

        public ValueTask DisposeAsync()
        {
            _client = null;
            return ValueTask.CompletedTask;
        }

        private object? ConvertBsonValue(BsonValue val)
        {
            if (val == null || val.IsBsonNull) return null;
            if (val.IsBsonDocument || val.IsBsonArray)
            {
                return val.ToJson();
            }
            try
            {
                return BsonTypeMapper.MapToDotNetValue(val);
            }
            catch
            {
                return val.ToString();
            }
        }

        private BsonValue ConvertToBsonValue(object? val)
        {
            if (val == null || val == DBNull.Value) return BsonNull.Value;
            if (val is string strVal && (strVal.StartsWith("{") && strVal.EndsWith("}") || strVal.StartsWith("[") && strVal.EndsWith("]")))
            {
                try
                {
                    return BsonDocument.Parse(strVal);
                }
                catch
                {
                    try
                    {
                        return MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonArray>(strVal);
                    }
                    catch
                    {
                        // Fallback to plain BsonValue
                    }
                }
            }
            return BsonValue.Create(val);
        }

        private static IReadOnlyList<string> ResolveBatchKeyColumns(IReadOnlyList<string> batchColumns, IReadOnlyList<string> configuredKeys)
        {
            var resolved = new List<string>(configuredKeys.Count);
            foreach (var key in configuredKeys)
            {
                var match = batchColumns.FirstOrDefault(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    throw new ExecutionException($"MongoDB upsert failed: KEY_COLUMNS entry '{key}' was not found in the source batch columns.");
                resolved.Add(match);
            }
            return resolved;
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is MongoException or InvalidOperationException;

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);
    }
}
