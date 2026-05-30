using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using ETL_SQL.ReportPortal.Models;

namespace ETL_SQL.ReportPortal.Tests
{
    [Trait("Category", "Portal")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class PortalLdapIntegrationTests : IClassFixture<PortalLdapIntegrationTests.OpenLdapPortalWebFactory>
    {
        private readonly HttpClient _client;
        private readonly OpenLdapPortalWebFactory _factory;

        public PortalLdapIntegrationTests(OpenLdapPortalWebFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        public class OpenLdapPortalWebFactory : PortalWebFactory, IAsyncLifetime
        {
            private IContainer? _ldapContainer;

            public int LdapPort { get; private set; }

            public async Task InitializeAsync()
            {
                // 1. Start the container
                _ldapContainer = new ContainerBuilder("osixia/openldap:1.5.0")
                    .WithPortBinding(389, true)
                    .WithEnvironment("LDAP_ORGANISATION", "ETL-SQL")
                    .WithEnvironment("LDAP_DOMAIN", "etl-sql.org")
                    .WithEnvironment("LDAP_ADMIN_PASSWORD", "adminpassword")
                    .WithWaitStrategy(Wait.ForUnixContainer()
                        .UntilInternalTcpPortIsAvailable(389))
                    .Build();

                await _ldapContainer.StartAsync();
                LdapPort = _ldapContainer.GetMappedPublicPort(389);

                // Wait a moment for LDAP to prepare
                await Task.Delay(2000);

                // 2. Seed LDAP entries using standard System.DirectoryServices.Protocols
                var identifier = new System.DirectoryServices.Protocols.LdapDirectoryIdentifier("127.0.0.1", LdapPort);
                using var conn = new System.DirectoryServices.Protocols.LdapConnection(identifier)
                {
                    Credential = new NetworkCredential("cn=admin,dc=etl-sql,dc=org", "adminpassword"),
                    AuthType = System.DirectoryServices.Protocols.AuthType.Basic
                };
                conn.SessionOptions.ProtocolVersion = 3;
                conn.Bind();

                try
                {
                    // Create OU=users
                    conn.SendRequest(new System.DirectoryServices.Protocols.AddRequest(
                        "ou=users,dc=etl-sql,dc=org",
                        new System.DirectoryServices.Protocols.DirectoryAttribute[]
                        {
                            new("objectClass", "organizationalUnit"),
                            new("ou", "users")
                        }
                    ));

                    // Create OU=groups
                    conn.SendRequest(new System.DirectoryServices.Protocols.AddRequest(
                        "ou=groups,dc=etl-sql,dc=org",
                        new System.DirectoryServices.Protocols.DirectoryAttribute[]
                        {
                            new("objectClass", "organizationalUnit"),
                            new("ou", "groups")
                        }
                    ));

                    // Create test user 'john'
                    conn.SendRequest(new System.DirectoryServices.Protocols.AddRequest(
                        "cn=john,ou=users,dc=etl-sql,dc=org",
                        new System.DirectoryServices.Protocols.DirectoryAttribute[]
                        {
                            new("objectClass", new[] { "inetOrgPerson", "organizationalPerson", "person" }),
                            new("cn", "john"),
                            new("sn", "Doe"),
                            new("givenName", "John"),
                            new("displayName", "John Doe"),
                            new("mail", "john@etl-sql.org"),
                            new("userPassword", "johnpassword")
                        }
                    ));

                    // Create group matching CN=GG-Portal-Publishers
                    conn.SendRequest(new System.DirectoryServices.Protocols.AddRequest(
                        "cn=GG-Portal-Publishers,ou=groups,dc=etl-sql,dc=org",
                        new System.DirectoryServices.Protocols.DirectoryAttribute[]
                        {
                            new("objectClass", "groupOfNames"),
                            new("cn", "GG-Portal-Publishers"),
                            new("member", "cn=john,ou=users,dc=etl-sql,dc=org")
                        }
                    ));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LDAP-SEED-WARN] Portal integration seed failed: {ex.Message}");
                    throw;
                }
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);

                builder.ConfigureServices(services =>
                {
                    // Re-register real LdapService pointing to our docker container
                    services.RemoveAll<ILdapService>();
                    services.AddScoped<ILdapService, LdapService>();

                    // Replace PortalConfig
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
                        DatabasePath      = dbPath,
                        ScriptRootPath    = scriptRoot,
                        SnapshotDirectory = snapshotDir,
                        MapRootPath       = mapRoot,
                        DatasetRootPath   = datasetRoot,
                        Jwt = new JwtConfig { Secret = jwtSecret, ExpiryMinutes = 60, RefreshExpiryDays = 7 },
                        FirstRun          = new FirstRunConfig { AdminUsername = "admin" },
                        Orchestrator      = new OrchestratorConfig { DatabasePath = orchDbPath },
                        Identity = new IdentityConfig
                        {
                            Provider = "Local",
                            Ldap = new LdapIdentityConfig
                            {
                                Enabled = true,
                                Server = "127.0.0.1",
                                Port = LdapPort,
                                UseSsl = false,
                                Domain = "etl-sql.org",
                                BaseDn = "dc=etl-sql,dc=org",
                                ServiceUser = "cn=admin,dc=etl-sql,dc=org",
                                ServicePassword = "adminpassword",
                                RoleMappings = new Dictionary<string, string>
                                {
                                    { "cn=GG-Portal-Publishers,ou=groups,dc=etl-sql,dc=org", "Publisher" }
                                }
                            }
                        }
                    };
                    services.AddSingleton(cfg);
                });
            }

            async Task IAsyncLifetime.DisposeAsync()
            {
                if (_ldapContainer != null)
                {
                    var container = _ldapContainer;
                    _ldapContainer = null;
                    await container.StopAsync();
                }
                await base.DisposeAsync();
            }
        }

        [Fact]
        public async Task Login_WithRealOpenLdapContainer_Success_And_AutoProvision()
        {
            // Seed a portal group mapped to the LDAP group inside DB
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                db.Groups.Add(new Group
                {
                    Name = "GG-Portal-Publishers",
                    Description = "Publishers Group",
                    Provider = "LDAP",
                    AdGroup = "cn=GG-Portal-Publishers,ou=groups,dc=etl-sql,dc=org"
                });
                await db.SaveChangesAsync();
            }

            // 1. Perform LDAP login against the docker container
            var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest("john", "johnpassword"));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var loginResp = await res.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginResp!.Token);

            // 2. Validate user created and roles/groups assigned
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>();

                var user = await db.Users
                    .Include(u => u.UserGroups).ThenInclude(ug => ug.Group)
                    .FirstOrDefaultAsync(u => u.UserName == "john");

                Assert.NotNull(user);
                Assert.Equal("LDAP", user.Provider);
                Assert.Equal("john@etl-sql.org", user.Email);
                Assert.Equal("John", user.FirstName);
                Assert.Equal("Doe", user.LastName);

                // Verify roles mapped: cn=GG-Portal-Publishers... -> Publisher
                var roles = await userManager.GetRolesAsync(user);
                Assert.Contains("Publisher", roles);

                // Verify group mapped
                var grpNames = user.UserGroups.Select(ug => ug.Group.Name).ToList();
                Assert.Contains("GG-Portal-Publishers", grpNames);
            }
        }
    }
}
