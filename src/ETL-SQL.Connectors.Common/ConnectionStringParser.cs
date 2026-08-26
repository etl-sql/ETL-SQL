using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Connectors;

/// <summary>
/// Result of parsing an unformatted connection string, ADO.NET / ODBC string, or URI.
/// </summary>
public sealed record ParsedConnectionStringResult(
    string? DetectedProvider,
    Dictionary<string, string> Options,
    string? ExtractedCredential,
    string? SuggestedSecretKey);

/// <summary>
/// Utility for parsing raw ADO.NET, ODBC, JDBC, or URI connection strings into canonical ETL-SQL connector options.
/// Automatically isolates raw credentials for zero-trust secret extraction.
/// </summary>
public static class ConnectionStringParser
{
    private static readonly Dictionary<string, string> StandardKeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DATA SOURCE"] = "SERVER",
        ["SERVER"] = "SERVER",
        ["HOST"] = "SERVER",
        ["HOSTNAME"] = "SERVER",
        ["ADDR"] = "SERVER",
        ["ADDRESS"] = "SERVER",
        ["NETWORK ADDRESS"] = "SERVER",

        ["INITIAL CATALOG"] = "DATABASE",
        ["DATABASE"] = "DATABASE",
        ["DB"] = "DATABASE",

        ["USER ID"] = "USER",
        ["USERID"] = "USER",
        ["UID"] = "USER",
        ["USER"] = "USER",
        ["USERNAME"] = "USER",

        ["PASSWORD"] = "PASSWORD",
        ["PWD"] = "PASSWORD",
        ["PASS"] = "PASSWORD",

        ["PORT"] = "PORT",

        ["TRUSTED_CONNECTION"] = "TRUSTED_CONNECTION",
        ["TRUSTED CONNECTION"] = "TRUSTED_CONNECTION",
        ["INTEGRATED SECURITY"] = "TRUSTED_CONNECTION",

        ["TRUSTSERVERCERTIFICATE"] = "TRUST_SERVER_CERTIFICATE",
        ["TRUST SERVER CERTIFICATE"] = "TRUST_SERVER_CERTIFICATE",
        ["TRUST_SERVER_CERTIFICATE"] = "TRUST_SERVER_CERTIFICATE",

        ["ENCRYPT"] = "ENCRYPT",
        ["SSL"] = "ENCRYPT",
        ["USE_SSL"] = "USE_SSL",
        ["SSLMODE"] = "SSL_MODE",
        ["SSL MODE"] = "SSL_MODE",

        ["CONNECT TIMEOUT"] = "CONNECT_TIMEOUT",
        ["CONNECTION TIMEOUT"] = "CONNECT_TIMEOUT",
        ["CONNECT_TIMEOUT"] = "CONNECT_TIMEOUT",
        ["TIMEOUT"] = "TIMEOUT_SECONDS",
        ["COMMAND TIMEOUT"] = "TIMEOUT_SECONDS",
        ["TIMEOUT_SECONDS"] = "TIMEOUT_SECONDS",

        ["APPLICATION NAME"] = "APPLICATION_NAME",
        ["APPLICATIONNAME"] = "APPLICATION_NAME",
        ["APP"] = "APPLICATION_NAME",

        ["APPLICATION INTENT"] = "APPLICATION_INTENT",
        ["APPLICATIONINTENT"] = "APPLICATION_INTENT",
        ["APPLICATION_INTENT"] = "APPLICATION_INTENT",

        ["MULTI SUBNET FAILOVER"] = "MULTI_SUBNET_FAILOVER",
        ["MULTISUBNETFAILOVER"] = "MULTI_SUBNET_FAILOVER",
        ["MULTI_SUBNET_FAILOVER"] = "MULTI_SUBNET_FAILOVER",

        ["POOLING"] = "POOLING",
        ["MIN POOL SIZE"] = "MIN_POOL_SIZE",
        ["MINPOOLSIZE"] = "MIN_POOL_SIZE",
        ["MAX POOL SIZE"] = "MAX_POOL_SIZE",
        ["MAXPOOLSIZE"] = "MAX_POOL_SIZE",

