using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.ReportPortal
{
    /// <summary>
    /// Connector type for connecting to a remote ETL-SQL Report Portal.
    /// Usage inside an ETL-SQL script:
    /// <code>
    /// CREATE CONNECTION portal AS REPORTPORTAL(
    ///     HOST = 'http://report-server:5000',
    ///     USER = 'admin',
    ///     PASSWORD = ENC:...
    /// );
    ///
    /// EXECUTE portal BEGIN
    ///     CREATE USER 'alice' WITH (EMAIL='alice@company.com', PASSWORD=ENC:..., ROLE=Viewer);
    ///     CREATE FOLDER '/Finance';
    ///     GRANT READ ON FOLDER '/Finance' TO GROUP 'Finance';
    ///     PUBLISH REPORT 'Monthly Sales' FROM 'reports/monthly.rptsql' IN FOLDER '/Finance';
    /// END
    /// </code>
    /// </summary>
    public sealed class ReportPortalConnector : IConnector
    {
        private readonly ILogger _logger;

        public ReportPortalConnector(ILogger logger) => _logger = logger;

        public string Name => "REPORTPORTAL";
        public IReadOnlyList<string> Aliases => ["REPORT_PORTAL"];

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult("Report Portal");

        public HashSet<string> GetSupportedFunctions() => [];
        public HashSet<string> GetSupportedKeywords() => [];
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["HOST"] = [],
            ["PORT"] = [],
            ["USER"] = [],
            ["PASSWORD"] = []
        };
        public Dictionary<string, string[]> GetOptionValues() => [];
        public string GetHelp() =>
            "REPORTPORTAL — connects to an ETL-SQL Report Portal for remote administration.\n" +
            "Usage: CREATE CONNECTION <alias> AS REPORTPORTAL(HOST='http://...', USER='admin', PASSWORD=ENC:...);\n" +
            "Then: EXECUTE <alias> BEGIN ... END";

        public IDataSource CreateDataSource(
            IExecutionContext context,
            string connectionString,
            Dictionary<string, string>? options = null)
        {
            options ??= [];

            string host = GetOption(options, "HOST", connectionString);
            string user = GetOption(options, "USER", "admin");
            string pass = GetOption(options, "PASSWORD", "");

            if (pass.StartsWith("ENC:") && context != null)
            {
                pass = context.DecryptValue(pass) ?? "";
            }

            // If HOST doesn't include a port but PORT is specified, append it
            if (options.TryGetValue("PORT", out var port) &&
                !string.IsNullOrWhiteSpace(port) &&
                !host.Contains(':', StringComparison.Ordinal))
            {
                host = host.TrimEnd('/') + $":{port}";
            }

            if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                host = "http://" + host;

            return new ReportPortalDataSource(host, user, pass, _logger);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult<IEnumerable<string>>([]);

        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult<IEnumerable<string>>([]);

        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) =>
            Task.FromResult<IEnumerable<string>>([]);

        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult<IEnumerable<string>>([]);

        private static string GetOption(Dictionary<string, string> options, string key, string fallback)
        {
            if (options.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                return val;
            return fallback;
        }
    }
}
