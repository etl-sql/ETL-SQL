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

            if (props.TryGetValue("USE_SSL", out var ssl))
                builder.Encrypt = ssl.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

            if (props.TryGetValue("TRUST_SERVER_CERTIFICATE", out var trustCert))
                builder.TrustServerCertificate = trustCert.Equals("TRUE", StringComparison.OrdinalIgnoreCase);

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
    }
}
