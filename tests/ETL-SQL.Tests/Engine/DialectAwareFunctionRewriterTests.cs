using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class DialectAwareFunctionRewriterTests
    {
        private static Script Parse(string source)
        {
            var tokens = new Lexer(source).Tokenize();
            return new Parser(tokens, source).Parse();
        }

        [Fact]
        public void TestMSSQLRewriting()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var compiler = new QueryCompiler(ev);

            // Test SYSDATE as identifier
            var stmt = Parse("SELECT SYSDATE FROM MyDb.Payment;").Statements[0];
            var compiled = compiler.CompileQuery(stmt, "MSSQL");
            Assert.Contains("GETDATE()", compiled.Sql);

            // Test NOW as function
            var stmt2 = Parse("SELECT NOW() FROM MyDb.Payment;").Statements[0];
            var compiled2 = compiler.CompileQuery(stmt2, "MSSQL");
            Assert.Contains("GETDATE()", compiled2.Sql);

            // Test TRUNC 1-arg
            var stmt3 = Parse("SELECT TRUNC(payment_date) FROM MyDb.Payment;").Statements[0];
            var compiled3 = compiler.CompileQuery(stmt3, "MSSQL");
            Assert.Contains("CAST(payment_date AS DATE)", compiled3.Sql);

            // Test TRUNC 2-arg month
            var stmt4 = Parse("SELECT TRUNC(payment_date, 'MM') FROM MyDb.Payment;").Statements[0];
            var compiled4 = compiler.CompileQuery(stmt4, "MSSQL");
            Assert.Contains("DATEADD(month, DATEDIFF(month, 0, payment_date), 0)", compiled4.Sql);
        }

        [Fact]
        public void TestPostgresRewriting()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var compiler = new QueryCompiler(ev);

            // Test SYSDATE as identifier
            var stmt = Parse("SELECT SYSDATE FROM MyDb.Payment;").Statements[0];
            var compiled = compiler.CompileQuery(stmt, "POSTGRES");
            Assert.Contains("NOW()", compiled.Sql);

            // Test GETDATE as function
            var stmt2 = Parse("SELECT GETDATE() FROM MyDb.Payment;").Statements[0];
            var compiled2 = compiler.CompileQuery(stmt2, "POSTGRES");
            Assert.Contains("NOW()", compiled2.Sql);

            // Test TRUNC 1-arg
            var stmt3 = Parse("SELECT TRUNC(payment_date) FROM MyDb.Payment;").Statements[0];
            var compiled3 = compiler.CompileQuery(stmt3, "POSTGRES");
            Assert.Contains("DATE_TRUNC('day', payment_date)", compiled3.Sql);

            // Test TRUNC 2-arg month
            var stmt4 = Parse("SELECT TRUNC(payment_date, 'month') FROM MyDb.Payment;").Statements[0];
            var compiled4 = compiler.CompileQuery(stmt4, "POSTGRES");
            Assert.Contains("DATE_TRUNC('month', payment_date)", compiled4.Sql);
        }
    }
}
