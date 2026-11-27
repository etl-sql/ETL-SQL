using System;
using System.Collections.Generic;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core.Common.Exceptions;
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

            ApplySqlServerAuthentication(builder, props);

            // Production Options: Security
            if ((props.TryGetValue("USE_SSL", out var ssl) || props.TryGetValue("ENCRYPT", out ssl)) && ssl != null)
                // COMPAT_BREAK: 0.22.0 — an unquoted ENCRYPT/USE_SSL = TRUE (stored as "ON") now encrypts
                // instead of explicitly turning encryption off.
                builder.Encrypt = ConnectorOptionValues.IsTrue(ssl);

            if (props.TryGetValue("TRUST_SERVER_CERTIFICATE", out var trustCert) && trustCert != null)
                builder.TrustServerCertificate = ConnectorOptionValues.IsTrue(trustCert);

            // Production Options: Failover
            if (props.TryGetValue("APPLICATION_INTENT", out var intent) && intent != null)
                builder.ApplicationIntent = intent.Equals("READONLY", StringComparison.OrdinalIgnoreCase)
                    ? ApplicationIntent.ReadOnly
                    : ApplicationIntent.ReadWrite;

            if (props.TryGetValue("MULTI_SUBNET_FAILOVER", out var failover) && failover != null)
                builder.MultiSubnetFailover = ConnectorOptionValues.IsTrue(failover);

            // Production Options: Pooling
            if (props.TryGetValue("POOLING", out var pooling) && pooling != null)
                builder.Pooling = ConnectorOptionValues.IsTrue(pooling);

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

        private static void ApplySqlServerAuthentication(SqlConnectionStringBuilder builder, Dictionary<string, string> props)
        {
            bool hasAuthMode = props.TryGetValue("AUTH_MODE", out var rawAuthMode);
            bool hasTrusted = props.TryGetValue("TRUSTED_CONNECTION", out var rawTrusted);
            bool trustedTrue = hasTrusted && ConnectorOptionValues.IsTrue(rawTrusted);
            bool hasUser = props.TryGetValue("USER", out var user) && !string.IsNullOrWhiteSpace(user);
            bool hasPassword = props.TryGetValue("PASSWORD", out var password) && !string.IsNullOrWhiteSpace(password);
            bool hasClientId = props.TryGetValue("CLIENT_ID", out var clientId) && !string.IsNullOrWhiteSpace(clientId);

            string mode;
            if (hasAuthMode)
            {
                if (string.IsNullOrWhiteSpace(rawAuthMode))
                    throw new ExecutionException("MSSQL AUTH_MODE must be one of: SQL, WINDOWS, ENTRA_DEFAULT, ENTRA_MANAGED_IDENTITY, ENTRA_SERVICE_PRINCIPAL.");

                mode = rawAuthMode.Trim().ToUpperInvariant();
            }
            else
            {
                mode = trustedTrue ? "WINDOWS" : "SQL";
            }

            switch (mode)
            {
                case "SQL":
                    if (trustedTrue)
                        throw new ExecutionException("MSSQL AUTH_MODE='SQL' cannot be combined with TRUSTED_CONNECTION=TRUE.");
                    if (hasClientId)
                        throw new ExecutionException("MSSQL CLIENT_ID is only valid with Entra AUTH_MODE values.");
                    if (hasUser) builder.UserID = user!;
                    if (hasPassword) builder.Password = password!;
                    break;

                case "WINDOWS":
                    if (hasTrusted && !trustedTrue)
                        throw new ExecutionException("MSSQL AUTH_MODE='WINDOWS' cannot be combined with TRUSTED_CONNECTION=FALSE.");
                    if (hasUser || hasPassword)
                        throw new ExecutionException("MSSQL Windows authentication cannot be combined with USER or PASSWORD.");
                    if (hasClientId)
                        throw new ExecutionException("MSSQL Windows authentication cannot be combined with CLIENT_ID.");
                    builder.IntegratedSecurity = true;
                    break;

                case "ENTRA_DEFAULT":
                    if (hasTrusted)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_DEFAULT' cannot be combined with TRUSTED_CONNECTION.");
                    if (hasUser || hasPassword || hasClientId)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_DEFAULT' does not accept USER, PASSWORD, or CLIENT_ID.");
                    builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
                    break;

                case "ENTRA_MANAGED_IDENTITY":
                    if (hasTrusted)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_MANAGED_IDENTITY' cannot be combined with TRUSTED_CONNECTION.");
                    if (hasUser || hasPassword)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_MANAGED_IDENTITY' does not accept USER or PASSWORD.");
                    builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
                    if (hasClientId)
                        builder.UserID = clientId!;
                    break;

                case "ENTRA_SERVICE_PRINCIPAL":
                    if (hasTrusted)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_SERVICE_PRINCIPAL' cannot be combined with TRUSTED_CONNECTION.");
                    if (hasUser)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_SERVICE_PRINCIPAL' uses CLIENT_ID, not USER.");
                    if (!hasClientId)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_SERVICE_PRINCIPAL' requires CLIENT_ID.");
                    if (!hasPassword)
                        throw new ExecutionException("MSSQL AUTH_MODE='ENTRA_SERVICE_PRINCIPAL' requires PASSWORD (the service principal client secret).");
                    builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryServicePrincipal;
                    builder.UserID = clientId!;
                    builder.Password = password!;
                    break;

                default:
                    throw new ExecutionException(
                        $"Invalid MSSQL AUTH_MODE '{rawAuthMode?.Trim()}'. Allowed values: SQL, WINDOWS, ENTRA_DEFAULT, ENTRA_MANAGED_IDENTITY, ENTRA_SERVICE_PRINCIPAL.");
            }
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
                builder.Pooling = ConnectorOptionValues.IsTrue(pooling);

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

            if (props.TryGetValue("TRUST_SERVER_CERTIFICATE", out var trustCert) && trustCert != null && ConnectorOptionValues.IsTrue(trustCert))
                builder["Trust Server Certificate"] = true;

            if (props.TryGetValue("SEARCH_PATH", out var searchPath))
                builder.SearchPath = ValidateAndNormalizePostgresSearchPath(searchPath);

            return builder.ConnectionString;
        }

        /// <summary>
        /// Validates a PostgreSQL <c>SEARCH_PATH</c> option as a comma-separated list of SQL identifiers
        /// (unquoted identifiers or double-quoted identifiers with doubled-quote escapes) and returns
        /// the normalized comma-separated string. Throws <see cref="ExecutionException"/> on any invalid value.
        /// </summary>
        internal static string ValidateAndNormalizePostgresSearchPath(string? rawSearchPath)
        {
            if (string.IsNullOrWhiteSpace(rawSearchPath))
                throw new ExecutionException("POSTGRES SEARCH_PATH must be a non-empty comma-separated list of schema identifiers.");

            var schemas = new List<string>();
            int i = 0;
            int len = rawSearchPath.Length;

            while (true)
            {
                while (i < len && char.IsWhiteSpace(rawSearchPath[i])) i++;

                if (i >= len)
                    throw new ExecutionException("POSTGRES SEARCH_PATH contains an empty schema entry.");

                string entry;
                if (rawSearchPath[i] == '"')
                {
                    int start = i;
                    i++; // skip opening quote
                    bool closed = false;
                    int contentLen = 0;

                    while (i < len)
                    {
                        char c = rawSearchPath[i];
                        if (c < 0x20 || c == 0x7F)
                            throw new ExecutionException("POSTGRES SEARCH_PATH contains an invalid control character.");

                        if (c == '"')
                        {
                            if (i + 1 < len && rawSearchPath[i + 1] == '"')
                            {
                                // Escaped double quote inside quoted identifier
                                i += 2;
                                contentLen++;
                                continue;
                            }

                            i++; // consume closing quote
                            closed = true;
                            break;
                        }

                        i++;
                        contentLen++;
                    }

                    if (!closed)
                        throw new ExecutionException("POSTGRES SEARCH_PATH has an unclosed quoted schema identifier.");

                    if (contentLen == 0)
                        throw new ExecutionException("POSTGRES SEARCH_PATH contains an empty quoted schema identifier.");

                    entry = rawSearchPath.Substring(start, i - start);
                }
                else
                {
                    int start = i;
                    char first = rawSearchPath[i];
                    if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z') || first == '_'))
                        throw new ExecutionException($"POSTGRES SEARCH_PATH contains an invalid schema identifier starting with '{first}'. Quote special identifiers with double quotes.");

                    i++;
                    while (i < len)
                    {
                        char c = rawSearchPath[i];
                        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '$')
                        {
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    entry = rawSearchPath.Substring(start, i - start);
                }

                schemas.Add(entry);

                while (i < len && char.IsWhiteSpace(rawSearchPath[i])) i++;

                if (i >= len)
                    break;

                if (rawSearchPath[i] != ',')
                    throw new ExecutionException($"POSTGRES SEARCH_PATH contains an invalid schema identifier near '{rawSearchPath[i]}'.");

                i++; // skip comma and expect another identifier
            }

            return string.Join(", ", schemas);
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
                builder.Pooling = ConnectorOptionValues.IsTrue(pooling);

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
                builder.AllowPublicKeyRetrieval = ConnectorOptionValues.IsTrue(pkr);

            if (props.TryGetValue("ALLOW_USER_VARIABLES", out var auv) && auv != null)
                builder.AllowUserVariables = ConnectorOptionValues.IsTrue(auv);

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
                builder.Pooling = ConnectorOptionValues.IsTrue(pooling);

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
