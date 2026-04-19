using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Common;
using Moq;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Tests
{
    public class SecurityHardeningTests
    {
        [Fact]
        public void CryptoUtils_Throws_On_Empty_Password()
        {
            Assert.Throws<ArgumentException>(() => CryptoUtils.Encrypt("data", ""));
            Assert.Throws<ArgumentException>(() => CryptoUtils.Decrypt("ENC:abc", "  "));
        }

        [Fact]
        public async Task Evaluator_Throws_On_Ambiguous_Identifiers()
        {
            var mockContext = new Mock<IExecutionContext>();
            var evaluator = new ExpressionEvaluator(mockContext.Object);

            var schema = new TableSchema(new[] { "T1.ID", "T2.ID", "Name" });
            var row = new Row(schema, new object[] { 1, 2, "Test" });

            var expr = new IdentifierExpression("ID");
            
            // Should throw because ID matches both T1.ID and T2.ID
            await Assert.ThrowsAsync<ExecutionException>(async () => await evaluator.Evaluate(expr, row));
        }

        [Fact]
        public async Task Evaluator_ExactMatch_WinsOverQualifiedSuffix()
        {
            var mockContext = new Mock<IExecutionContext>();
            var evaluator = new ExpressionEvaluator(mockContext.Object);

            var schema = new TableSchema(new[] { "ID", "T1.ID" });
            var row = new Row(schema, new object[] { 1, 2 });

            var expr = new IdentifierExpression("ID");

            // "ID" is an exact column key — resolves to value 1, not ambiguous
            var result = await evaluator.Evaluate(expr, row);
            Assert.Equal(1, result);
        }

        [Theory]
        [InlineData("SELECT * FROM T1", "src.T1")]
        [InlineData("SELECT * FROM T1 JOIN T2 ON 1=1", "src.T1", "src.T2")]
        [InlineData("WITH cte AS (SELECT * FROM T3) SELECT * FROM cte JOIN T1 ON 1=1", "src.T3", "src.T1")]
        [InlineData("SELECT * FROM (SELECT * FROM T4) t", "src.T4")]
        [InlineData("SELECT * FROM T1, T2", "src.T1", "src.T2")]
        public void ExecutePushdown_GetSourceTables_Handles_Complex_Sql(string sql, params string[] expected)
        {
            var connExpr = new LiteralExpression("src", TokenType.STRING);
            var stmt = new ExecutePushdownStatement(connExpr, sql);
            
            var sources = stmt.GetSourceTables().ToList();
            
            foreach (var exp in expected)
            {
                Assert.Contains(exp, sources);
            }
            
            // Ensure no extra sources (like CTE names)
            Assert.Equal(expected.Length, sources.Count);
        }
    }
}
