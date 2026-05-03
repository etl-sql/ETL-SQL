using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.BigQuery.V2;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Shared;

namespace ETL_SQL.Connectors.BigQuery
{
    /// <summary>
    /// Data source for Google BigQuery.
    /// Uses the Google.Cloud.BigQuery.V2 REST client; not ADO.NET.
    /// Auth: service-account JSON file (CREDENTIAL_FILE option) or ADC when omitted.
    /// Transactions are not supported — BigQuery DML is auto-committed per statement.
    /// </summary>
    public class BigQueryDataSource : IDatabaseSource
    {
        private readonly string _connectionString;
        private readonly string? _tableName;
        private readonly Dictionary<string, string>? _options;
        private readonly ILogger _logger;
        private readonly IExecutionContext _context;

        private readonly string _projectId;
        private readonly string? _dataset;
        private readonly string? _credentialFile;
        private readonly string? _location;

        public BigQueryDataSource(IExecutionContext context, string connectionString, string? tableName, Dictionary<string, string>? options)
        {
            _context = context;
            _logger = context.Logger;
            _connectionString = connectionString;
            _tableName = tableName;
            _options = options;

            _projectId = options?.GetValueOrDefault("PROJECT_ID")
                ?? BigQueryConnector.ParseField(connectionString, "project")
                ?? throw new ExecutionException("BigQuery: PROJECT_ID is required.");

            _dataset = options?.GetValueOrDefault("DATASET")
                ?? BigQueryConnector.ParseField(connectionString, "dataset");

            var rawCred = options?.GetValueOrDefault("CREDENTIAL_FILE")
                ?? BigQueryConnector.ParseField(connectionString, "credential_file");
            _credentialFile = string.IsNullOrWhiteSpace(rawCred) ? null : context.ResolvePath(rawCred);

            _location = options?.GetValueOrDefault("LOCATION")
                ?? BigQueryConnector.ParseField(connectionString, "location");

            context.SecurityService.ValidateHost("bigquery.googleapis.com");
        }

        public string ConnectionString => _connectionString;
        public string Path => "BIGQUERY";
        public string Dialect => "BIGQUERY";
        public bool SupportsSqlPushdown => true;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "BIGQUERY";

        public IDataSource WithTable(string tableName)
            => new BigQueryDataSource(_context, _connectionString, tableName, _options);

        public async Task<string> GetVersionAsync()
        {
            try
            {
                var client = await CreateClientAsync();
                await client.ExecuteQueryAsync("SELECT 1", null);
                return $"BigQuery (project: {_projectId})";
            }
            catch (Google.GoogleApiException ex)
            {
                throw new ExecutionException($"BigQuery error: {ex.Error?.Message ?? ex.Message}");
            }
        }

        public HashSet<string> GetSupportedFunctions() => BigQuerySyntax.Functions;

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for BigQuery data source read.");

            var client = await CreateClientAsync();
            BigQueryResults? results = null;
            try
            {
                await ConnectorRetryPolicy.ForBigQuery(_logger)
                    .ExecuteAsync(async ct =>
                    {
                        results = await client.ExecuteQueryAsync(
                            $"SELECT * FROM {QuoteIdentifier(_tableName)}",
                            null, BuildQueryOptions(), null, ct);
                    });
            }
            catch (Google.GoogleApiException ex)
            {
                throw new ExecutionException($"BigQuery read error: {ex.Error?.Message ?? ex.Message}");
            }

            var fieldNames = results!.Schema?.Fields?.Select(f => f.Name).ToList() ?? new List<string>();
            var batch = new DataTable();
            batch.SetColumns(fieldNames);

