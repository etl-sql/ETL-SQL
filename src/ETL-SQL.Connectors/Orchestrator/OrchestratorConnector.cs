using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
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
    ///     CREATE SCHEDULE MonthlySalesNightly ON '0 2 * * *';
    ///     CREATE JOB MonthlySalesRefresh FOR REPORT '/Finance/Monthly Sales';
    ///     ALTER JOB MonthlySalesRefresh ADD SCHEDULE MonthlySalesNightly;
    ///     DROP JOB IF EXISTS MonthlySalesRefresh;
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
            ["API_KEY"] = [],
            // The Portal this connection federates through, and the two credential forms it accepts.
            ["PORTAL_HOST"] = [],
            ["CLIENT_ID"] = [],
            ["CLIENT_SECRET"] = []
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
            string user = GetOption(options, "USER", "");
            string password = GetOption(options, "PASSWORD", "");
            string portalHost = GetOption(options, "PORTAL_HOST", "");
            string clientId = GetOption(options, "CLIENT_ID", "");
            string clientSecret = GetOption(options, "CLIENT_SECRET", "");

            // PASSWORD is overloaded, and USER is what disambiguates it. Alone it is the shared API
            // key, which is the alias the PORTAL connection syntax established and existing scripts
            // already use; paired with USER it is that user's Portal password. Reading it as the API
            // key in the federated form would send a person's password as a shared key.
            string apiKey = GetOption(options, "API_KEY",
                            string.IsNullOrWhiteSpace(user) ? password : "");

            if (options.TryGetValue("PORT", out var port) &&
                !string.IsNullOrWhiteSpace(port) &&
                !host.Contains(':', StringComparison.Ordinal))
            {
                host = host.TrimEnd('/') + $":{port}";
            }

            if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                host = "http://" + host;

            OrchestratorPortalCredentials? credentials = null;
            if (!string.IsNullOrWhiteSpace(portalHost))
            {
                credentials = new OrchestratorPortalCredentials(
                    portalHost, user, password, clientId, clientSecret);
                if (!credentials.IsComplete)
                    throw new ExecutionException(
                        "ORCHESTRATOR(PORTAL_HOST=...) needs a credential to exchange: either " +
                        "USER and PASSWORD, or CLIENT_ID and CLIENT_SECRET.");
            }

            return new OrchestratorDataSource(host, apiKey, _logger, credentials);
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
