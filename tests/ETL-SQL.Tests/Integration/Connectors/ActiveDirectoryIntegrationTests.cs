using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors;
using ETL_SQL.Services;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("LDAP collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "ACTIVE_DIRECTORY")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class ActiveDirectoryIntegrationTests
    {
        private readonly ActiveDirectoryFixture _fixture;

        public ActiveDirectoryIntegrationTests(ActiveDirectoryFixture fixture)
        {
            _fixture = fixture;
        }

        private IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        private Dictionary<string, string> GetValidOptions() => new()
        {
            { "HOST", _fixture.Host },
            { "PORT", _fixture.Port.ToString() },
            { "AUTH_MODE", "SIMPLE" },
            { "USER", ActiveDirectoryFixture.AdminUser },
            { "PASSWORD", ActiveDirectoryFixture.AdminPassword },
            { "BASE_DN", ActiveDirectoryFixture.BaseDn }
        };

        [Fact]
        public async Task ADConnector_GetVersionAsync_Success()
        {
            var ctx = MakeContext();
            var options = GetValidOptions();
            var connector = new ActiveDirectoryConnector(ctx, "", options);

            var version = await connector.GetVersionAsync(ctx, "");
            Assert.Contains("Connected", version);
            Assert.Contains(_fixture.Host, version);
        }

        [Fact]
        public async Task ADConnector_GetVersionAsync_InvalidCredentials_ThrowsException()
        {
            var ctx = MakeContext();
            var options = GetValidOptions();
            options["PASSWORD"] = "wrong-admin-password";

            var connector = new ActiveDirectoryConnector(ctx, "", options);

            await Assert.ThrowsAsync<ExecutionException>(() =>
                connector.GetVersionAsync(ctx, ""));
        }

        [Fact]
        public async Task ADConnector_ReadBatches_ReturnsSeededUsers()
        {
            var ctx = MakeContext();
            var options = GetValidOptions();
            options["FILTER_CONTEXT"] = "users";
            options["ATTRIBUTES"] = "cn,mail,displayName";

            var connector = new ActiveDirectoryConnector(ctx, "", options);
            var batches = await connector.ReadBatches().ToListAsync();

            Assert.Single(batches);
            var table = batches[0];
            
            // Check that the seeded test user "john" exists in results
            Assert.Contains(table.Rows, r => r["cn"]?.ToString() == "john" && r["mail"]?.ToString() == "john@etl-sql.org");
        }

        [Fact]
        public async Task ADConnector_ReadBatches_ReturnsSeededGroups()
        {
            var ctx = MakeContext();
            var options = GetValidOptions();
            options["FILTER_CONTEXT"] = "groups";
            options["ATTRIBUTES"] = "cn,member";

            var connector = new ActiveDirectoryConnector(ctx, "", options);
            var batches = await connector.ReadBatches().ToListAsync();

            Assert.Single(batches);
            var table = batches[0];

            // Verify our seeded group CN=GG-Finance-Readers is present
            Assert.Contains(table.Rows, r => r["cn"]?.ToString() == "GG-Finance-Readers");
        }
    }
}
