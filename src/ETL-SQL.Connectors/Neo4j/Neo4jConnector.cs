using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Neo4j
{
    public class Neo4jConnector : IConnector
    {
        public string Name => "NEO4J";
        public IReadOnlyList<string> Aliases => new[] { "NEO" };
        public bool IsFileBased => false;

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return "Neo4j Connector v1.0 (Offline - No Connection String Specified)";
            }

            var host = GetHost(connectionString);
            if (!string.IsNullOrEmpty(host))
            {
                context.SecurityService.ValidateHost(host);
            }

            try
            {
                string user = "";
                string password = "";
                string cleanUri = connectionString;

                try
                {
                    var uri = new Uri(connectionString);
                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var parts = uri.UserInfo.Split(':');
                        user = Uri.UnescapeDataString(parts[0]);
                        if (parts.Length > 1)
                        {
                            password = Uri.UnescapeDataString(parts[1]);
                        }
                        var uriBuilder = new UriBuilder(uri) { UserName = "", Password = "" };
                        cleanUri = uriBuilder.Uri.ToString();
                    }
                }
                catch { }

                var authToken = string.IsNullOrEmpty(user) ? AuthTokens.None : AuthTokens.Basic(user, password);
                var driver = GraphDatabase.Driver(cleanUri, authToken);
                await using (driver)
                {
                    var session = driver.AsyncSession();
                    await using (session)
                    {
                        var result = await session.RunAsync("CALL dbms.components() YIELD name, versions, edition RETURN name, versions[0] AS version, edition");
                        if (await result.FetchAsync())
                        {
                            var version = result.Current["version"]?.ToString() ?? "Unknown";
                            var edition = result.Current["edition"]?.ToString() ?? "Unknown";
                            return $"Neo4j Connector v1.0 (Connected - Server Version: {version} {edition})";
                        }
                    }
                }
                return "Neo4j Connector v1.0 (Connected)";
            }
            catch (Exception ex)
            {
                throw Shared.ConnectorExceptionWrapper.Wrap("Neo4j", ex);
            }
        }

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public string GetHelp() =>
            "Neo4j Connector: Connects to Neo4j graph databases.\n" +
            "Options:\n" +
            "  CONNECTION_STRING: Connection URI (e.g. bolt://localhost:7687 or neo4j://...)\n" +
            "  URI: Alias for CONNECTION_STRING\n" +
            "  DATABASE: Target database name (default: neo4j)\n" +
            "  TIMEOUT_SECONDS: Timeout limit in seconds (default: 30)\n" +
            "  HOST: Server hostname\n" +
            "  PORT: Server port (default: 7687)\n" +
            "  KEY_COLUMNS: Comma-separated node properties used with MERGE instead of CREATE\n" +
            "  FROM_LABEL / TO_LABEL: Edge endpoint labels when loading edges by keys\n" +
            "  FROM_KEY_COLUMN / TO_KEY_COLUMN: Endpoint key property names (default: id)\n" +
            "  USER: User identifier\n" +
            "  PASSWORD: Connection password\n";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "CONNECTION_STRING", Array.Empty<string>() },
            { "URI", Array.Empty<string>() },
            { "DATABASE", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "HOST", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "PROTOCOL", Array.Empty<string>() },
            { "KEY_COLUMNS", Array.Empty<string>() },
            { "FROM_LABEL", Array.Empty<string>() },
            { "TO_LABEL", Array.Empty<string>() },
            { "FROM_KEY_COLUMN", Array.Empty<string>() },
            { "TO_KEY_COLUMN", Array.Empty<string>() },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            var host = GetHost(connectionString, options);
            if (!string.IsNullOrEmpty(host))
            {
                context.SecurityService.ValidateHost(host);
            }

            return new Neo4jDataSource(context, connectionString, null, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            string connectionString = properties.GetValueOrDefault("CONNECTION_STRING", "");
            if (string.IsNullOrEmpty(connectionString))
            {
                properties.TryGetValue("URI", out var uriStr);
                connectionString = uriStr ?? "";
            }
            if (!string.IsNullOrEmpty(connectionString))
            {
                return connectionString;
            }

            string host = properties.GetValueOrDefault("HOST", "localhost");
            string portStr = properties.GetValueOrDefault("PORT", "7687");

            string protocol = "bolt";
            if (properties.TryGetValue("PROTOCOL", out var prot))
            {
                protocol = prot;
            }

            var builder = new System.Text.StringBuilder();
            builder.Append(protocol).Append("://");
            builder.Append(host);
            if (!string.IsNullOrEmpty(portStr))
            {
                builder.Append(':').Append(portStr);
            }

            return builder.ToString();
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            string connStr = connectionString;
            if (string.IsNullOrEmpty(connStr) && options != null)
            {
                connStr = BuildConnectionString(options);
            }
            if (string.IsNullOrEmpty(connStr)) return null;
            try
            {
                var uri = new Uri(connStr);
                return uri.Host;
            }
            catch
            {
                // Fallback for simple hosts
                if (connStr.Contains("://"))
                {
                    var parts = connStr.Split(new[] { "://" }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        var hostPort = parts[1].Split('/')[0].Split('@').Last().Split(':')[0];
                        return hostPort;
                    }
                }
                return null;
            }
        }
    }
}
