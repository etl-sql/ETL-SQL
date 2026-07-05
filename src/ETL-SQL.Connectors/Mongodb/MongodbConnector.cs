using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ETL_SQL.Connectors.Mongodb
{
    public class MongodbConnector : IConnector
    {
        public string Name => "MONGODB";
        public IReadOnlyList<string> Aliases => new[] { "MONGO" };
        public bool IsFileBased => false;

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return "MongoDB Connector v1.0 (Offline - No Connection String Specified)";
            }

            var host = GetHost(connectionString);
            if (!string.IsNullOrEmpty(host))
            {
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
            }

            try
            {
                var client = new MongoClient(connectionString);
                var adminDb = client.GetDatabase("admin");
                var pingCommand = new BsonDocument("ping", 1);
                await adminDb.RunCommandAsync<BsonDocument>(pingCommand);

                var buildInfoCommand = new BsonDocument("buildInfo", 1);
                var buildInfoResult = await adminDb.RunCommandAsync<BsonDocument>(buildInfoCommand);
                string mongoVersion = buildInfoResult.GetValue("version", "Unknown")?.ToString() ?? "Unknown";

                return $"MongoDB Connector v1.0 (Connected - Server Version: {mongoVersion})";
            }
            catch (Exception ex)
            {
                throw Shared.ConnectorExceptionWrapper.Wrap("MongoDB", ex);
            }
        }

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public string GetHelp() =>
            "MongoDB Connector: Connects to MongoDB document databases.\n" +
            "Options:\n" +
            "  CONNECTION_STRING: Connection URI (e.g. mongodb://localhost:27017)\n" +
            "  DATABASE: Target database name (required)\n" +
            "  COLLECTION: Collection context\n" +
            "  TIMEOUT_SECONDS: Timeout limit in seconds (default: 30)\n" +
            "  HOST: Server hostname\n" +
            "  PORT: Server port\n" +
            "  USER: User identifier\n" +
            "  PASSWORD: Connection password\n";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "CONNECTION_STRING", Array.Empty<string>() },
            { "DATABASE", Array.Empty<string>() },
            { "COLLECTION", Array.Empty<string>() },
            { "TIMEOUT_SECONDS", Array.Empty<string>() },
            { "HOST", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string? db = null;
            string? collection = null;

            if (options != null)
            {
                options.TryGetValue("DATABASE", out db);
                options.TryGetValue("COLLECTION", out collection);
            }

            if (string.IsNullOrEmpty(db))
            {
                try
                {
                    var mongoUrl = new MongoUrl(connectionString);
                    db = mongoUrl.DatabaseName;
                }
                catch
                {
                    // Ignore URL parse failure here, standard datasource constructor will handle it
                }
            }

            // Egress Security Hardening: Validate host against egress policies
            var host = GetHost(connectionString, options);
            if (!string.IsNullOrEmpty(host))
            {
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
            }

            return new MongodbDataSource(context, connectionString, db ?? "test", collection, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            string connectionString = properties.GetValueOrDefault("CONNECTION_STRING", "");
            if (!string.IsNullOrEmpty(connectionString))
            {
                return connectionString;
            }

            string host = properties.GetValueOrDefault("HOST", "localhost");
            string portStr = properties.GetValueOrDefault("PORT", "27017");
            string user = properties.GetValueOrDefault("USER", "");
            string password = properties.GetValueOrDefault("PASSWORD", "");
            string database = properties.GetValueOrDefault("DATABASE", "");

            var builder = new System.Text.StringBuilder("mongodb://");
            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
            {
                builder.Append(Uri.EscapeDataString(user))
                       .Append(':')
                       .Append(Uri.EscapeDataString(password))
                       .Append('@');
            }
            builder.Append(host);
            if (!string.IsNullOrEmpty(portStr))
            {
                builder.Append(':').Append(portStr);
            }
            if (!string.IsNullOrEmpty(database))
            {
                builder.Append('/').Append(Uri.EscapeDataString(database));
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
                var mongoUrl = new MongoUrl(connStr);
                var server = mongoUrl.Servers.FirstOrDefault();
                return server?.Host;
            }
            catch
            {
                if (Uri.TryCreate(connStr, UriKind.Absolute, out var uri))
                {
                    return uri.Host;
                }
                return null;
            }
        }
    }
}
