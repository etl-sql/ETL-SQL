using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Collection("Database collection")]
    public class DatabaseIntegrationTests
    {
        private readonly DatabaseFixture _fixture;

        public DatabaseIntegrationTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<Evaluator> GetEvaluator()
        {
             var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
             await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION ms ON MSSQL('{_fixture.SqlConnectionString}');").Tokenize()).Parse());
             await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION pg ON POSTGRES('{_fixture.PostgresConnectionString}');").Tokenize()).Parse());
             return eval;
        }

        [Fact]
        public async Task TestDbToDbTransfer()
        {
            AnsiConsole.MarkupLine("  - Scenario: MSSQL -> Postgres Transfer...");
            var eval = await GetEvaluator();

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE ms.src_users (id INT, name VARCHAR(50));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO ms.src_users VALUES (1, 'Alice'), (2, 'Bob');").Tokenize()).Parse());

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE pg.tgt_users (id INT, name VARCHAR(50));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO pg.tgt_users SELECT * FROM ms.src_users;").Tokenize()).Parse());

            await eval.Evaluate(new Parser(new Lexer("SELECT COUNT(*) FROM pg.tgt_users;").Tokenize()).Parse());
            Assert.Equal(2, Convert.ToInt32(eval.LastResult?.Rows[0][0]));
        }

        [Fact]
        public async Task TestMultiDbJoin()
        {
            AnsiConsole.MarkupLine("  - Scenario: MSSQL + Postgres JOIN -> Postgres...");
            
            var eval = await GetEvaluator();
            
            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE ms.customers_ext (id INT, name VARCHAR(50));").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO ms.customers_ext VALUES (1, 'Alice'), (2, 'Bob');").Tokenize()).Parse());

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE pg.orders_ext (cid INT, amt DECIMAL);").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO pg.orders_ext VALUES (1, 100.50), (1, 50.25), (2, 200.00);").Tokenize()).Parse());

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE pg.summary_ext (name VARCHAR(50), total DECIMAL);").Tokenize()).Parse());

            string etl = @"
                INSERT INTO pg.summary_ext
                SELECT c.name, SUM(o.amt)
                FROM ms.customers_ext c
                JOIN pg.orders_ext o ON c.id = o.cid
                GROUP BY c.name;";
            
            await eval.Evaluate(new Parser(new Lexer(etl).Tokenize(), etl).Parse());

            // Verify
            await eval.Evaluate(new Parser(new Lexer("SELECT COUNT(*) FROM pg.summary_ext;").Tokenize()).Parse());
            int count = Convert.ToInt32(eval.LastResult?.Rows[0][0] ?? 0);
            Assert.True(count >= 1, "Expected rows in summary_ext");
        }

        [Fact]
        public async Task TestComplexJoinAndTransfer()
        {
            AnsiConsole.MarkupLine("  - Scenario: Complex Join (Oracle + FlatFile) -> MSSQL...");
            
            var eval = await GetEvaluator();
            await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION ora ON ORACLE('{_fixture.OracleConnectionString}');").Tokenize()).Parse());
            
            string csvPath = Path.Combine(AppContext.BaseDirectory, "regions_final.csv");
            await File.WriteAllTextAsync(csvPath, "RegionID,RegionName\n1,North\n2,South");
            await eval.Evaluate(new Parser(new Lexer($"CREATE CONNECTION csv ON FLATFILE('{csvPath.Replace("\\", "/")}');").Tokenize()).Parse());

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE ora.Sales_Final (ID INT, RID INT, Amt DECIMAL);").Tokenize()).Parse());
            await eval.Evaluate(new Parser(new Lexer("INSERT INTO ora.Sales_Final VALUES (101, 1, 500.00), (102, 2, 750.00), (103, 1, 250.00);").Tokenize()).Parse());

            await eval.Evaluate(new Parser(new Lexer("CREATE TABLE ms.RegSales_Final (RName VARCHAR(50), Total DECIMAL);").Tokenize()).Parse());

            string script = @"
                INSERT INTO ms.RegSales_Final
                SELECT R.RegionName, SUM(S.Amt)
                FROM ora.Sales_Final S
                JOIN csv R ON S.RID = CAST(R.RegionID AS INT)
                GROUP BY R.RegionName;
            ";
            await eval.Evaluate(new Parser(new Lexer(script).Tokenize(), script).Parse());

            // Verify
            await eval.Evaluate(new Parser(new Lexer("SELECT COUNT(*) FROM ms.RegSales_Final;").Tokenize()).Parse());
            Assert.True(Convert.ToInt32(eval.LastResult?.Rows[0][0] ?? 0) >= 1);

            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }
}