            foreach (BigQueryRow row in results)
            {
                var r = batch.NewRow();
                for (int i = 0; i < fieldNames.Count; i++)
                    r[i] = row[fieldNames[i]];
                await batch.AddRowAsync(r);

                if (batch.Rows.Count >= batchSize)
                {
                    yield return batch;
                    batch = new DataTable();
                    batch.SetColumns(fieldNames);
                }
            }
            if (batch.Rows.Count > 0) yield return batch;
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (string.IsNullOrEmpty(_tableName))
                throw new ExecutionException("No table specified for BigQuery data source write.");

            if (!append) await TruncateAsync();

            var (_, ds, table) = ParseTableName(_tableName);
            var datasetId = ds ?? _dataset
                ?? throw new ExecutionException("BigQuery: DATASET is required for write operations.");

            var client = await CreateClientAsync();
            try
            {
                await foreach (var batch in batches)
                {
                    if (batch.Rows.Count == 0) continue;

                    var rows = batch.Rows.Select(row =>
                    {
                        var insertRow = new BigQueryInsertRow();
                        foreach (var col in batch.ColumnNames)
                        {
                            var val = row[col];
                            insertRow[col] = val == DBNull.Value ? null : val;
                        }
                        return insertRow;
                    }).ToList();

                    await ConnectorRetryPolicy.ForBigQuery(_logger)
                        .ExecuteAsync(async ct =>
                        {
                            await client.InsertRowsAsync(datasetId, table, rows, null, ct);
                        });
                }
            }
            catch (Google.GoogleApiException ex)
            {
                throw new ExecutionException($"BigQuery streaming insert error: {ex.Error?.Message ?? ex.Message}");
            }
        }

        public async IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
        {
            var client = await CreateClientAsync();
            var bqParams = BuildParameters(parameters);

            BigQueryResults? results = null;
            try
            {
                await ConnectorRetryPolicy.ForBigQuery(_logger)
                    .ExecuteAsync(async ct =>
                    {
                        results = await client.ExecuteQueryAsync(sql, bqParams, BuildQueryOptions(), null, ct);
                    });
            }
            catch (Google.GoogleApiException ex)
            {
                throw new ExecutionException($"BigQuery error: {ex.Error?.Message ?? ex.Message}");
            }

            var fieldNames = results!.Schema?.Fields?.Select(f => f.Name).ToList() ?? new List<string>();
            var batch = new DataTable { ResultSetIndex = 0 };
            batch.SetColumns(fieldNames);

            foreach (BigQueryRow row in results)
            {
                var r = batch.NewRow();
                for (int i = 0; i < fieldNames.Count; i++)
                    r[i] = row[fieldNames[i]];
                await batch.AddRowAsync(r);

                if (batch.Rows.Count >= 10000)
                {
                    yield return batch;
                    batch = new DataTable { ResultSetIndex = 0 };
                    batch.SetColumns(fieldNames);
                }
            }

            batch.RowsAffected = fieldNames.Count == 0 ? -1 : batch.Rows.Count;
            yield return batch;
        }

        public async Task<IEnumerable<string>> GetColumnsAsync()
        {
            if (string.IsNullOrEmpty(_tableName)) return Enumerable.Empty<string>();
            return await GetColumnsAsync(_tableName);
        }

        public async Task<IEnumerable<string>> GetTablesAsync()
        {
            var ds = _dataset ?? throw new ExecutionException("BigQuery: DATASET required for schema introspection.");
            var client = await CreateClientAsync();
            var sql = $"SELECT table_name FROM `{_projectId}.{ds}.INFORMATION_SCHEMA.TABLES` WHERE table_type = 'BASE TABLE' ORDER BY table_name";
            try
            {
                var results = await client.ExecuteQueryAsync(sql, null);
                return results
                    .Select(r => r["table_name"]?.ToString() ?? "")
                    .Where(n => n.Length > 0)
                    .ToList();
            }
            catch (Google.GoogleApiException ex)
            {
                throw new ExecutionException($"BigQuery error: {ex.Error?.Message ?? ex.Message}");
            }
        }

