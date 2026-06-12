using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ETL_SQL.ReportPortal.Tests
{
    [Trait("Category", "Portal")]
    public class LdapAuthTests : IClassFixture<LdapAuthTests.LdapPortalWebFactory>
    {
        private readonly HttpClient _client;
        private readonly LdapPortalWebFactory _factory;

        public LdapAuthTests(LdapPortalWebFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public class LdapPortalWebFactory : PortalWebFactory
        {
            public MockLdapService MockLdap { get; } = new();

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ILdapService>();
                    services.AddSingleton<ILdapService>(MockLdap);

                    // Re-register PortalConfig with LDAP enabled
                    services.RemoveAll<PortalConfig>();

                    var dbPath = System.IO.Path.Combine(TempDir, "portal.db");
                    var scriptRoot = System.IO.Path.Combine(TempDir, "scripts");
                    var snapshotDir = System.IO.Path.Combine(TempDir, "snapshots");
                    var mapRoot = System.IO.Path.Combine(TempDir, "maps");
                    var datasetRoot = System.IO.Path.Combine(TempDir, "datasets");
                    var orchDbPath = System.IO.Path.Combine(TempDir, "etlsql.db");
                    const string jwtSecret = "integration-test-secret-key-1234567890";

                    var cfg = new PortalConfig
                    {
                        DatabasePath = dbPath,
                        ScriptRootPath = scriptRoot,
                        SnapshotDirectory = snapshotDir,
                        MapRootPath = mapRoot,
                        DatasetRootPath = datasetRoot,
                        Jwt = new JwtConfig { Secret = jwtSecret, ExpiryMinutes = 60, RefreshExpiryDays = 7 },
                        FirstRun = new FirstRunConfig { AdminUsername = "admin", AdminPassword = "Admin@12345!" },
                        Orchestrator = new OrchestratorConfig { DatabasePath = orchDbPath },
                        Identity = new IdentityConfig
                        {
                            Provider = "Local",
                            Ldap = new LdapIdentityConfig
                            {
                                Enabled = true,
                                Server = "localhost",
                                Port = 389,
                                UseSsl = false,
                                Domain = "corp.local",
                                BaseDn = "DC=corp,DC=local",
                                RoleMappings = new Dictionary<string, string>
                                {
                                    { "CN=GG-Portal-Admins,OU=Groups,DC=corp,DC=local", "Admin" },
                                    { "GG-Portal-Publishers", "Publisher" }
                                }
                            }
                        }
                    };
                    services.AddSingleton(cfg);
                });
            }
        }

        public class MockLdapService : ILdapService
        {
            public Func<string, string, LdapUserResult?> AuthenticateFunc { get; set; } = (user, pass) => null;

            public Task<LdapUserResult?> AuthenticateAsync(string username, string password)
            {
                return Task.FromResult(AuthenticateFunc(username, password));
            }
        }

        private static string? _adminToken;
        private static readonly SemaphoreSlim _tokenLock = new(1, 1);

        private async Task<string> GetAdminTokenAsync()
        {
            await _tokenLock.WaitAsync();
            try
            {
                if (_adminToken is not null) return _adminToken;

                var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
                {
                    username = "admin",
                    password = "Admin@12345!"
                });
                loginRes.EnsureSuccessStatusCode();
                var resp = await loginRes.Content.ReadFromJsonAsync<LoginResponse>();
                var token = resp!.Token;

                using var cpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
                cpReq.Headers.Authorization = new("Bearer", token);
                cpReq.Content = JsonContent.Create(new
                {
                    currentPassword = "Admin@12345!",
                    newPassword = "Admin@Tests99!"
                });
                (await _client.SendAsync(cpReq)).EnsureSuccessStatusCode();

                var reloginRes = await _client.PostAsJsonAsync("/api/auth/login", new
                {
                    username = "admin",
                    password = "Admin@Tests99!"
                });
                reloginRes.EnsureSuccessStatusCode();
                var reloginResp = await reloginRes.Content.ReadFromJsonAsync<LoginResponse>();
                _adminToken = reloginResp!.Token;

                return _adminToken;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        [Fact]
        public async Task Login_InvalidLdapCredentials_ReturnsUnauthorized()
        {
            _factory.MockLdap.AuthenticateFunc = (u, p) => null;

            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("invalid@corp.local", "wrongpass"));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            var content = await res.Content.ReadAsStringAsync();
            Assert.Contains("Invalid LDAP credentials", content);
        }

        [Fact]
        public async Task Login_ValidLdapCredentials_AutoProvisionsUserAndRolesAndGroups()
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var username = $"ldapuser_{suffix}";
            var email = $"{username}@corp.local";

            // Setup mock LDAP return details
            _factory.MockLdap.AuthenticateFunc = (u, p) => new LdapUserResult
            {
                Username = username,
                Email = email,
                FirstName = "Ldap",
                LastName = "User",
                Groups = new List<string>
                {
                    "CN=GG-Portal-Admins,OU=Groups,DC=corp,DC=local",
                    "GG-Portal-Publishers",
                    "GG-Other-Unmapped-Group"
                }
            };

            // Seed some LDAP portal groups in database
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

                // Mapped group via AdGroup
                db.Groups.Add(new Group
                {
                    Name = "Portal Admins",
                    Description = "Portal Admins LDAP Mapped",
                    Provider = "LDAP",
                    AdGroup = "CN=GG-Portal-Admins,OU=Groups,DC=corp,DC=local"
                });

                // Mapped group via matching Name to CN
                db.Groups.Add(new Group
                {
                    Name = "GG-Portal-Publishers",
                    Description = "Portal Publishers LDAP Mapped by Name",
                    Provider = "LDAP",
                    AdGroup = null
                });

                // Unmapped group
                db.Groups.Add(new Group
                {
                    Name = "Local-Analysts",
                    Description = "Local manual group",
                    Provider = "Local",
                    AdGroup = null
                });

                await db.SaveChangesAsync();
            }

            // 1. Perform login
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"{username}@corp.local", "ldapPass123!"));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var loginResp = await res.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginResp!.Token);

            // 2. Validate DB state for the user
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>();

                var user = await db.Users
                    .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                    .FirstOrDefaultAsync(u => u.UserName == username);

                Assert.NotNull(user);
                Assert.Equal("LDAP", user.Provider);
                Assert.Equal(email, user.Email);
                Assert.Equal("Ldap", user.FirstName);
                Assert.Equal("User", user.LastName);

                // Check Role assignment based on RoleMappings ("CN=GG-Portal-Admins..." -> "Admin", "GG-Portal-Publishers" -> "Publisher")
                var roles = await userManager.GetRolesAsync(user);
                Assert.Contains("Admin", roles);
                Assert.Contains("Publisher", roles);

                // Check Group synchronization
                var groupNames = user.UserGroups.Select(ug => ug.Group.Name).ToList();
                Assert.Contains("Portal Admins", groupNames);
                Assert.Contains("GG-Portal-Publishers", groupNames);
                Assert.DoesNotContain("Local-Analysts", groupNames);

                // Add user to local group manually
                var localGroup = await db.Groups.FirstAsync(g => g.Name == "Local-Analysts");
                db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = localGroup.Id });
                await db.SaveChangesAsync();
            }

            // 3. Login again, but simulate user removed from one AD group ("GG-Portal-Publishers")
            _factory.MockLdap.AuthenticateFunc = (u, p) => new LdapUserResult
            {
                Username = username,
                Email = email,
                FirstName = "Ldap",
                LastName = "User",
                Groups = new List<string>
                {
                    "CN=GG-Portal-Admins,OU=Groups,DC=corp,DC=local"
                }
            };

            var res2 = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "ldapPass123!"));
            Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

            // Verify they are removed from "GG-Portal-Publishers" group and role "Publisher", but remain in local group and "Portal Admins"
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>();

                var user = await db.Users
                    .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                    .FirstOrDefaultAsync(u => u.UserName == username);

                Assert.NotNull(user);
                var roles = await userManager.GetRolesAsync(user);
                Assert.Contains("Admin", roles);
                Assert.DoesNotContain("Publisher", roles);

                var groupNames = user.UserGroups.Select(ug => ug.Group.Name).ToList();
                Assert.Contains("Portal Admins", groupNames);
                Assert.DoesNotContain("GG-Portal-Publishers", groupNames);
                Assert.Contains("Local-Analysts", groupNames); // Local group preserved!
            }
        }

        [Fact]
        public async Task Login_DisabledLdapUser_IsRejectedAndStaysDisabled()
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var username = $"ldapoff_{suffix}";

            _factory.MockLdap.AuthenticateFunc = (u, p) => new LdapUserResult
            {
                Username = username,
                Email = $"{username}@corp.local",
                FirstName = "Ldap",
                LastName = "Disabled",
                Groups = new List<string>()
            };

            // Provision the account via a first successful LDAP login.
            var first = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "ldapPass123!"));
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            // An administrator disables the account.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var user = await db.Users.SingleAsync(u => u.UserName == username);
                user.IsActive = false;
                await db.SaveChangesAsync();
            }

            // Valid LDAP credentials must not resurrect a portal-disabled account.
            var second = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "ldapPass123!"));
            Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var user = await db.Users.SingleAsync(u => u.UserName == username);
                Assert.False(user.IsActive);
            }
        }

        [Fact]
        public async Task ChangePassword_LdapUser_ReturnsBadRequest()
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var username = $"ldappw_{suffix}";

            _factory.MockLdap.AuthenticateFunc = (u, p) => new LdapUserResult
            {
                Username = username,
                Email = $"{username}@corp.local",
                FirstName = "Ldap",
                LastName = "Pw",
                Groups = new List<string> { "GG-Portal-Publishers" }
            };

            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "pass"));
            var loginResp = await res.Content.ReadFromJsonAsync<LoginResponse>();

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
            {
                Content = JsonContent.Create(new ChangePasswordRequest("pass", "NewPass@123!"))
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResp!.Token);

            var changeRes = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, changeRes.StatusCode);
            var content = await changeRes.Content.ReadAsStringAsync();
            Assert.Contains("Password changes are not supported for LDAP accounts", content);
        }

        [Fact]
        public async Task AdminController_CreateLdapUserAndGroup_Succeeds()
        {
            var token = await GetAdminTokenAsync();
            var suffix = Guid.NewGuid().ToString("N")[..8];

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
            {
                Content = JsonContent.Create(new
                {
                    username = $"new_ldap_{suffix}",
                    email = $"new_ldap_{suffix}@corp.local",
                    role = "Publisher",
                    provider = "LDAP"
                })
            };
            reqMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var res = await _client.SendAsync(reqMessage);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);

            // Create Group via Admin controller
            var grpMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/groups")
            {
                Content = JsonContent.Create(new
                {
                    name = $"New_Ldap_Grp_{suffix}",
                    description = "LDAP mapped group",
                    provider = "LDAP",
                    adGroup = $"CN=New_Ldap_Grp_{suffix},OU=Groups,DC=corp,DC=local"
                })
            };
            grpMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var grpRes = await _client.SendAsync(grpMessage);
            Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);

            // Verify in DB
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

                var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == $"new_ldap_{suffix}");
                Assert.NotNull(user);
                Assert.Equal("LDAP", user.Provider);

                var group = await db.Groups.FirstOrDefaultAsync(g => g.Name == $"New_Ldap_Grp_{suffix}");
                Assert.NotNull(group);
                Assert.Equal("LDAP", group.Provider);
                Assert.Equal($"CN=New_Ldap_Grp_{suffix},OU=Groups,DC=corp,DC=local", group.AdGroup);
            }
        }
    }
}
