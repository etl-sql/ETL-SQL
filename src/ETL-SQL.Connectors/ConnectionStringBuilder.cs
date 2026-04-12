using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors
{
    /// <summary>
    /// Centralized utility for building provider-specific connection strings from named properties.
    /// Supports MSSQL, PostgreSQL, Oracle, and generic file/remote paths.
    /// </summary>
    public static class ConnectionStringBuilder
    {
        public static string Build(string provider, Dictionary<string, string> props)
        {
            if (props == null || props.Count == 0) return string.Empty;

            return provider.ToUpperInvariant() switch
            {
                "MSSQL" or "SQLSERVER" => BuildSqlServer(props),
                "POSTGRES" or "NPSQL" => BuildPostgres(props),
                "ORACLE" => BuildOracle(props),
                "ODBC" => BuildOdbc(props),
                "API" or "REST" or "HTTP" => BuildRest(props),
                "FTP" or "SFTP" or "SMTP" or "AZURE_BLOB" or "BLOB" or "EMAIL" or "SSH" => BuildRemote(props),
                "FLATFILE" or "CSV" or "EXCEL" or "JSON" or "XML" or "PARQUET" or "AVRO" or "DIRECTORY" => BuildFile(props),
                _ => throw new ArgumentException($"Structured property building is not yet supported for provider: {provider}")
            };
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

            if (props.TryGetValue("PWD", out var pass) || props.TryGetValue("PASSWORD", out pass))
                builder.Append($";PWD={pass}");

            if (props.TryGetValue("CONNECT_TIMEOUT", out var timeout))
                builder.Append($";Connect Timeout={timeout}");

            // Allow arbitrary pass-through properties
            foreach (var kvp in props)
            {
                var key = kvp.Key.ToUpper();
                if (key == "DSN" || key == "DRIVER" || key == "SERVER" || key == "PORT" || 
                    key == "DATABASE" || key == "UID" || key == "USER" || 
                    key == "PWD" || key == "PASSWORD" || key == "CONNECT_TIMEOUT" || 
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
