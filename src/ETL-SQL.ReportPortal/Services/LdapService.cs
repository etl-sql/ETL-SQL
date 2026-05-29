using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ETL_SQL.ReportPortal.Services
{
    public class LdapService : ILdapService
    {
        private readonly PortalConfig _config;
        private readonly ILogger<LdapService> _logger;

        public LdapService(IOptions<PortalConfig> config, ILogger<LdapService> logger)
        {
            _config = config.Value;
            _logger = logger;
        }

        public async Task<LdapUserResult?> AuthenticateAsync(string username, string password)
        {
            if (!_config.Identity.Ldap.Enabled)
                return null;

            var ldapConfig = _config.Identity.Ldap;

            // Formulate bind username
            string bindUsername = username;
            if (!string.IsNullOrEmpty(ldapConfig.Domain))
            {
                if (!username.Contains("@") && !username.Contains("\\"))
                {
                    bindUsername = $"{username}@{ldapConfig.Domain}";
                }
            }

            LdapConnection? connection = null;
            try
            {
                var identifier = new LdapDirectoryIdentifier(ldapConfig.Server, ldapConfig.Port);
                connection = new LdapConnection(identifier);
                connection.SessionOptions.ProtocolVersion = 3;

                if (ldapConfig.UseSsl)
                {
                    connection.SessionOptions.SecureSocketLayer = true;
                    if (ldapConfig.AllowSelfSignedCertificates)
                    {
                        connection.SessionOptions.VerifyServerCertificate = (conn, cert) => true;
                    }
                }
                else
                {
                    connection.SessionOptions.SecureSocketLayer = false;
                }

                // Bind using credentials
                var credential = new NetworkCredential(bindUsername, password);
                connection.Credential = credential;
                
                // Active Directory supports both Basic (over SSL) and Negotiate (default for Windows environments).
                connection.AuthType = ldapConfig.UseSsl ? AuthType.Basic : AuthType.Negotiate;

                await Task.Run(() => connection.Bind());

                // Find user details
                string cleanUsername = username;
                if (username.Contains("@"))
                {
                    cleanUsername = username.Split('@')[0];
                }
                else if (username.Contains("\\"))
                {
                    cleanUsername = username.Split('\\')[1];
                }

                string escapedUsername = EscapeLdapFilter(cleanUsername);
                string searchFilter = $"(&(objectClass=user)(sAMAccountName={escapedUsername}))";
                
                string baseDn = ldapConfig.BaseDn;
                if (string.IsNullOrEmpty(baseDn) && !string.IsNullOrEmpty(ldapConfig.Domain))
                {
                    var parts = ldapConfig.Domain.Split('.');
                    baseDn = string.Join(",", Array.ConvertAll(parts, p => $"DC={p}"));
                }

                var request = new SearchRequest(
                    baseDn,
                    searchFilter,
                    SearchScope.Subtree,
                    "displayName", "mail", "givenName", "sn", "memberOf", "sAMAccountName"
                );

                var response = (SearchResponse)await Task.Run(() => connection.SendRequest(request));

                if (response.Entries.Count == 0)
                {
                    // Fallback if search didn't return entry (e.g. read permissions, but bind succeeded)
                    return new LdapUserResult
                    {
                        Username = cleanUsername,
                        Email = username.Contains("@") ? username : null,
                        DisplayName = cleanUsername
                    };
                }

                var entry = response.Entries[0];
                var result = new LdapUserResult
                {
                    Username = GetAttributeValue(entry, "sAMAccountName") ?? cleanUsername,
                    Email = GetAttributeValue(entry, "mail"),
                    DisplayName = GetAttributeValue(entry, "displayName"),
                    FirstName = GetAttributeValue(entry, "givenName"),
                    LastName = GetAttributeValue(entry, "sn")
                };

                if (entry.Attributes.Contains("memberOf"))
                {
                    var memberOfAttr = entry.Attributes["memberOf"];
                    foreach (byte[] valBytes in memberOfAttr)
                    {
                        string dn = System.Text.Encoding.UTF8.GetString(valBytes);
                        result.Groups.Add(dn);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LDAP authentication failed for user '{Username}' due to connection, bind, or search error.", username);
                return null;
            }
            finally
            {
                connection?.Dispose();
            }
        }

        private static string? GetAttributeValue(SearchResultEntry entry, string attributeName)
        {
            if (entry.Attributes.Contains(attributeName))
            {
                var attr = entry.Attributes[attributeName];
                if (attr.Count > 0 && attr[0] is byte[] valBytes)
                {
                    return System.Text.Encoding.UTF8.GetString(valBytes);
                }
            }
            return null;
        }

        private static string EscapeLdapFilter(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\5c"); break;
                    case '*':  sb.Append("\\2a"); break;
                    case '(':  sb.Append("\\28"); break;
                    case ')':  sb.Append("\\29"); break;
                    case '\0': sb.Append("\\00"); break;
                    default:   sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
