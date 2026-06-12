using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace ETL_SQL.Connectors
{
    /// <summary>
    /// Centralized utility for building provider-specific connection strings from named properties.
    /// Supports MSSQL, PostgreSQL, Oracle, and generic file/remote paths.
    /// </summary>
    public static class ConnectionStringBuilder
    {
        private static readonly HashSet<string> ValidProviders = new(StringComparer.OrdinalIgnoreCase)
        {
            "MSSQL", "SQLSERVER", "POSTGRES", "NPSQL", "ORACLE", "ODBC", "MYSQL", "MARIADB",
            "API", "REST", "HTTP",
            "FTP", "SFTP", "SMTP", "AZURE_BLOB", "BLOB", "EMAIL", "SSH",
            "FLATFILE", "CSV", "EXCEL", "JSON", "XML", "PARQUET", "AVRO", "DIRECTORY", "MOCKDB"
        };

        /// <summary>
        /// Builds a provider-specific connection string from a property dictionary.
        /// </summary>
        /// <param name="provider">Connector type name, e.g. <c>MSSQL</c>, <c>POSTGRES</c>, <c>ORACLE</c>, <c>FLATFILE</c>.</param>
        /// <param name="props">
        ///   Key/value options. Required keys vary by provider:
        ///   <list type="bullet">
        ///     <item><b>MSSQL / SQLSERVER</b> — <c>SERVER</c> (required); optional: <c>DATABASE</c>, <c>USER</c>, <c>PASSWORD</c>, <c>TRUSTED_CONNECTION</c>, <c>ENCRYPT</c>, <c>PORT</c>.</item>
        ///     <item><b>POSTGRES / NPSQL</b> — <c>SERVER</c> (required); optional: <c>DATABASE</c>, <c>USER</c>, <c>PASSWORD</c>, <c>PORT</c>, <c>SSL_MODE</c>.</item>
        ///     <item><b>ORACLE</b> — <c>SERVER</c> (required); optional: <c>USER</c>, <c>PASSWORD</c>, <c>PORT</c>, <c>SERVICE_NAME</c>.</item>
        ///     <item><b>ODBC</b> — <c>DSN</c> or <c>DRIVER</c> required; optional: <c>SERVER</c>, <c>DATABASE</c>, <c>USER</c>, <c>PASSWORD</c>.</item>
        ///     <item><b>API / REST / HTTP</b> — <c>URL</c> (required); optional: <c>AUTH_TYPE</c>, <c>TOKEN</c>, <c>USER</c>, <c>PASSWORD</c>.</item>
        ///     <item><b>FTP / SFTP / AZURE_BLOB / EMAIL / SSH</b> — <c>HOST</c> or <c>URL</c> required; optional: <c>USER</c>, <c>PASSWORD</c>, <c>PORT</c>, <c>CONTAINER</c>.</item>
        ///     <item><b>File connectors (FLATFILE, CSV, etc.)</b> — empty string returned; path is set directly on the data source.</item>
        ///   </list>
        ///   All keys are case-insensitive.
        /// </param>
        /// <returns>A ready-to-use connection string, or <see cref="string.Empty"/> if <paramref name="provider"/> or <paramref name="props"/> is null/empty.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="provider"/> is not in <see cref="ValidProviders"/>.</exception>
        public static string Build(string provider, Dictionary<string, string> props)
        {
            if (string.IsNullOrWhiteSpace(provider)) return string.Empty;
            if (props == null || props.Count == 0) return string.Empty;

            ValidateProvider(provider);

            return provider.ToUpperInvariant() switch
            {
                "MSSQL" or "SQLSERVER" => BuildSqlServer(props),
                "POSTGRES" or "NPSQL" => BuildPostgres(props),
                "MYSQL" or "MARIADB" => BuildMySql(props),
                "ORACLE" => BuildOracle(props),
                "ODBC" => BuildOdbc(props),
                "API" or "REST" or "HTTP" => BuildRest(props),
                "FTP" or "SFTP" or "SMTP" or "AZURE_BLOB" or "BLOB" or "EMAIL" or "SSH" => BuildRemote(props),
                "FLATFILE" or "CSV" or "EXCEL" or "JSON" or "XML" or "PARQUET" or "AVRO" or "DIRECTORY" or "MOCKDB" => BuildFile(props),
                _ => throw new ArgumentException($"Structured property building is not yet supported for provider: {provider}")
            };
        }

        private static void ValidateProvider(string provider)
        {
            if (ValidProviders.Contains(provider)) return;

            var suggestion = ValidProviders
                .Select(p => new { Name = p, Distance = GetDistance(provider.ToUpperInvariant(), p) })
                .Where(x => x.Distance <= 2)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            string message = suggestion != null
                ? $"Unsupported provider: '{provider}'. Did you mean '{suggestion.Name}'?"
                : $"Unsupported provider: '{provider}'. Supported providers include: {string.Join(", ", ValidProviders.Take(10))}...";

            throw new ArgumentException(message);
        }

        private static int GetDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) d[0, j] = j;

            for (int j = 1; j <= t.Length; j++)
            {
                for (int i = 1; i <= s.Length; i++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[s.Length, t.Length];
        }

        private static string BuildSqlServer(Dictionary<string, string> props)
        {
            var builder = new SqlConnectionStringBuilder();

            if (props.TryGetValue("SERVER", out var server)) builder.DataSource = server;
            if (props.TryGetValue("DATABASE", out var db)) builder.InitialCatalog = db;

            bool trusted = props.ContainsKey("TRUSTED_CONNECTION") &&
                          props["TRUSTED_CONNECTION"].Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (trusted)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                if (props.TryGetValue("USER", out var user)) builder.UserID = user;
                if (props.TryGetValue("PASSWORD", out var pass)) builder.Password = pass;
            }

            // Production Options: Security
            if ((props.TryGetValue("USE_SSL", out var ssl) || props.TryGetValue("ENCRYPT", out ssl)) && ssl != null)
                builder.Encrypt = ssl.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (props.TryGetValue("TRUST_SERVER_CERTIFICATE", out var trustCert) && trustCert != null)
                builder.TrustServerCertificate = trustCert.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            // Production Options: Failover
            if (props.TryGetValue("APPLICATION_INTENT", out var intent) && intent != null)
                builder.ApplicationIntent = intent.Equals("READONLY", StringComparison.OrdinalIgnoreCase)
                    ? ApplicationIntent.ReadOnly
                    : ApplicationIntent.ReadWrite;

            if (props.TryGetValue("MULTI_SUBNET_FAILOVER", out var failover) && failover != null)
                builder.MultiSubnetFailover = failover.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            // Production Options: Pooling
            if (props.TryGetValue("MIN_POOL_SIZE", out var minPool) && int.TryParse(minPool, out var min))
                builder.MinPoolSize = min;

            if (props.TryGetValue("MAX_POOL_SIZE", out var maxPool) && int.TryParse(maxPool, out var max))
                builder.MaxPoolSize = max;

            if (props.TryGetValue("POOL_LIFETIME", out var lifetime) && int.TryParse(lifetime, out var wait))
                builder.LoadBalanceTimeout = wait;

            // Production Options: Timeouts
            if (props.TryGetValue("CONNECT_TIMEOUT", out var connTimeout) && int.TryParse(connTimeout, out var ct))
                builder.ConnectTimeout = ct;

            return builder.ConnectionString;
        }

        private static string BuildPostgres(Dictionary<string, string> props)
        {
            var builder = new NpgsqlConnectionStringBuilder();

            if (props.TryGetValue("HOST", out var host)) builder.Host = host;
            if (props.TryGetValue("DATABASE", out var db)) builder.Database = db;
            if (props.TryGetValue("USER", out var user)) builder.Username = user;
            if (props.TryGetValue("PASSWORD", out var pass)) builder.Password = pass;
            if (props.TryGetValue("PORT", out var portStr) && int.TryParse(portStr, out var port)) builder.Port = port;

            // Production Options: Pooling
            if (props.TryGetValue("POOLING", out var pooling) && pooling != null)
                builder.Pooling = pooling.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (props.TryGetValue("MIN_POOL_SIZE", out var minPool) && int.TryParse(minPool, out var min))
                builder.MinPoolSize = min;

            if (props.TryGetValue("MAX_POOL_SIZE", out var maxPool) && int.TryParse(maxPool, out var max))
                builder.MaxPoolSize = max;

            if (props.TryGetValue("CONNECTION_IDLE_LIFETIME", out var idle) && int.TryParse(idle, out var i))
                builder.ConnectionIdleLifetime = i;

            // Production Options: Security
            if (props.TryGetValue("SSL_MODE", out var sslMode) && sslMode != null)
                if (Enum.TryParse<SslMode>(sslMode, true, out var mode))
                    builder.SslMode = mode;

            if (props.TryGetValue("TRUST_SERVER_CERTIFICATE", out var trustCert) && trustCert != null && trustCert.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                builder["Trust Server Certificate"] = true;

            return builder.ConnectionString;
        }

        private static string BuildMySql(Dictionary<string, string> props)
        {
            var builder = new MySqlConnector.MySqlConnectionStringBuilder();

            if (props.TryGetValue("HOST", out var host)) builder.Server = host;
            else if (props.TryGetValue("SERVER", out var server)) builder.Server = server;

            if (props.TryGetValue("DATABASE", out var db)) builder.Database = db;

            if (props.TryGetValue("USER", out var user)) builder.UserID = user;
            else if (props.TryGetValue("UID", out var uid)) builder.UserID = uid;

            if (props.TryGetValue("PASSWORD", out var pass)) builder.Password = pass;

            if (props.TryGetValue("PORT", out var portStr) && uint.TryParse(portStr, out var port)) builder.Port = port;

            // Production Options: Pooling
            if (props.TryGetValue("POOLING", out var pooling) && pooling != null)
                builder.Pooling = pooling.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (props.TryGetValue("MIN_POOL_SIZE", out var minPool) && uint.TryParse(minPool, out var min))
                builder.MinimumPoolSize = min;

            if (props.TryGetValue("MAX_POOL_SIZE", out var maxPool) && uint.TryParse(maxPool, out var max))
                builder.MaximumPoolSize = max;

            // Production Options: Security
            if (props.TryGetValue("SSL_MODE", out var sslMode) && sslMode != null)
            {
                if (Enum.TryParse<MySqlConnector.MySqlSslMode>(sslMode, true, out var mode))
                    builder.SslMode = mode;
            }

            if (props.TryGetValue("ALLOW_PUBLIC_KEY_RETRIEVAL", out var pkr) && pkr != null)
                builder.AllowPublicKeyRetrieval = pkr.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (props.TryGetValue("ALLOW_USER_VARIABLES", out var auv) && auv != null)
                builder.AllowUserVariables = auv.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            // Production Options: Timeouts
            if (props.TryGetValue("CONNECT_TIMEOUT", out var connTimeout) && uint.TryParse(connTimeout, out var ct))
                builder.ConnectionTimeout = ct;

            return builder.ConnectionString;
        }

        private static string BuildOracle(Dictionary<string, string> props)
        {
            var builder = new OracleConnectionStringBuilder();

            if (props.TryGetValue("USER", out var user)) builder.UserID = user;
            if (props.TryGetValue("PASSWORD", out var pass)) builder.Password = pass;

            // Oracle can use TNS_NAME or Easy Connect (HOST:PORT/SERVICE_NAME)
            if (props.TryGetValue("TNS_NAME", out var tns))
            {
                builder.DataSource = tns;
            }
            else if (props.TryGetValue("HOST", out var host))
            {
                var port = props.TryGetValue("PORT", out var p) ? p : "1521";
                var service = props.TryGetValue("SERVICE_NAME", out var s) ? s : "";

                if (!string.IsNullOrEmpty(service))
                    builder.DataSource = $"{host}:{port}/{service}";
                else
                    builder.DataSource = $"{host}:{port}";
            }

            // Production Options: Pooling
            if (props.TryGetValue("POOLING", out var pooling) && pooling != null)
                builder.Pooling = pooling.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (props.TryGetValue("MIN_POOL_SIZE", out var minPool) && int.TryParse(minPool, out var min))
                builder.MinPoolSize = min;

            if (props.TryGetValue("MAX_POOL_SIZE", out var maxPool) && int.TryParse(maxPool, out var max))
                builder.MaxPoolSize = max;

            if (props.TryGetValue("CONNECTION_LIFETIME", out var lifetime) && int.TryParse(lifetime, out var l))
                builder.ConnectionLifeTime = l;

            return builder.ConnectionString;
        }

        private static string BuildRemote(Dictionary<string, string> props)
        {
            // For remote connectors like FTP, the "connection string" is often just the host
            // with Port/User/Pass passed in as separate options to the connector itself.
            // However, we return the HOST here if present.
            if (props.TryGetValue("HOST", out var host)) return host;
            return string.Empty;
        }

        private static string BuildFile(Dictionary<string, string> props)
        {
            // For file connectors, the "connection string" is the path.
            if (props.TryGetValue("PATH", out var path)) return path;
            return string.Empty;
        }

        private static string BuildOdbc(Dictionary<string, string> props)
        {
            var builder = new System.Text.StringBuilder();

            // DSN takes precedence
            if (props.TryGetValue("DSN", out var dsn) && !string.IsNullOrEmpty(dsn))
            {
                builder.Append($"DSN={dsn}");
            }
            else if (props.TryGetValue("DRIVER", out var driver) && !string.IsNullOrEmpty(driver))
            {
                // Ensure driver is enclosed in {} if not already
                if (!driver.StartsWith("{")) driver = "{" + driver + "}";
                builder.Append($"DRIVER={driver}");

                if (props.TryGetValue("SERVER", out var srv)) builder.Append($";SERVER={srv}");
                if (props.TryGetValue("PORT", out var port)) builder.Append($";PORT={port}");
                if (props.TryGetValue("DATABASE", out var db)) builder.Append($";DATABASE={db}");
            }

            if (props.TryGetValue("UID", out var user) || props.TryGetValue("USER", out user))
                builder.Append($";UID={user}");

            if (props.TryGetValue("PASSWORD", out var pass))
                builder.Append($";PWD={pass}");

            if (props.TryGetValue("CONNECT_TIMEOUT", out var timeout))
                builder.Append($";Connect Timeout={timeout}");

            // Allow arbitrary pass-through properties
            foreach (var kvp in props)
            {
                var key = kvp.Key.ToUpper();
                if (key == "DSN" || key == "DRIVER" || key == "SERVER" || key == "PORT" ||
                    key == "DATABASE" || key == "UID" || key == "USER" ||
                    key == "PASSWORD" || key == "CONNECT_TIMEOUT" ||
                    key == "TABLE") continue;

                builder.Append($";{kvp.Key}={kvp.Value}");
            }

            return builder.ToString();
        }

        private static string BuildRest(Dictionary<string, string> props)
        {
            // For REST connectors, the "connection string" is the base URL.
            if (props.TryGetValue("URL", out var url)) return url;
            return string.Empty;
        }
    }
}