        ["DRIVER"] = "DRIVER",
        ["DSN"] = "DSN",
        ["PATH"] = "PATH",
        ["FILE"] = "PATH",
        ["FILENAME"] = "PATH"
    };

    /// <summary>
    /// Parses a raw connection string (key-value delimited or URI format) into canonical ETL-SQL connector options.
    /// </summary>
    /// <param name="rawString">The raw connection string or URI.</param>
    /// <param name="hintProvider">Optional expected connector type, e.g. "MSSQL", "POSTGRES".</param>
    public static ParsedConnectionStringResult Parse(string rawString, string? hintProvider = null)
    {
        if (string.IsNullOrWhiteSpace(rawString))
            return new ParsedConnectionStringResult(hintProvider, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, null);

        var trimmed = rawString.Trim();

        // 1. Check for URI schemes (postgres://, mysql://, sftp://, etc.)
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Scheme) && uri.Scheme.Length > 1 && !trimmed.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUri(uri, hintProvider);
        }

        // 2. Parse standard Key=Value; pairs
        return ParseKeyValueString(trimmed, hintProvider);
    }

    private static ParsedConnectionStringResult ParseKeyValueString(string raw, string? hintProvider)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? extractedCredential = null;
        string? detectedProvider = hintProvider;

        var tokens = SplitKeyValuePairs(raw);
        foreach (var (rawKey, rawVal) in tokens)
        {
            var key = rawKey.Trim();
            var val = rawVal.Trim();

            // Detect provider hints
            if (key.Equals("DRIVER", StringComparison.OrdinalIgnoreCase))
            {
                if (val.Contains("SQL Server", StringComparison.OrdinalIgnoreCase)) detectedProvider ??= "MSSQL";
                else if (val.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase)) detectedProvider ??= "POSTGRES";
                else if (val.Contains("MySQL", StringComparison.OrdinalIgnoreCase)) detectedProvider ??= "MYSQL";
                else if (val.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) detectedProvider ??= "ORACLE";
            }

            var canonicalKey = StandardKeyAliases.TryGetValue(key, out var mapped) ? mapped : key.ToUpperInvariant().Replace(' ', '_');

            // Normalize boolean values
            if (val.Equals("True", StringComparison.OrdinalIgnoreCase) || val.Equals("SSPI", StringComparison.OrdinalIgnoreCase))
            {
                val = "ON";
            }
            else if (val.Equals("False", StringComparison.OrdinalIgnoreCase))
            {
                val = "OFF";
            }

            // Extract credentials
            if (canonicalKey.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) || canonicalKey.Equals("PWD", StringComparison.OrdinalIgnoreCase))
            {
                extractedCredential = val;
                canonicalKey = "PASSWORD";
            }

            options[canonicalKey] = val;
        }

        // Handle host:port syntax in SERVER
        if (options.TryGetValue("SERVER", out var srv) && srv.Contains(','))
        {
            var parts = srv.Split(',', 2);
            options["SERVER"] = parts[0].Trim();
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p))
            {
                options["PORT"] = p.ToString();
            }
        }
        else if (options.TryGetValue("SERVER", out var srvCol) && srvCol.Contains(':') && !srvCol.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = srvCol.Split(':', 2);
            options["SERVER"] = parts[0].Trim();
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p))
            {
                options["PORT"] = p.ToString();
            }
        }
        else if (options.TryGetValue("SERVER", out var srvTcp) && srvTcp.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            var clean = srvTcp["tcp:".Length..];
            if (clean.Contains(','))
            {
                var parts = clean.Split(',', 2);
                options["SERVER"] = parts[0].Trim();
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p)) options["PORT"] = p.ToString();
            }
            else
            {
                options["SERVER"] = clean.Trim();
            }
        }

        string? suggestedKey = null;
        if (!string.IsNullOrEmpty(extractedCredential))
        {
            var db = options.TryGetValue("DATABASE", out var d) ? d : "DB";
            var s = options.TryGetValue("SERVER", out var srvName) ? srvName.Replace('.', '_') : "CONN";
            suggestedKey = $"{detectedProvider ?? "CONN"}_{db.ToUpperInvariant()}_PW";
        }

        return new ParsedConnectionStringResult(detectedProvider, options, extractedCredential, suggestedKey);
    }

    private static ParsedConnectionStringResult ParseUri(Uri uri, string? hintProvider)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? extractedCredential = null;
        var scheme = uri.Scheme.ToLowerInvariant();

        var detectedProvider = hintProvider ?? scheme switch
        {
            "postgres" or "postgresql" => "POSTGRES",
            "mysql" => "MYSQL",
            "sqlserver" or "mssql" => "MSSQL",
            "oracle" => "ORACLE",
            "sftp" => "SFTP",
            "ftp" => "FTP",
            "http" or "https" => "REST",
            "file" => "FLATFILE",
            _ => scheme.ToUpperInvariant()
        };

        if (detectedProvider is "FLATFILE" or "FILE")
        {
            options["PATH"] = uri.LocalPath;
            return new ParsedConnectionStringResult("FLATFILE", options, null, null);
        }

        if (!string.IsNullOrEmpty(uri.Host))
        {
            options[detectedProvider is "SFTP" or "FTP" or "REST" ? "HOST" : "SERVER"] = uri.Host;
        }

        if (uri.Port > 0)
        {
            options["PORT"] = uri.Port.ToString();
        }

        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath.Length > 1)
        {
            options["DATABASE"] = uri.AbsolutePath.TrimStart('/');
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var userParts = uri.UserInfo.Split(':', 2);
            options["USER"] = Uri.UnescapeDataString(userParts[0]);
            if (userParts.Length > 1)
            {
                extractedCredential = Uri.UnescapeDataString(userParts[1]);
                options["PASSWORD"] = extractedCredential;
            }
        }

        // Query parameters
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var query = uri.Query.TrimStart('?');
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=', 2);
                var k = Uri.UnescapeDataString(kv[0]).ToUpperInvariant();
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "ON";
                options[k] = v;
            }
        }

        string? suggestedKey = null;
        if (!string.IsNullOrEmpty(extractedCredential))
        {
            var db = options.TryGetValue("DATABASE", out var d) ? d : "DB";
            suggestedKey = $"{detectedProvider}_{db.ToUpperInvariant()}_PW";
        }

        return new ParsedConnectionStringResult(detectedProvider, options, extractedCredential, suggestedKey);
    }

    private static List<(string Key, string Value)> SplitKeyValuePairs(string connectionString)
    {
        var result = new List<(string Key, string Value)>();
        var currentKey = new System.Text.StringBuilder();
        var currentValue = new System.Text.StringBuilder();
        bool inKey = true;
        bool inQuotes = false;
        char quoteChar = '\0';

        for (int i = 0; i < connectionString.Length; i++)
        {
            char c = connectionString[i];

            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                }
                else
                {
                    currentValue.Append(c);
                }
            }
            else
            {
                if (c is '\'' or '"' or '{')
                {
                    inQuotes = true;
                    quoteChar = c == '{' ? '}' : c;
                }
                else if (c == '=' && inKey)
                {
                    inKey = false;
                }
                else if (c == ';')
                {
                    if (currentKey.Length > 0)
                    {
                        result.Add((currentKey.ToString().Trim(), currentValue.ToString().Trim()));
                        currentKey.Clear();
                        currentValue.Clear();
                    }
                    inKey = true;
                }
                else
                {
                    if (inKey) currentKey.Append(c);
                    else currentValue.Append(c);
                }
            }
        }

        if (currentKey.Length > 0)
        {
            result.Add((currentKey.ToString().Trim(), currentValue.ToString().Trim()));
        }

        return result;
    }
}
