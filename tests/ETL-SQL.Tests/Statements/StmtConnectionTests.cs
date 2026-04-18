using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using System.Threading.Tasks;
using ETL_SQL.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Tests.Statements.Statements
{
    public class ConnectionTests
    {
        public ConnectionTests()
        {
        }

        private async Task Execute(string sql, Evaluator evaluator)
        {
            var lexer = new Lexer(sql);
            var parser = new Parser(lexer.Tokenize());
            var script = parser.Parse();
            await evaluator.Evaluate(script);
        }

        [Fact]
        public async Task TestDropConnection()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            // 1. Create a connection
            await Execute("CREATE CONNECTION my_mock ON MOCKDB();", evaluator);
            Assert.True(((IExecutionContext)evaluator).Connections.ContainsKey("my_mock"));

            // 2. Drop the connection
            await Execute("DROP CONNECTION my_mock;", evaluator);
            Assert.False(((IExecutionContext)evaluator).Connections.ContainsKey("my_mock"));
        }

        [Fact]
        public async Task TestDropConnectionIfExists()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            // Should not throw
            await Execute("DROP CONNECTION IF EXISTS non_existent_conn;", evaluator);
        }

        [Fact]
        public async Task TestAlterConnection()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            await Execute("CREATE CONNECTION alter_conn ON MOCKDB();", evaluator);
            await Execute("ALTER CONNECTION alter_conn ON MOCKDB();", evaluator);
            Assert.True(((IExecutionContext)evaluator).Connections.ContainsKey("alter_conn"));
        }

        [Fact]
        public async Task TestAlterConnection_ThrowsWhenNotExists()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                await Execute("ALTER CONNECTION nonexistent_alter ON MOCKDB();", evaluator));
        }

        [Fact]
        public async Task TestCreateOrAlterConnection_CreatesWhenNotExists()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            await Execute("CREATE OR ALTER CONNECTION coalt_conn ON MOCKDB();", evaluator);
            Assert.True(((IExecutionContext)evaluator).Connections.ContainsKey("coalt_conn"));
        }

        [Fact]
        public async Task TestCreateOrAlterConnection_AltersWhenExists()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            await Execute("CREATE CONNECTION coalt_existing ON MOCKDB();", evaluator);
            // Second call should succeed (alters the existing one, not throw duplicate)
            await Execute("CREATE OR ALTER CONNECTION coalt_existing ON MOCKDB();", evaluator);
            Assert.True(((IExecutionContext)evaluator).Connections.ContainsKey("coalt_existing"));
        }

        [Fact]
        public async Task TestCreateConnection_ThrowsWhenAlreadyExists()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            await Execute("CREATE CONNECTION dup_conn ON MOCKDB();", evaluator);
            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                await Execute("CREATE CONNECTION dup_conn ON MOCKDB();", evaluator));
        }

        [Fact]
        public async Task TestRenamedCommandsParsing()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            // Test parsing of the new names
            await Execute("CREATE CONNECTION smtp ON SMTP('localhost');", evaluator);

            // This verifies the PARSER accepts SEND_EMAIL
            // We expect an execution failure because localhost:25 isn't open, but Parser should succeed.
            try { await Execute("SEND_EMAIL TO 'test@test.com' SUBJECT 'Hi' BODY 'Hello' AT smtp;", evaluator); }
            catch (Exception ex) when (ex.Message.Contains("Unexpected token")) { throw; }
            catch { /* Ignore connection/execution errors */ }
            
            // Testing SEND_FILE / RECEIVE_FILE parsing
            try { await Execute("SEND_FILE 'local.txt', smtp, 'remote.txt';", evaluator); }
            catch (Exception ex) when (ex.Message.Contains("Unexpected token")) { throw; }
            catch { /* Ignore connection/execution errors */ }
        }
    }
}
