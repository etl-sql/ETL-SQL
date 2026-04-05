using Xunit;
using System.Collections.Generic;
using ETL_SQL.Connectors;

namespace ETL_SQL.Tests.Connectors
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
