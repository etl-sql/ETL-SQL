using System.Collections.Generic;
using ETL_SQL.Connectors;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "ODBC")]
    [Trait("CertificationClass", "MetadataOnly")]
    public class OdbcConnectionStringTests
    {
        [Fact]
        public void BuildOdbc_DSNOnly_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "DSN", "MySalesDSN" }
            };

            var cs = ConnectionStringBuilder.Build("ODBC", props);
            Assert.Equal("DSN=MySalesDSN", cs);
        }

        [Fact]
        public void BuildOdbc_DSNWithCredentials_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "DSN", "MySalesDSN" },
                { "UID", "admin" },
                { "PWD", "secret" }
            };

            var cs = ConnectionStringBuilder.Build("ODBC", props);
            Assert.Equal("DSN=MySalesDSN;UID=admin;PWD=secret", cs);
        }

        [Fact]
        public void BuildOdbc_DSNLess_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "DRIVER", "PostgreSQL Unicode" },
                { "SERVER", "localhost" },
                { "DATABASE", "testdb" },
                { "UID", "pguser" },
                { "PWD", "pgpass" }
            };

            var cs = ConnectionStringBuilder.Build("ODBC", props);
            Assert.Contains("DRIVER={PostgreSQL Unicode}", cs);
            Assert.Contains("SERVER=localhost", cs);
            Assert.Contains("DATABASE=testdb", cs);
            Assert.Contains("UID=pguser", cs);
            Assert.Contains("PWD=pgpass", cs);
        }

        [Fact]
        public void BuildOdbc_PassThroughProperties_CorrectlyBuilds()
        {
            var props = new Dictionary<string, string>
            {
                { "DSN", "MyDSN" },
                { "ReadOnly", "1" },
                { "FetchSize", "100" }
            };

            var cs = ConnectionStringBuilder.Build("ODBC", props);
            Assert.Contains("DSN=MyDSN", cs);
            Assert.Contains("ReadOnly=1", cs);
            Assert.Contains("FetchSize=100", cs);
        }
    }
}
