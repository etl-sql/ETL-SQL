using Xunit;
using System.Collections.Generic;
using ETL_SQL.Connectors;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class ConnectionStringBuilderTests
    {
        [Fact]
        public void BuildSqlServer_Trusted_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "SERVER", "localhost" },
                { "DATABASE", "TestDB" },
                { "TRUSTED_CONNECTION", "TRUE" }
            };

            var cs = ConnectionStringBuilder.Build("MSSQL", props);

            Assert.Contains("Data Source=localhost", cs);
            Assert.Contains("Initial Catalog=TestDB", cs);
            Assert.Contains("Integrated Security=True", cs);
        }

        [Fact]
        public void BuildSqlServer_UserPass_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "SERVER", "localhost" },
                { "DATABASE", "TestDB" },
                { "USER", "sa" },
                { "PASSWORD", "password123" }
            };

            var cs = ConnectionStringBuilder.Build("MSSQL", props);

            Assert.Contains("User ID=sa", cs);
            Assert.Contains("Password=password123", cs);
        }

        [Fact]
        public void BuildSqlServer_ProductionOptions_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "SERVER", "dbserver" },
                { "DATABASE", "TestDB" },
                { "APPLICATION_INTENT", "READONLY" },
                { "MULTI_SUBNET_FAILOVER", "TRUE" },
                { "MIN_POOL_SIZE", "10" },
                { "MAX_POOL_SIZE", "100" },
                { "CONNECT_TIMEOUT", "60" }
            };

            var cs = ConnectionStringBuilder.Build("MSSQL", props);

            Assert.True(cs.Contains("Application Intent=ReadOnly", StringComparison.OrdinalIgnoreCase), $"Expected 'Application Intent=ReadOnly' in: {cs}");
            Assert.True(cs.Contains("Multi Subnet Failover=True", StringComparison.OrdinalIgnoreCase), $"Expected 'Multi Subnet Failover=True' in: {cs}");
            Assert.True(cs.Contains("Min Pool Size=10", StringComparison.OrdinalIgnoreCase), $"Expected 'Min Pool Size=10' in: {cs}");
            Assert.True(cs.Contains("Max Pool Size=100", StringComparison.OrdinalIgnoreCase), $"Expected 'Max Pool Size=100' in: {cs}");
            Assert.True(cs.Contains("Connect Timeout=60", StringComparison.OrdinalIgnoreCase), $"Expected 'Connect Timeout=60' in: {cs}");
        }

        [Fact]
        public void BuildPostgres_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST", "localhost" },
                { "DATABASE", "mydb" },
                { "USER", "etl" },
                { "PASSWORD", "p@ssword" },
                { "PORT", "5432" }
            };

            var cs = ConnectionStringBuilder.Build("POSTGRES", props);

            Assert.Contains("Host=localhost", cs);
            Assert.Contains("Database=mydb", cs);
            Assert.Contains("Username=etl", cs);
            Assert.Contains("Password=p@ssword", cs);
            Assert.Contains("Port=5432", cs);
        }

        [Fact]
        public void BuildPostgres_ProductionOptions_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST", "pgserver" },
                { "POOLING", "TRUE" },
                { "MIN_POOL_SIZE", "5" },
                { "MAX_POOL_SIZE", "50" },
                { "SSL_MODE", "REQUIRE" },
                { "TRUST_SERVER_CERTIFICATE", "TRUE" }
            };

            var cs = ConnectionStringBuilder.Build("POSTGRES", props);

            Assert.Contains("Pooling=true", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Minimum Pool Size=5", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Maximum Pool Size=50", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SSL Mode=Require", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Trust Server Certificate=true", cs, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildOracle_EasyConnect_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST", "dbserver" },
                { "PORT", "1521" },
                { "SERVICE_NAME", "xe" },
                { "USER", "scott" },
                { "PASSWORD", "tiger" }
            };

            var cs = ConnectionStringBuilder.Build("ORACLE", props);

            Assert.Contains("USER ID=scott", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PASSWORD=tiger", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DATA SOURCE=dbserver:1521/xe", cs, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildOracle_ProductionOptions_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST", "oraserver" },
                { "SERVICE_NAME", "orcl" },
                { "POOLING", "TRUE" },
                { "MIN_POOL_SIZE", "2" },
                { "MAX_POOL_SIZE", "20" },
                { "CONNECTION_LIFETIME", "300" }
            };

            var cs = ConnectionStringBuilder.Build("ORACLE", props);

            Assert.Contains("Pooling=true", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Min Pool Size=2", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Max Pool Size=20", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Connection LifeTime=300", cs, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildOracle_TNS_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "TNS_NAME", "ORA_PRODUCTION" },
                { "USER", "scott" },
                { "PASSWORD", "tiger" }
            };

            var cs = ConnectionStringBuilder.Build("ORACLE", props);

            Assert.Contains("DATA SOURCE=ORA_PRODUCTION", cs, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("USER ID=scott", cs, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFile_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "PATH", @"C:\temp\data.csv" }
            };

            var cs = ConnectionStringBuilder.Build("FLATFILE", props);

            Assert.Equal(@"C:\temp\data.csv", cs);
        }

        [Fact]
        public void BuildRemote_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "HOST", "ftp.example.com" }
            };

            var cs = ConnectionStringBuilder.Build("SFTP", props);

            Assert.Equal("ftp.example.com", cs);
        }
    }
}
