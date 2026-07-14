using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    /// <summary>
    /// Covers TEST CONNECTION parsing (including the soft-keyword guarantee that "test" remains a
    /// usable identifier) and the governed diagnostic handler's behaviour for a non-network connection.
    /// Socket-level DNS/TCP/TLS paths are exercised by manual/end-to-end verification.
    /// </summary>
    public class StmtTestConnectionTests
    {
        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var parser = new Parser(lexer.Tokenize());
            return parser.Parse();
        }

        private static async Task Execute(string sql, Evaluator evaluator)
        {
            var script = Parse(sql);
            await evaluator.Evaluate(script);
        }

        [Fact]
        public void ParsesTestConnection()
        {
            var script = Parse("TEST CONNECTION prod;");
            var stmt = Assert.IsType<TestConnectionStatement>(script.Statements.Single());
            Assert.Equal("prod", stmt.ConnectionName);
            Assert.Null(stmt.IntoTable);
        }

        [Fact]
        public void ParsesTestConnectionIntoTempTable()
        {
            var script = Parse("TEST CONNECTION prod INTO #diag;");
            var stmt = Assert.IsType<TestConnectionStatement>(script.Statements.Single());
            Assert.Equal("prod", stmt.ConnectionName);
            Assert.Equal("#diag", stmt.IntoTable);
        }

        [Fact]
        public void TestRemainsAUsableIdentifier()
        {
            // The soft keyword must not reserve "test": these must still parse without error.
            _ = Parse("SELECT * FROM test;");
            _ = Parse("CREATE TABLE test (id INT);");
        }

        [Fact]
        public async Task MissingConnectionThrows()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            await Assert.ThrowsAsync<ExecutionException>(() => Execute("TEST CONNECTION does_not_exist;", evaluator));
        }

        [Fact]
        public async Task ReachableTcpEndpointReportsDnsAndTcpOk()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            var context = (IExecutionContext)evaluator;
            context.PreviewLimit = 0; // don't open the datasource on CREATE

            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                await Execute(
                    $"CREATE CONNECTION diag_tcp AS MSSQL(SERVER='127.0.0.1', PORT={port}, DATABASE='x', USERNAME='u', PASSWORD='p');",
                    evaluator);
                await Execute("TEST CONNECTION diag_tcp;", evaluator);

                var table = Assert.IsType<DataTable>(context.LastResult);
                var status = table.Rows.ToDictionary(r => (string)r["Layer"]!, r => (string)r["Status"]!);

                Assert.Equal("OK", status.GetValueOrDefault("DNS"));
                Assert.Equal("OK", status.GetValueOrDefault("TCP"));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task NonNetworkConnectionReportsPolicyOkAndNetworkSkipped()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            var context = (IExecutionContext)evaluator;

            await Execute("CREATE CONNECTION diag_mock AS MOCKDB();", evaluator);
            await Execute("TEST CONNECTION diag_mock;", evaluator);

            var table = Assert.IsType<DataTable>(context.LastResult);
            Assert.Equal(new[] { "Layer", "Status", "Detail", "Remedy" }, table.ColumnNames.ToArray());

            var policy = table.Rows.Single(r => (string?)r["Layer"] == "POLICY");
            Assert.Equal("OK", policy["Status"]);

            var network = table.Rows.Single(r => (string?)r["Layer"] == "NETWORK");
            Assert.Equal("SKIPPED", network["Status"]);
        }
    }
}
