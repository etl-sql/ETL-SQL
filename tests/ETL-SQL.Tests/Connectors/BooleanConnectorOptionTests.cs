using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// CREATE CONNECTION stores an unquoted TRUE as "ON". Connectors that compared against "TRUE"
    /// alone read the documented <c>USE_SSL = TRUE</c> as false: MSSQL set Encrypt=False explicitly
    /// and Active Directory bound over plain LDAP.
    /// </summary>
    [Trait("CompatBreak", "0.22.0")]
    [Trait("Connector", "MULTIPLE")]
    public class BooleanConnectorOptionTests
    {
        [Theory]
        [InlineData("TRUE", true)]
        [InlineData("ON", true)]
        [InlineData(" on ", true)]
        [InlineData("YES", true)]
        [InlineData("1", true)]
        [InlineData("FALSE", false)]
        [InlineData("OFF", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsTrue_AcceptsBothBooleanSpellings(string? value, bool expected) =>
            Assert.Equal(expected, ConnectorOptionValues.IsTrue(value));

        [Theory]
        [InlineData("ENCRYPT")]
        [InlineData("USE_SSL")]
        public void SqlServer_On_Encrypts(string key)
        {
            var on = DatabaseConnectionStringBuilder.Build("MSSQL",
                new Dictionary<string, string> { { "SERVER", "db" }, { key, "ON" } });
            var off = DatabaseConnectionStringBuilder.Build("MSSQL",
                new Dictionary<string, string> { { "SERVER", "db" }, { key, "OFF" } });

            Assert.Contains("Encrypt=True", on, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Encrypt=False", off, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SqlServer_TrustedConnectionOn_UsesIntegratedSecurity()
        {
            var cs = DatabaseConnectionStringBuilder.Build("MSSQL",
                new Dictionary<string, string> { { "SERVER", "db" }, { "TRUSTED_CONNECTION", "ON" } });

            Assert.Contains("Integrated Security=True", cs);
        }

        [Fact]
        public void ActiveDirectory_UseSslOn_UsesLdaps()
        {
            var url = new ActiveDirectoryConnector().BuildConnectionString(
                new Dictionary<string, string> { { "HOST", "dc.example.com" }, { "USE_SSL", "ON" } });

            Assert.StartsWith("ldaps://", url);
        }

        [Fact]
        public async Task CreateConnection_UnquotedUseSslTrue_Encrypts()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            const string sql = "CREATE CONNECTION bool_tls AS MSSQL(SERVER = 'db.invalid', DATABASE = 'd', USE_SSL = TRUE);";
            await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());

            // The engine stores the literal as "ON"; the connection string is built from these options.
            var options = ((IExecutionContext)evaluator).Connections["bool_tls"].Options!;
            Assert.Equal("ON", options["USE_SSL"]);
            Assert.Contains("Encrypt=True", DatabaseConnectionStringBuilder.Build("MSSQL", options),
                System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