        public async Task<IEnumerable<string>> GetViewsAsync()
        {
            var ds = _dataset ?? throw new ExecutionException("BigQuery: DATASET required for schema introspection.");
            var client = await CreateClientAsync();
            var sql = $"SELECT table_name FROM `{_projectId}.{ds}.INFORMATION_SCHEMA.VIEWS` ORDER BY table_name";
            try
            {
                var results = await client.ExecuteQueryAsync(sql, null);
                return results
                    .Select(r => r["table_name"]?.ToString() ?? "")
                    .Where(n => n.Length > 0)
                    .ToList();
            }
            catch (Google.GoogleApiException ex)
            {
                throw new ExecutionException($"BigQuery error: {ex.Error?.Message ?? ex.Message}");
            }
        }

        public async Task<IEnumerable<string>> GetColumnsAsync(string tableName)
        {
            var (proj, ds, table) = ParseTableName(tableName);
            var projectId  = proj ?? _projectId;
            var datasetId  = ds   ?? _dataset
                ?? throw new ExecutionException("BigQuery: DATASET required for column introspection.");

            var client = await CreateClientAsync();
            var sql = $"SELECT column_name FROM `{projectId}.{datasetId}.INFORMATION_SCHEMA.COLUMNS` " +
                      "WHERE table_name = @tableName ORDER BY ordinal_position";
            try
            {
                var results = await client.ExecuteQueryAsync(sql,
                    new[] { new BigQueryParameter("tableName", BigQueryDbType.String, table) });
                return results
                    .Select(r => r["column_name"]?.ToString() ?? "")
                    .Where(n => n.Length > 0)
                    .ToList();
            }
            catch (Google.GoogleApiException ex)
            {
                throw new ExecutionException($"BigQuery error: {ex.Error?.Message ?? ex.Message}");
            }
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private async Task<BigQueryClient> CreateClientAsync()
        {
            try
            {
                if (_credentialFile != null)
                {
#pragma warning disable CS0618
                    var credential = GoogleCredential.FromFile(_credentialFile)
                        .CreateScoped("https://www.googleapis.com/auth/bigquery");
#pragma warning restore CS0618
                    return await BigQueryClient.CreateAsync(_projectId, credential);
                }
                return await BigQueryClient.CreateAsync(_projectId);
            }
            catch (Exception ex) when (ex is not ExecutionException)
            {
                throw new ExecutionException($"BigQuery: failed to create client — {ex.Message}");
            }
        }

        private QueryOptions? BuildQueryOptions()
        {
            if (_dataset == null && _location == null) return null;
            var opts = new QueryOptions();
            if (_dataset != null)
                opts.DefaultDataset = new Google.Apis.Bigquery.v2.Data.DatasetReference
                    { ProjectId = _projectId, DatasetId = _dataset };
            if (_location != null)
                opts.JobLocation = _location;
            return opts;
        }

        private async Task TruncateAsync()
        {
            await foreach (var _ in ExecuteRawSql($"TRUNCATE TABLE {QuoteIdentifier(_tableName!)}")) { }
        }

        private static IEnumerable<BigQueryParameter>? BuildParameters(IEnumerable<object?>? parameters)
        {
            if (parameters == null) return null;
            var list = parameters.ToList();
            return list.Count == 0
                ? null
                : list.Select((p, i) => new BigQueryParameter($"p{i}", BigQueryDbType.String, p?.ToString()));
        }

        private static (string? project, string? dataset, string table) ParseTableName(string name)
        {
            var parts = name.Split('.');
            return parts.Length switch
            {
                3 => (parts[0], parts[1], parts[2]),
                2 => (null,     parts[0], parts[1]),
                _ => (null,     null,     parts[0])
            };
        }

        private static string QuoteIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var parts = name.Split('.');
            return string.Join(".", parts.Select(p =>
                p.StartsWith('`') ? p : $"`{p.Replace("`", "\\`")}`"));
        }
    }
}
