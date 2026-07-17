using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Orchestrator
{
    /// <summary>
    /// Connector type for connecting to a remote ETL-SQL Orchestrator service.
    /// Usage inside an ETL-SQL script:
    /// <code>
    /// CREATE CONNECTION orch AS ORCHESTRATOR(
    ///     HOST = 'http://orch-server:5001',
    ///     API_KEY = ENC:...
    /// );
    ///
    /// EXECUTE orch BEGIN
    ///     CREATE REFRESH JOB FOR REPORT 'Monthly Sales' SCHEDULE '0 2 * * *' AT orch;
    ///     DROP REFRESH JOB FOR REPORT 'Monthly Sales';
    /// END
    /// </code>
    /// </summary>
    public sealed class OrchestratorConnector : IConnector
    {
        private readonly ILogger _logger;

        public OrchestratorConnector(ILogger logger) => _logger = logger;

        public string Name => "ORCHESTRATOR";
        public IReadOnlyList<string> Aliases => ["ORCH"];

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult("Orchestrator");

        public HashSet<string> GetSupportedFunctions() => [];
        public HashSet<string> GetSupportedKeywords() => [];
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["HOST"] = [],
            ["PORT"] = [],
            ["USER"] = [],
            ["PASSWORD"] = [],
            ["API_KEY"] = []
        };
        public Dictionary<string, string[]> GetOptionValues() => [];
        public string GetHelp() =>
            "ORCHESTRATOR — connects to an ETL-SQL Orchestrator service for remote job management.\n" +
            "Usage: CREATE CONNECTION <alias> AS ORCHESTRATOR(HOST='http://...', PASSWORD='api-key');\n" +
            "Then: EXECUTE <alias> BEGIN ... END";

        public IDataSource CreateDataSource(
            IExecutionContext context,
            string connectionString,
            Dictionary<string, string>? options = null)
        {
            options ??= [];

            string host = GetOption(options, "HOST", connectionString);
            // API_KEY takes precedence; PASSWORD is accepted as a convenience alias matching
            // the PORTAL connection syntax that users already know.
            string apiKey = GetOption(options, "API_KEY",
                            GetOption(options, "PASSWORD", ""));

            if (options.TryGetValue("PORT", out var port) &&
                !string.IsNullOrWhiteSpace(port) &&
                !host.Contains(':', StringComparison.Ordinal))
            {
                host = host.TrimEnd('/') + $":{port}";
            }

            if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                host = "http://" + host;

            return new OrchestratorDataSource(host, apiKey, _logger);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult<IEnumerable<string>>([]);

        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult<IEnumerable<string>>([]);

        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) =>
            Task.FromResult<IEnumerable<string>>([]);

        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult<IEnumerable<string>>([]);

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            options ??= [];
            string host = GetOption(options, "HOST", connectionString);
            if (string.IsNullOrEmpty(host)) return null;

            try
            {
                if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    host = "http://" + host;
                var uri = new Uri(host);
                return uri.Host;
            }
            catch { return host; }
        }

        private static string GetOption(Dictionary<string, string> options, string key, string fallback)
        {
            if (options.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                return val;
            return fallback;
        }
    }
}
