using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.BigQuery
{
    /// <summary>
    /// Native connector for Google BigQuery.
    /// Supports service-account JSON auth and Application Default Credentials (ADC).
    /// </summary>
    public class BigQueryConnector : IConnector
    {
        public string Name => "BIGQUERY";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public int CommandTimeoutSeconds => 1800;
        public bool IsDataWarehouse => true;

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) =>
            new BigQueryDataSource(context, connectionString, null, null).GetVersionAsync();

        public HashSet<string> GetSupportedFunctions() => BigQuerySyntax.Functions;
        public HashSet<string> GetSupportedKeywords() => BigQuerySyntax.Additions;
        public HashSet<string> GetExcludedKeywords() => BigQuerySyntax.Exclusions;

        public string GetHelp() =>
            "BIGQUERY Connector: Native connector for Google BigQuery.\n" +
            "Options:\n" +
            "  PROJECT_ID: GCP project ID (required).\n" +
            "  DATASET: Default dataset (equivalent to schema).\n" +
            "  CREDENTIAL_FILE: Path to service account JSON key file.\n" +
            "                   Omit to use Application Default Credentials (ADC / workload identity).\n" +
            "  LOCATION: BigQuery job location (e.g. US, EU, us-central1). Default: US.";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "PROJECT_ID",      Array.Empty<string>() },
            { "DATASET",         Array.Empty<string>() },
            { "CREDENTIAL_FILE", Array.Empty<string>() },
            { "LOCATION",        new[] { "US", "EU", "us-central1", "europe-west1", "asia-east1" } },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
            => new BigQueryDataSource(context, connectionString, null, options);

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString)
            => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString)
            => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName)
            => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString)
            => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            var parts = new List<string>();
            if (properties.TryGetValue("PROJECT_ID",      out var proj)) parts.Add($"project={proj}");
            if (properties.TryGetValue("DATASET",         out var ds))   parts.Add($"dataset={ds}");
            if (properties.TryGetValue("LOCATION",        out var loc))  parts.Add($"location={loc}");
            if (properties.TryGetValue("CREDENTIAL_FILE", out var cred) && !string.IsNullOrWhiteSpace(cred))
                parts.Add($"credential_file={cred}");
            return string.Join(";", parts) + ";";
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
            => GetHostStatic(connectionString, options);

        public static string? GetHostStatic(string connectionString, Dictionary<string, string>? options = null)
            => "bigquery.googleapis.com";

        internal static string? ParseField(string connectionString, string key)
        {
            foreach (var part in connectionString.Split(';'))
            {
                var kv = part.Trim().Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kv[1];
            }
            return null;
        }
    }
}
