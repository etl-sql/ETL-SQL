using System;
using System.Collections.Generic;

namespace ETL_SQL.Connectors
{
    /// <summary>
    /// Centralized utility for building connection strings from named properties for the
    /// provider-agnostic connectors (file, remote/FTP, REST, ODBC). Database providers whose
    /// connection strings require a driver-specific builder live in
    /// <c>DatabaseConnectionStringBuilder</c> in the database connector assembly, which delegates
    /// non-database providers back to this type.
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

        private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "PASSWORD", "PWD", "TOKEN", "ACCESS_TOKEN", "REFRESH_TOKEN", "API_KEY", "CLIENT_SECRET",
            "SECRET", "SASL_PASSWORD", "PASSPHRASE", "KEY", "PRIVATE_KEY"
        };

        /// <summary>
        /// Builds a provider-specific connection string from a property dictionary for the
        /// provider-agnostic connectors. Database providers are handled by
        /// <c>DatabaseConnectionStringBuilder</c>.
        /// </summary>
        /// <param name="provider">Connector type name, e.g. <c>FLATFILE</c>, <c>ODBC</c>, <c>API</c>, <c>FTP</c>.</param>
        /// <param name="props">
        ///   Key/value options. Required keys vary by provider:
        ///   <list type="bullet">
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
                "ODBC" => BuildOdbc(props),
                "API" or "REST" or "HTTP" => BuildRest(props),
                "FTP" or "SFTP" or "SMTP" or "AZURE_BLOB" or "BLOB" or "EMAIL" or "SSH" => BuildRemote(props),
                "FLATFILE" or "CSV" or "EXCEL" or "JSON" or "XML" or "PARQUET" or "AVRO" or "DIRECTORY" or "MOCKDB" => BuildFile(props),
                _ => throw new ArgumentException($"Structured property building is not yet supported for provider: {provider}")
            };
        }

        /// <summary>
        /// Builds a provider-specific connection string with sensitive values replaced for diagnostics.
        /// Use this for logs, exceptions, and support bundles instead of logging <see cref="Build"/>.
        /// </summary>
        public static string BuildForDiagnostics(string provider, Dictionary<string, string> props)
        {
            if (props == null || props.Count == 0)
                return Build(provider, props!);

            return Build(provider, Redact(props));
        }

        /// <summary>
        /// Returns a copy of <paramref name="props"/> with sensitive values replaced by
        /// <c>&lt;redacted&gt;</c>. Shared with <c>DatabaseConnectionStringBuilder</c> so database
        /// diagnostics redact identically.
        /// </summary>
        public static Dictionary<string, string> Redact(Dictionary<string, string> props) =>
            props.ToDictionary(
                kvp => kvp.Key,
                kvp => IsSensitiveKey(kvp.Key) ? "<redacted>" : kvp.Value,
                StringComparer.OrdinalIgnoreCase);

        public static bool IsSensitiveKey(string key) =>
            SensitiveKeys.Contains(key)
            || ETL_SQL.Core.Governance.SecretResolvableFields.IsOrganizationDesignated(key);

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
