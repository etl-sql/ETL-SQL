using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Shared;

namespace ETL_SQL.Connectors
{
    public class ActiveDirectoryConnector : IDataSource, IConnector
    {
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        
        private readonly string _host = "localhost";
        private readonly int _port = 389;
        private readonly bool _useSsl = false;
        private readonly string _authMode = "INTEGRATED";
        private readonly string _user = "";
        private readonly string _password = "";
        private readonly string _domain = "";
        private readonly string _baseDn = "";
        private readonly string _filterContext = "users";
        private readonly string _customFilter = "";
        private readonly List<string> _attributesToQuery = new();
        private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DefaultAttributes = new[]
        {
            "sAMAccountName", "displayName", "mail", "userPrincipalName", 
            "distinguishedName", "memberOf", "whenCreated", "objectGUID", "objectSid"
        };

        public string Name => "ACTIVE_DIRECTORY";
        public IReadOnlyList<string> Aliases => new[] { "AD", "LDAP" };
        public string Path => $"ldap://{_host}:{_port}/{_baseDn}";
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "ACTIVE_DIRECTORY";

        public ActiveDirectoryConnector()
        {
            _logger = NullLogger.Instance;
        }

        public ActiveDirectoryConnector(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            _context = context;
            _logger = context.Logger;

            if (options != null)
            {
                foreach (var kv in options)
                {
                    _options[kv.Key] = kv.Value;
                }
            }

            // Parse connection string or HOST option
            if (!string.IsNullOrEmpty(connectionString))
            {
                if (connectionString.StartsWith("ldap://", StringComparison.OrdinalIgnoreCase) || 
                    connectionString.StartsWith("ldaps://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(connectionString);
                    _host = uri.Host;
                    _port = uri.Port > 0 ? uri.Port : (uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase) ? 636 : 389);
                    _useSsl = uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase);
                    _baseDn = uri.AbsolutePath.TrimStart('/');
                }
                else
                {
                    _host = connectionString;
                }
            }

            _host = _options.GetValueOrDefault("HOST", _host);
            if (_options.TryGetValue("PORT", out var portStr) && int.TryParse(portStr, out var port))
            {
                _port = port;
            }
            _useSsl = _options.GetValueOrDefault("USE_SSL", _useSsl.ToString()).Equals("TRUE", StringComparison.OrdinalIgnoreCase) || 
                      _options.GetValueOrDefault("LDAPS", "FALSE").Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            
            _authMode = _options.GetValueOrDefault("AUTH_MODE", "INTEGRATED").ToUpperInvariant();
            _user = _options.GetValueOrDefault("USER", "");
            _password = _options.GetValueOrDefault("PASSWORD", "");
            _domain = _options.GetValueOrDefault("DOMAIN", "");
            _baseDn = _options.GetValueOrDefault("BASE_DN", _baseDn);
            _filterContext = _options.GetValueOrDefault("FILTER_CONTEXT", "users");
            _customFilter = _options.GetValueOrDefault("FILTER", "");

            if (_options.TryGetValue("ATTRIBUTES", out var attribs))
            {
                _attributesToQuery = attribs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
            else
            {
                _attributesToQuery = DefaultAttributes.ToList();
            }

            // Security Hardening: Validate host against egress rules
            if (!string.IsNullOrEmpty(_host))
            {
                context.SecurityService.ValidateHost(_host);
            }
        }

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            if (!string.IsNullOrEmpty(_host))
            {
                context.SecurityService.ValidateHost(_host);
            }

