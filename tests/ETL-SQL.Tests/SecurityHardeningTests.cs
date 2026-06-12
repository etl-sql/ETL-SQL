using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using Moq;
using Xunit;

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

        private static Mock<IExecutionContext> MakeMockContext()
        {
            var mock = new Mock<IExecutionContext>();
            mock.Setup(c => c.OuterRowStack).Returns(new Stack<Row>());
            mock.Setup(c => c.VarContext).Returns(new Mock<IVariableContext>().Object);
            mock.Setup(c => c.Connections).Returns(new Dictionary<string, IDataSource>());
            var mockFr = new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            mockFr.Setup(f => f.IsRegistered(It.IsAny<string>())).Returns(false);
            mock.Setup(c => c.FunctionRegistry).Returns(mockFr.Object);
            var mockDocker = new Mock<IDockerManager>();
            mockDocker.Setup(d => d.GetConnectionString(It.IsAny<string>())).Returns((string?)null);
            mock.Setup(c => c.DockerManager).Returns(mockDocker.Object);
            return mock;
        }

        [Fact]
        public async Task Evaluator_QualifiedIdentifier_ResolvesToCorrectTable()
        {
            var evaluator = new ExpressionEvaluator(MakeMockContext().Object);

            var schema = new TableSchema(new[] { "T1.ID", "T2.ID" });
            var row = new Row(schema, new object[] { 10, 20 });

            // T1.ID should resolve to 10 despite T2.ID also existing
            var result = await evaluator.Evaluate(new IdentifierExpression("T1.ID"), row);
            Assert.Equal(10, result);
        }

        [Fact]
        public async Task Evaluator_QualifiedIdentifier_NoMatch_ReturnsNull()
        {
            var evaluator = new ExpressionEvaluator(MakeMockContext().Object);

            var schema = new TableSchema(new[] { "T1.ID", "T2.ID" });
            var row = new Row(schema, new object[] { 10, 20 });

            // T3.ID has no match — should return null, not throw
            var result = await evaluator.Evaluate(new IdentifierExpression("T3.ID"), row);
            Assert.Null(result);
        }

        [Fact]
        public async Task Evaluator_UnqualifiedIdentifier_SingleWeakMatch_Resolves()
        {
            var evaluator = new ExpressionEvaluator(MakeMockContext().Object);

            // Only T1.Score — unqualified "Score" should resolve via weak match
            var schema = new TableSchema(new[] { "T1.Score", "T1.Name" });
            var row = new Row(schema, new object[] { 99, "Alice" });

            var result = await evaluator.Evaluate(new IdentifierExpression("Score"), row);
            Assert.Equal(99, result);
        }

        [Fact]
        public async Task Evaluator_QualifiedIdentifier_UnqualifiedColOwnedByOtherTable_IsExcluded()
        {
            var evaluator = new ExpressionEvaluator(MakeMockContext().Object);

            // Row has both unqualified "Name" and "T2.Name".
            // When resolving T1.Name, the unqualified "Name" is owned by T2 → not a weak match for T1.
            // T1.Name doesn't exist as a qualified key → null.
            var schema = new TableSchema(new[] { "Name", "T2.Name" });
            var row = new Row(schema, new object[] { "direct", "from-t2" });

            var result = await evaluator.Evaluate(new IdentifierExpression("T1.Name"), row);
            Assert.Null(result);
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
