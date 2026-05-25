using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Snowflake
{
    /// <summary>
    /// Native connector for Snowflake Cloud Data Platform.
    /// Supports username+password and private-key JWT authentication.
    /// </summary>
    public class SnowflakeConnector : IConnector
    {
        public string Name => "SNOWFLAKE";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public int CommandTimeoutSeconds => 1800;
        public bool IsDataWarehouse => true;

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) =>
            new SnowflakeDataSource(context, connectionString, null, null).GetVersionAsync();

        public HashSet<string> GetSupportedFunctions() => SnowflakeSyntax.Functions;
        public HashSet<string> GetSupportedKeywords() => SnowflakeSyntax.Additions;
        public HashSet<string> GetExcludedKeywords() => SnowflakeSyntax.Exclusions;

        public string GetHelp() =>
            "SNOWFLAKE Connector: Native connector for Snowflake Cloud Data Platform.\n" +
            "Options:\n" +
            "  HOST: Account identifier (e.g. myorg-myaccount or myorg-myaccount.snowflakecomputing.com).\n" +
            "  ACCOUNT: Snowflake account name override, useful with local emulators.\n" +
            "  PORT: Optional Snowflake service port, useful with local emulators.\n" +
            "  PROTOCOL: Optional protocol (https or http), useful with local emulators.\n" +
            "  DATABASE: Target database.\n" +
            "  SCHEMA: Target schema (default: PUBLIC).\n" +
            "  WAREHOUSE: Virtual warehouse for query execution.\n" +
            "  USERNAME: Snowflake user name.\n" +
            "  PASSWORD: Password (for username/password auth).\n" +
            "  PRIVATE_KEY_FILE: Path to RSA private key PEM file (for key-pair JWT auth).";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HOST",             Array.Empty<string>() },
            { "ACCOUNT",          Array.Empty<string>() },
            { "PORT",             Array.Empty<string>() },
            { "PROTOCOL",         Array.Empty<string>() },
            { "DATABASE",         Array.Empty<string>() },
            { "SCHEMA",           Array.Empty<string>() },
            { "WAREHOUSE",        Array.Empty<string>() },
            { "USERNAME",         Array.Empty<string>() },
            { "PASSWORD",         Array.Empty<string>() },
            { "PRIVATE_KEY_FILE",   Array.Empty<string>() },
            { "TIMEOUT_SECONDS",   Array.Empty<string>() },
        };

        public Dictionary<string, string[]> GetOptionValues() => new();

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
            => new SnowflakeDataSource(context, connectionString, null, options);

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

            properties.TryGetValue("HOST", out var host);

            if (properties.TryGetValue("ACCOUNT", out var account))
                parts.Add($"account={account}");
            else if (!string.IsNullOrWhiteSpace(host))
                parts.Add($"account={NormalizeAccount(host)}");

            if (!string.IsNullOrWhiteSpace(host) && IsLocalOrExplicitEndpoint(host))
                parts.Add($"host={host}");

            if (properties.TryGetValue("PORT", out var port))
                parts.Add($"port={port}");

            if (properties.TryGetValue("PROTOCOL", out var protocol))
                parts.Add($"scheme={protocol}");

            if (properties.TryGetValue("USERNAME", out var user))
                parts.Add($"user={user}");

            if (properties.TryGetValue("PRIVATE_KEY_FILE", out var pkFile) && !string.IsNullOrWhiteSpace(pkFile))
            {
                parts.Add("authenticator=snowflake_jwt");
                parts.Add($"private_key_file={pkFile}");
            }
            else if (properties.TryGetValue("PASSWORD", out var password))
            {
                parts.Add($"password={password}");
            }

            if (properties.TryGetValue("WAREHOUSE", out var warehouse))
                parts.Add($"warehouse={warehouse}");
            if (properties.TryGetValue("DATABASE", out var db))
                parts.Add($"db={db}");
            if (properties.TryGetValue("SCHEMA", out var schema))
                parts.Add($"schema={schema}");

            return string.Join(";", parts) + ";";
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
            => GetHostStatic(connectionString, options);

        public static string? GetHostStatic(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options?.TryGetValue("HOST", out var host) == true) return host;
            return ParseValueFromConnectionString(connectionString, "host")
                ?? ParseValueFromConnectionString(connectionString, "account");
        }

        internal static string NormalizeAccount(string host)
        {
            const string suffix = ".snowflakecomputing.com";
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? host[..^suffix.Length]
                : host;
        }

        internal static bool IsLocalOrExplicitEndpoint(string host)
        {
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
                || host.Contains(':', StringComparison.Ordinal);
        }

        private static string? ParseValueFromConnectionString(string cs, string key)
        {
            foreach (var part in cs.Split(';'))
            {
                var kv = part.Trim().Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kv[1];
            }
            return null;
        }
    }
}
