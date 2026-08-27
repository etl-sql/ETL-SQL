using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace ETL_SQL.Connectors
{
    /// <summary>
    /// Builds connection strings for the database providers whose native driver exposes a
    /// strongly-typed connection-string builder (SQL Server, PostgreSQL, MySQL/MariaDB, Oracle).
    /// Keeping these here isolates the database driver package references to the database connector
    /// assembly; provider-agnostic providers (file, remote, REST, ODBC) are delegated to
    /// <see cref="ConnectionStringBuilder"/>.
    /// </summary>
    public static class DatabaseConnectionStringBuilder
    {
        /// <summary>
        /// Builds a connection string for a database provider, or delegates to
        /// <see cref="ConnectionStringBuilder.Build"/> for non-database providers.
        /// </summary>
        public static string Build(string provider, Dictionary<string, string> props)
        {
            if (string.IsNullOrWhiteSpace(provider)) return string.Empty;
            if (props == null || props.Count == 0) return string.Empty;

            return provider.ToUpperInvariant() switch
            {
                "MSSQL" or "SQLSERVER" => BuildSqlServer(props),
                "POSTGRES" or "NPSQL" => BuildPostgres(props),
                "MYSQL" or "MARIADB" => BuildMySql(props),
                "ORACLE" => BuildOracle(props),
                _ => ConnectionStringBuilder.Build(provider, props)
            };
        }

        /// <summary>
        /// Builds a database connection string with sensitive values replaced for diagnostics.
        /// Redaction is shared with <see cref="ConnectionStringBuilder"/> so all providers redact
        /// identically.
        /// </summary>
        public static string BuildForDiagnostics(string provider, Dictionary<string, string> props)
        {
            if (props == null || props.Count == 0)
                return Build(provider, props!);

            return Build(provider, ConnectionStringBuilder.Redact(props));
        }

        private static string BuildSqlServer(Dictionary<string, string> props)
        {
            var builder = new SqlConnectionStringBuilder();

            if (props.TryGetValue("SERVER", out var server))
            {
                if (props.TryGetValue("PORT", out var port) && !string.IsNullOrWhiteSpace(port) && !server.Contains(','))
                {
                    builder.DataSource = $"{server},{port}";
                }
                else
                {
                    builder.DataSource = server;
                }
            }
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
            if (props.TryGetValue("POOLING", out var pooling) && pooling != null)
                builder.Pooling = pooling.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

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

            if (props.TryGetValue("CONNECTION_IDLE_TIMEOUT", out var idleTimeout) && uint.TryParse(idleTimeout, out var idle))
                builder.ConnectionIdleTimeout = idle;

            if (props.TryGetValue("CONNECTION_LIFETIME", out var lifetime) && uint.TryParse(lifetime, out var life))
                builder.ConnectionLifeTime = life;

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
    }
}
