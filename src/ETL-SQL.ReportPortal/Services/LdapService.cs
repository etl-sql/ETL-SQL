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

        public LdapService(PortalConfig config, ILogger<LdapService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<LdapUserResult?> AuthenticateAsync(string username, string password)
        {
            Console.WriteLine($"[LDAP-DIAG] AuthenticateAsync start for '{username}'");
            if (!_config.Identity.Ldap.Enabled)
                return null;

            var ldapConfig = _config.Identity.Ldap;

            // 1. If ServiceUser is configured, perform a search first to find the user's distinguished name (DN)
            if (!string.IsNullOrEmpty(ldapConfig.ServiceUser))
            {
                _logger.LogDebug("LDAP: Service User configured. Performing user lookup for '{Username}' first.", username);
                SearchResultEntry? userEntry = null;
                var serviceGroups = new List<string>();
                LdapConnection? serviceConn = null;
                try
                {
                    var identifier = new LdapDirectoryIdentifier(ldapConfig.Server, ldapConfig.Port);
                    serviceConn = new LdapConnection(identifier);
                    serviceConn.SessionOptions.ProtocolVersion = 3;

                    if (ldapConfig.UseSsl)
                    {
                        serviceConn.SessionOptions.SecureSocketLayer = true;
                        if (ldapConfig.AllowSelfSignedCertificates)
                        {
                            serviceConn.SessionOptions.VerifyServerCertificate = (conn, cert) => true;
                        }
                    }
                    else
                    {
                        serviceConn.SessionOptions.SecureSocketLayer = false;
                    }

                    var credential = new NetworkCredential(ldapConfig.ServiceUser, ldapConfig.ServicePassword);
                    serviceConn.Credential = credential;
                    serviceConn.AuthType = (ldapConfig.UseSsl || string.IsNullOrEmpty(ldapConfig.Domain) || ldapConfig.ServiceUser.Contains("=") || ldapConfig.ServiceUser.Contains(","))
                        ? AuthType.Basic
                        : AuthType.Negotiate;

                    await Task.Run(() => serviceConn.Bind());

                    // Clean username
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
                    string searchFilter = $"(|(sAMAccountName={escapedUsername})(uid={escapedUsername})(cn={escapedUsername}))";

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
                        "displayName", "mail", "givenName", "sn", "memberOf", "sAMAccountName", "uid", "cn"
                    );

                    var response = (SearchResponse)await Task.Run(() => serviceConn.SendRequest(request));
                    if (response.Entries.Count > 0)
                    {
                        userEntry = response.Entries[0];
                        
                        // Query groups using service connection
                        bool hasMemberOf = false;
                        foreach (string name in userEntry.Attributes.AttributeNames)
                        {
                            if (string.Equals(name, "memberOf", StringComparison.OrdinalIgnoreCase))
                            {
                                hasMemberOf = true;
                                var memberOfAttr = userEntry.Attributes[name];
                                foreach (var val in memberOfAttr)
                                {
                                    string dn = val is byte[] valBytes ? System.Text.Encoding.UTF8.GetString(valBytes) : (val.ToString() ?? "");
                                    if (!string.IsNullOrEmpty(dn))
                                    {
                                        serviceGroups.Add(dn);
                                    }
                                }
                                break;
                            }
                        }
                        
                        if (!hasMemberOf)
                        {
                            try
                            {
                                var groupRequest = new SearchRequest(
                                    baseDn,
                                    $"(|(member={userEntry.DistinguishedName})(uniqueMember={userEntry.DistinguishedName}))",
                                    SearchScope.Subtree,
                                    "cn"
                                );
                                try { System.IO.File.AppendAllText("C:\\Users\\chuck\\scratch\\ETL-SQL\\ldap_debug.txt", $"Group Query Filter: {groupRequest.Filter}\nBase DN: {baseDn}\n"); } catch {}
                                var groupResponse = (SearchResponse)await Task.Run(() => serviceConn.SendRequest(groupRequest));
                                try { System.IO.File.AppendAllText("C:\\Users\\chuck\\scratch\\ETL-SQL\\ldap_debug.txt", $"Found Group Count: {groupResponse.Entries.Count}\n"); } catch {}
                                foreach (SearchResultEntry grp in groupResponse.Entries)
                                {
                                    try { System.IO.File.AppendAllText("C:\\Users\\chuck\\scratch\\ETL-SQL\\ldap_debug.txt", $"Found Group DN: {grp.DistinguishedName}\n"); } catch {}
                                    serviceGroups.Add(grp.DistinguishedName);
                                }
                            }
                            catch (Exception ex)
                            {
                                try { System.IO.File.AppendAllText("C:\\Users\\chuck\\scratch\\ETL-SQL\\ldap_debug.txt", $"Group Query Error: {ex}\n"); } catch {}
                                _logger.LogDebug(ex, "Failed to perform group membership query fallback using Service User.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LDAP-DIAG] Service user lookup failed: {ex}");
                    _logger.LogWarning(ex, "LDAP Service User lookup failed for user '{Username}'.", username);
                }
                finally
                {
                    serviceConn?.Dispose();
                }

                if (userEntry == null)
                {
                    Console.WriteLine($"[LDAP-DIAG] User entry is null after service lookup.");
                    _logger.LogWarning("LDAP: User '{Username}' not found during Service User lookup.", username);
                    return null;
                }

                // Attempt to bind with the discovered user DN and their password
                Console.WriteLine($"[LDAP-DIAG] User DN found: '{userEntry.DistinguishedName}'. Starting user credentials bind...");
                try { System.IO.File.WriteAllText("C:\\Users\\chuck\\scratch\\ETL-SQL\\ldap_debug.txt", $"User DN: {userEntry.DistinguishedName}\n"); } catch {}
                LdapConnection? userConn = null;
                try
                {
                    var identifier = new LdapDirectoryIdentifier(ldapConfig.Server, ldapConfig.Port);
                    userConn = new LdapConnection(identifier);
                    userConn.SessionOptions.ProtocolVersion = 3;

                    if (ldapConfig.UseSsl)
                    {
                        userConn.SessionOptions.SecureSocketLayer = true;
                        if (ldapConfig.AllowSelfSignedCertificates)
                        {
                            userConn.SessionOptions.VerifyServerCertificate = (conn, cert) => true;
                        }
                    }
                    else
                    {
                        userConn.SessionOptions.SecureSocketLayer = false;
                    }

                    var credential = new NetworkCredential(userEntry.DistinguishedName, password);
                    userConn.Credential = credential;
                    userConn.AuthType = AuthType.Basic; // Always use simple bind (Basic) when binding directly with a DN

                    await Task.Run(() => userConn.Bind());

                    // Bind succeeded! Build the result from the entry retrieved earlier.
                    string cleanUsername = username;
                    if (username.Contains("@"))
                    {
                        cleanUsername = username.Split('@')[0];
                    }
                    else if (username.Contains("\\"))
                    {
                        cleanUsername = username.Split('\\')[1];
                    }

                    var result = new LdapUserResult
                    {
                        Username = GetAttributeValue(userEntry, "sAMAccountName") ?? GetAttributeValue(userEntry, "uid") ?? GetAttributeValue(userEntry, "cn") ?? cleanUsername,
                        Email = GetAttributeValue(userEntry, "mail"),
                        DisplayName = GetAttributeValue(userEntry, "displayName") ?? GetAttributeValue(userEntry, "cn"),
                        FirstName = GetAttributeValue(userEntry, "givenName"),
                        LastName = GetAttributeValue(userEntry, "sn")
                    };

                    result.Groups.AddRange(serviceGroups);

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LDAP-DIAG] User credentials bind failed: {ex}");
                    _logger.LogWarning(ex, "LDAP authentication credentials bind failed for user DN '{DistinguishedName}'.", userEntry.DistinguishedName);
                    return null;
                }
                finally
                {
                    userConn?.Dispose();
                }
            }

            // 2. Direct Bind Fallback (Active Directory style or direct DN input style)
            Console.WriteLine($"[LDAP-DIAG] Falling back to direct bind.");
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
                connection.AuthType = (ldapConfig.UseSsl || string.IsNullOrEmpty(ldapConfig.Domain) || bindUsername.Contains("=")) 
                    ? AuthType.Basic 
                    : AuthType.Negotiate;

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
                string searchFilter = $"(|(sAMAccountName={escapedUsername})(uid={escapedUsername})(cn={escapedUsername}))";
                
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
                    "displayName", "mail", "givenName", "sn", "memberOf", "sAMAccountName", "uid", "cn"
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
                    Username = GetAttributeValue(entry, "sAMAccountName") ?? GetAttributeValue(entry, "uid") ?? GetAttributeValue(entry, "cn") ?? cleanUsername,
                    Email = GetAttributeValue(entry, "mail"),
                    DisplayName = GetAttributeValue(entry, "displayName") ?? GetAttributeValue(entry, "cn"),
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
                else
                {
                    try
                    {
                        var groupRequest = new SearchRequest(
                            baseDn,
                            $"(|(member={entry.DistinguishedName})(uniqueMember={entry.DistinguishedName}))",
                            SearchScope.Subtree,
                            "cn"
                        );
                        var groupResponse = (SearchResponse)await Task.Run(() => connection.SendRequest(groupRequest));
                        foreach (SearchResultEntry grp in groupResponse.Entries)
                        {
                            result.Groups.Add(grp.DistinguishedName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to perform group membership query fallback.");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LDAP-DIAG] Direct bind failed: {ex}");
                _logger.LogWarning(ex, "LDAP direct authentication failed for user '{Username}' due to connection, bind, or search error.", username);
                return null;
            }
            finally
            {
                connection?.Dispose();
            }
        }

        private static string? GetAttributeValue(SearchResultEntry entry, string attributeName)
        {
            foreach (string name in entry.Attributes.AttributeNames)
            {
                if (string.Equals(name, attributeName, StringComparison.OrdinalIgnoreCase))
                {
                    var attr = entry.Attributes[name];
                    if (attr.Count > 0)
                    {
                        var val = attr[0];
                        if (val is byte[] valBytes)
                        {
                            return System.Text.Encoding.UTF8.GetString(valBytes);
                        }
                        if (val is string valStr)
                        {
                            return valStr;
                        }
                        return val?.ToString();
                    }
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
