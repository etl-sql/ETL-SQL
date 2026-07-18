using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    public class MongodbDataSource : IDataSource
    {
        private string _connectionString;
        private readonly string _databaseName;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        private IMongoClient? _client;

        public MongodbDataSource(IExecutionContext context, string connectionString, string databaseName, string? tableName = null, Dictionary<string, string>? options = null, IMongoClient? client = null)
        {
            _context = context;
            _logger = context.Logger;
            _tableName = tableName;
            _options = options;
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

        private IMongoClient GetClient()
        {
            if (_client != null) return _client;
            _client = new MongoClient(_connectionString);
            return _client;
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

            var client = GetClient();
            var db = client.GetDatabase(_databaseName);
            var coll = db.GetCollection<BsonDocument>(_tableName);

            try
            {
                if (!append)
                {
                    await db.DropCollectionAsync(_tableName, effectiveCancellationToken);
                }

                await foreach (var batch in batches.WithCancellation(effectiveCancellationToken))
                {
                    if (batch.Rows.Count == 0) continue;

                    var docs = new List<BsonDocument>();
                    foreach (var row in batch.Rows)
                    {
                        var doc = new BsonDocument();
                        foreach (var col in batch.ColumnNames)
                        {
                            var val = row[col];
                            doc[col] = ConvertToBsonValue(val);
                        }
                        docs.Add(doc);
                    }

                    await coll.InsertManyAsync(docs, options: null, cancellationToken: effectiveCancellationToken);
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("MongoDB", ex);
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => GetColumnsInternalAsync();

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

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is MongoException or InvalidOperationException;

        private CancellationToken EffectiveCancellationToken(CancellationToken cancellationToken) =>
            cancellationToken.CanBeCanceled ? cancellationToken : (_context?.CancellationToken ?? CancellationToken.None);
    }
}
