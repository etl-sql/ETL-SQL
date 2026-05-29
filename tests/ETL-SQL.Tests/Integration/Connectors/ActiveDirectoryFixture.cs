using System;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class ActiveDirectoryFixture : IAsyncLifetime
    {
        private IContainer? _container;

        public const string AdminUser = "cn=admin,dc=etl-sql,dc=org";
        public const string AdminPassword = "adminpassword";
        public const string BaseDn = "dc=etl-sql,dc=org";
        
        public int Port { get; private set; }
        public string Host => "127.0.0.1";

        public LdapConnection CreateConnection(string user = AdminUser, string password = AdminPassword)
        {
            var identifier = new LdapDirectoryIdentifier(Host, Port);
            var connection = new LdapConnection(identifier)
            {
                Credential = new NetworkCredential(user, password),
                AuthType = AuthType.Basic
            };
            connection.SessionOptions.ProtocolVersion = 3;
            return connection;
        }

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder()
                .WithImage("osixia/openldap:1.5.0")
                .WithPortBinding(389, true)
                .WithEnvironment("LDAP_ORGANISATION", "ETL-SQL")
                .WithEnvironment("LDAP_DOMAIN", "etl-sql.org")
                .WithEnvironment("LDAP_ADMIN_PASSWORD", AdminPassword)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(389))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(389);

            // Wait a moment to ensure LDAP is fully ready to accept binds
            await Task.Delay(2000);

            // Seed organizational units, groups, and users
            using var conn = CreateConnection();
            conn.Bind();

            try
            {
                // Create OU=users
                conn.SendRequest(new AddRequest(
                    "ou=users,dc=etl-sql,dc=org",
                    new DirectoryAttribute[]
                    {
                        new("objectClass", "organizationalUnit"),
                        new("ou", "users")
                    }
                ));

                // Create OU=groups
                conn.SendRequest(new AddRequest(
                    "ou=groups,dc=etl-sql,dc=org",
                    new DirectoryAttribute[]
                    {
                        new("objectClass", "organizationalUnit"),
                        new("ou", "groups")
                    }
                ));

                // Create a test user: cn=john,ou=users,dc=etl-sql,dc=org
                conn.SendRequest(new AddRequest(
                    "cn=john,ou=users,dc=etl-sql,dc=org",
                    new DirectoryAttribute[]
                    {
                        new("objectClass", new[] { "inetOrgPerson", "organizationalPerson", "person" }),
                        new("cn", "john"),
                        new("sn", "Doe"),
                        new("displayName", "John Doe"),
                        new("mail", "john@etl-sql.org"),
                        new("userPassword", "johnpassword")
                    }
                ));

                // Create a test group: cn=GG-Finance-Readers,ou=groups,dc=etl-sql,dc=org
                conn.SendRequest(new AddRequest(
                    "cn=GG-Finance-Readers,ou=groups,dc=etl-sql,dc=org",
                    new DirectoryAttribute[]
                    {
                        new("objectClass", "groupOfNames"),
                        new("cn", "GG-Finance-Readers"),
                        new("member", "cn=john,ou=users,dc=etl-sql,dc=org") // memberOf overlay maps member attribute back
                    }
                ));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LDAP-SEED-ERROR] Failed to seed LDAP structure: {ex.Message}");
            }
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.StopAsync();
            }
        }
    }

    [CollectionDefinition("LDAP collection")]
    public class LdapCollection : ICollectionFixture<ActiveDirectoryFixture> { }
}