            try
            {
                using var connection = GetConnection();
                connection.Bind();

                var request = new SearchRequest(
                    "",
                    "(objectClass=*)",
                    SearchScope.Base,
                    "dnsHostName", "supportedLDAPVersion"
                );

                var response = (SearchResponse)connection.SendRequest(request);
                string dnsName = _host;
                string ldapVer = "3";

                if (response.Entries.Count > 0)
                {
                    var entry = response.Entries[0];
                    var hostAttr = entry.Attributes["dnsHostName"];
                    if (hostAttr != null && hostAttr.Count > 0)
                    {
                        dnsName = hostAttr[0]?.ToString() ?? dnsName;
                    }
                    var verAttr = entry.Attributes["supportedLDAPVersion"];
                    if (verAttr != null && verAttr.Count > 0)
                    {
                        ldapVer = string.Join(",", verAttr.Cast<object>().Select(o => o.ToString()));
                    }
                }

                return $"Active Directory / LDAP Connector v1.0 (Connected - Host: {dnsName}, Supported LDAP Versions: {ldapVer})";
            }
            catch (Exception ex)
            {
                throw ConnectorExceptionWrapper.Wrap("Active Directory", ex);
            }
        }

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HOST", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "USE_SSL", new[] { "TRUE", "FALSE" } },
            { "LDAPS", new[] { "TRUE", "FALSE" } },
            { "AUTH_MODE", new[] { "SIMPLE", "INTEGRATED" } },
            { "USER", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "DOMAIN", Array.Empty<string>() },
            { "BASE_DN", Array.Empty<string>() },
            { "FILTER", Array.Empty<string>() },
            { "ATTRIBUTES", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "USE_SSL", new[] { "TRUE", "FALSE" } },
            { "AUTH_MODE", new[] { "SIMPLE", "INTEGRATED" } }
        };

        public string GetHelp() =>
            "ACTIVE_DIRECTORY Connector: Queries user, group, and machine details from Active Directory via LDAP.\n" +
            "Supports: SELECT queries against virtual tables (users, groups, computers, contacts).\n\n" +
            "Options:\n" +
            "  HOST: Server hostname or domain controller (e.g. 'corp.company.com').\n" +
            "  PORT: LDAP port (default: 389, LDAPS: 636).\n" +
            "  USE_SSL / LDAPS: Set to TRUE to enable secure LDAPS connection.\n" +
            "  AUTH_MODE: SIMPLE (username/password bind) or INTEGRATED (uses process environment identity).\n" +
            "  USER: AD Username (e.g., 'DOMAIN\\svc-account' or full UPN/DN).\n" +
            "  PASSWORD: AD Password (use ENC: prefix for safety).\n" +
            "  DOMAIN: Active Directory domain name.\n" +
            "  BASE_DN: Search root DN (e.g., 'DC=corp,DC=company,DC=com').\n" +
            "  FILTER: Custom search filter (overrides virtual table context).\n" +
            "  ATTRIBUTES: Comma-separated list of attributes to query.";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            return new ActiveDirectoryConnector(context, connectionString, options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => 
            Task.FromResult<IEnumerable<string>>(new[] { "users", "groups", "computers", "contacts" });

        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => 
            Task.FromResult<IEnumerable<string>>(DefaultAttributes);
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            string protocol = properties.GetValueOrDefault("USE_SSL", "FALSE").Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? "ldaps" : "ldap";
            string host = properties.GetValueOrDefault("HOST", "localhost");
            string port = properties.ContainsKey("PORT") ? $":{properties["PORT"]}" : "";
            string baseDn = properties.ContainsKey("BASE_DN") ? $"/{properties["BASE_DN"]}" : "";
            return $"{protocol}://{host}{port}{baseDn}";
        }

        private LdapConnection GetConnection()
        {
            var identifier = new LdapDirectoryIdentifier(_host, _port);
            var connection = new LdapConnection(identifier);

            if (_useSsl)
            {
                connection.SessionOptions.SecureSocketLayer = true;
            }

            // Version 3 is the standard LDAP protocol version
            connection.SessionOptions.ProtocolVersion = 3;

            NetworkCredential? credentials = null;
            AuthType authType = AuthType.Negotiate;

            if (_authMode == "SIMPLE")
            {
                string pass = _password;
                if (pass.StartsWith("ENC:") && _context != null)
                {
                    pass = _context.DecryptValue(pass) ?? "";
                }
                
                credentials = new NetworkCredential(_user, pass, _domain);
                authType = AuthType.Basic;
            }

            connection.Credential = credentials;
            connection.AuthType = authType;

            return connection;
        }

        internal string ResolveLdapFilter()
        {
            if (!string.IsNullOrEmpty(_customFilter))
            {
                return _customFilter;
            }

            return _filterContext.ToLowerInvariant() switch
            {
                "users" or "user" => "(|(&(objectCategory=person)(objectClass=user))(objectClass=inetOrgPerson))",
                "groups" or "group" => "(|(objectClass=group)(objectClass=groupOfNames)(objectClass=groupOfUniqueNames))",
                "computers" or "computer" => "(objectClass=computer)",
                "contacts" or "contact" => "(objectClass=contact)",
                _ => $"(objectClass={_filterContext})"
            };
        }

        // ── IDataSource Implementation ────────────────────────────────────────────

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            string ldapFilter = ResolveLdapFilter();
            _logger.Debug("Executing Active Directory LDAP Search. BaseDN: {BaseDN}, Filter: {Filter}", _baseDn, ldapFilter);

            using var connection = GetConnection();
            connection.Bind();

            byte[]? cookie = null;
            var table = new DataTable();
            table.SetColumns(_attributesToQuery);

            do
            {
                var request = new SearchRequest(
                    _baseDn,
                    ldapFilter,
                    SearchScope.Subtree,
                    _attributesToQuery.ToArray()
                );

                // Add search page request control to support pagination
                var pageControl = new PageResultRequestControl(Math.Min(batchSize, 1000))
                {
                    Cookie = cookie
                };
                request.Controls.Add(pageControl);

                var response = (SearchResponse)connection.SendRequest(request);
                
                // Get page response cookie to verify if more records exist
                var pageResponse = (PageResultResponseControl?)response.Controls
                    .FirstOrDefault(c => c is PageResultResponseControl);
                cookie = pageResponse?.Cookie;

                foreach (SearchResultEntry entry in response.Entries)
                {
                    var row = table.NewRow();
                    foreach (var attr in _attributesToQuery)
                    {
                        var attrib = entry.Attributes[attr];
                        if (attrib != null && attrib.Count > 0)
                        {
                            if (attrib.Count == 1)
                            {
                                // Single value property
                                var val = attrib[0];
                                row[attr] = val is byte[] bytes ? ConvertLdapValue(bytes, attr) : val?.ToString() ?? "";
                            }
                            else
                            {
                                // Multi-value property (e.g. memberOf) - Serialize to JSON Array
                                var valuesList = new List<string>();
                                foreach (var val in attrib)
                                {
                                    valuesList.Add(val is byte[] bytes ? ConvertLdapValue(bytes, attr) : val?.ToString() ?? "");
                                }
                                row[attr] = JsonSerializer.Serialize(valuesList);
                            }
                        }
                        else
                        {
                            row[attr] = "";
                        }
                    }
                    await table.AddRowAsync(row);

                    if (table.Rows.Count >= batchSize)
                    {
                        yield return table;
                        table = table.Clone();
                    }
                }

                await Task.Yield(); // Keep UI thread responsive during sync LDAP query pagination
            } 
            while (cookie != null && cookie.Length > 0);

            if (table.Rows.Count > 0)
            {
                yield return table;
            }
        }

        private static string ConvertLdapValue(byte[] bytes, string attributeName)
        {
            if (attributeName.Equals("objectGUID", StringComparison.OrdinalIgnoreCase))
            {
                return new Guid(bytes).ToString();
            }
            if (attributeName.Equals("objectSid", StringComparison.OrdinalIgnoreCase))
            {
                // Simple representation, full SecurityIdentifier parse can be added if needed
                return "S-" + string.Join("-", bytes.Select(b => b.ToString()));
            }
            return Encoding.UTF8.GetString(bytes);
        }

        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            throw new NotSupportedException("Active Directory / LDAP queries are read-only. WriteBatches is not supported.");
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(_attributesToQuery);
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName)
        {
            var options = new Dictionary<string, string>(_options, StringComparer.OrdinalIgnoreCase);
            options["FILTER_CONTEXT"] = tableName;
            return new ActiveDirectoryConnector(_context!, _host, options);
        }

        public async Task<IEnumerable<string>> GetTablesAsync() => await GetTablesAsync(null!, null!);

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
