using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class DialectTranslationMatrixTests
    {
        private static Script Parse(string source)
        {
            var tokens = new Lexer(source).Tokenize();
            return new Parser(tokens, source).Parse();
        }

        private static string Compile(string query, string dialect)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var compiler = new QueryCompiler(ev);
            var stmt = Parse(query).Statements[0];
            return compiler.CompileQuery(stmt, dialect).Sql;
        }

        [Theory]
        [InlineData("SELECT SYSDATE FROM t1;", "SELECT GETDATE() FROM [t1]", "SELECT NOW() FROM t1", "SELECT SYSDATE FROM T1")]
        [InlineData("SELECT NOW() FROM t1;", "SELECT GETDATE() FROM [t1]", "SELECT NOW() FROM t1", "SELECT SYSDATE FROM T1")]
        [InlineData("SELECT GETDATE() FROM t1;", "SELECT GETDATE() FROM [t1]", "SELECT NOW() FROM t1", "SELECT SYSDATE FROM T1")]
        [InlineData("SELECT TRUNC(c1) FROM t1;", "SELECT CAST(c1 AS DATE) FROM [t1]", "SELECT DATE_TRUNC('day', c1) FROM t1", "SELECT TRUNC(c1) FROM T1")]
        [InlineData("SELECT TRUNC(c1, 'MM') FROM t1;", "SELECT DATEADD(month, DATEDIFF(month, 0, c1), 0) FROM [t1]", "SELECT DATE_TRUNC('MM', c1) FROM t1", "SELECT TRUNC(c1, @p0) FROM T1")]
        [InlineData("SELECT TRUNC(c1, 'MONTH') FROM t1;", "SELECT DATEADD(month, DATEDIFF(month, 0, c1), 0) FROM [t1]", "SELECT DATE_TRUNC('MONTH', c1) FROM t1", "SELECT TRUNC(c1, @p0) FROM T1")]
        [InlineData("SELECT TRUNC(c1, 'YY') FROM t1;", "SELECT DATEADD(year, DATEDIFF(year, 0, c1), 0) FROM [t1]", "SELECT DATE_TRUNC('YY', c1) FROM t1", "SELECT TRUNC(c1, @p0) FROM T1")]
        [InlineData("SELECT TRUNC(c1, 'YEAR') FROM t1;", "SELECT DATEADD(year, DATEDIFF(year, 0, c1), 0) FROM [t1]", "SELECT DATE_TRUNC('YEAR', c1) FROM t1", "SELECT TRUNC(c1, @p0) FROM T1")]
        [InlineData("SELECT CAST(c1 AS VARCHAR) FROM t1;", "SELECT CAST(c1 AS VARCHAR) FROM [t1]", "SELECT CAST(c1 AS VARCHAR) FROM t1", "SELECT CAST(c1 AS VARCHAR) FROM T1")]
        [InlineData("SELECT TRY_CAST(c1 AS INT) FROM t1;", "SELECT TRY_CAST(c1 AS INT) FROM [t1]", "SELECT TRY_CAST(c1 AS INT) FROM t1", "SELECT TRY_CAST(c1 AS INT) FROM T1")]
        [InlineData("SELECT UPPER(c1) FROM t1;", "SELECT UPPER(c1) FROM [t1]", "SELECT UPPER(c1) FROM t1", "SELECT UPPER(c1) FROM T1")]
        // A. Null Handling
        [InlineData("SELECT ISNULL(c1, 'N/A') FROM t1;", "SELECT ISNULL(c1, @p0) FROM [t1]", "SELECT COALESCE(c1, @p0) FROM t1", "SELECT COALESCE(c1, @p0) FROM T1")]
        // B. Date Part Extractors
        [InlineData("SELECT YEAR(c1) FROM t1;", "SELECT YEAR(c1) FROM [t1]", "SELECT EXTRACT(YEAR FROM c1) FROM t1", "SELECT EXTRACT(YEAR FROM c1) FROM T1")]
        [InlineData("SELECT MONTH(c1) FROM t1;", "SELECT MONTH(c1) FROM [t1]", "SELECT EXTRACT(MONTH FROM c1) FROM t1", "SELECT EXTRACT(MONTH FROM c1) FROM T1")]
        [InlineData("SELECT DAY(c1) FROM t1;", "SELECT DAY(c1) FROM [t1]", "SELECT EXTRACT(DAY FROM c1) FROM t1", "SELECT EXTRACT(DAY FROM c1) FROM T1")]
        // C. String Length
        [InlineData("SELECT LEN(c1) FROM t1;", "SELECT LEN(c1) FROM [t1]", "SELECT LENGTH(c1) FROM t1", "SELECT LENGTH(c1) FROM T1")]
        [InlineData("SELECT LENGTH(c1) FROM t1;", "SELECT LEN(c1) FROM [t1]", "SELECT LENGTH(c1) FROM t1", "SELECT LENGTH(c1) FROM T1")]
        // D. Substrings
        [InlineData("SELECT SUBSTRING(c1, 1, 3) FROM t1;", "SELECT SUBSTRING(c1, @p0, @p1) FROM [t1]", "SELECT SUBSTRING(c1, @p0, @p1) FROM t1", "SELECT SUBSTR(c1, @p0, @p1) FROM T1")]
        public void VerifyDialectFunctionRewriting(string inputSql, string expectedMssql, string expectedPostgres, string expectedOracle)
        {
            var mssqlResult = Compile(inputSql, "MSSQL");
            var postgresResult = Compile(inputSql, "POSTGRES");
            var oracleResult = Compile(inputSql, "ORACLE");

            Assert.Equal(expectedMssql, mssqlResult);
            Assert.Equal(expectedPostgres, postgresResult);
            Assert.Equal(expectedOracle, oracleResult);
        }
    }
}
