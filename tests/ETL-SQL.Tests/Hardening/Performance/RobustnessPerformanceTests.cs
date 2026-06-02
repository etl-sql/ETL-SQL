using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Common;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ETL_SQL.Tests.Hardening.Performance
{
    [Trait("Category", "Performance")]
    public class RobustnessPerformanceTests
    {
        // ─── JSON Streaming ─────────────────────────────────────────────────────

        [Fact]
        public async Task JsonExtractor_Streaming_HandlesLargeDatasets()
        {
            // Generate a fake large JSON string (array of objects)
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < 5000; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"{{\"id\":{i}, \"name\":\"Row_{i}\", \"data\":\"{new string('A', 100)}\"}}");
            }
            sb.Append("]");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
            
            var batchCount = 0;
            var rowCount = 0;
            
            await foreach (var batch in JsonExtractor.ExtractBatchesAsync(stream, "$", batchSize: 1000))
            {
                batchCount++;
                rowCount += batch.Rows.Count;
                Assert.True(batch.Rows.Count <= 1000);
            }

            Assert.Equal(5, batchCount);
            Assert.Equal(5000, rowCount);
        }

        [Fact]
        public async Task JsonExtractor_PathNavigation_Streaming()
        {
            var json = "{\"metadata\": {\"status\": \"ok\"}, \"items\": [{\"id\":1}, {\"id\":2}]}";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            var results = await JsonExtractor.ExtractBatchesAsync(stream, "items", batchSize: 10).ToListAsync();
            
            Assert.Single(results);
            Assert.Equal(2, results[0].Rows.Count);
            Assert.Equal((decimal)1, results[0].Rows[0]["id"]);
        }

        // ─── Configurable Security ──────────────────────────────────────────────

        [Fact]
        public async Task CredentialLeakRule_SupportsCustomKeywords()
        {
            var customKeywords = new[] { "topsecret", "ultra-confidential" };
            var rule = new CredentialLeakRule(customKeywords);
            
            var linter = new Linter();
            linter.AddRule(rule);

            var source = @"
DECLARE @topsecret_val STRING = 'hidden';
PRINT @topsecret_val;";

            var tokens = new Lexer(source).Tokenize();
            var script = new Parser(tokens).Parse();
            
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            
            Assert.Contains(results, r => r.Message.Contains("topsecret_val") && r.RuleName == "CredentialLeak");
        }

        // ─── CLI Robustness ─────────────────────────────────────────────────────

        [Fact]
        public void LinterFactory_MergesDIAndReflectionRules()
        {
            var services = new ServiceCollection();
            // Register a custom version of CredentialLeakRule
            services.AddSingleton<ILintRule>(new CredentialLeakRule(new[] { "custom_key" }));
            var sp = services.BuildServiceProvider();

            var linter = LinterFactory.CreateWithAllRules(sp);
            
            // Should have CredentialLeakRule (from DI) and others (from reflection like SpillSecurityRule)
            Assert.True(linter.HasRuleOfType(typeof(CredentialLeakRule)));
            Assert.True(linter.HasRuleOfType(typeof(SpillSecurityRule)));
        }

        // ─── Email Validation ───────────────────────────────────────────────────

        [Theory]
        [InlineData("test@example.com", true)]
        [InlineData("test@example.com;other@web.de", true)]
        [InlineData("invalid-email", false)]
        [InlineData("valid@mail.com; invalid@", false)]
        public async Task EmailStatementHandler_ValidatesFormats(string emailList, bool shouldPass)
        {
            var logger = new Mock<ILogger>();
            var handler = new EmailStatementHandler(logger.Object);
            
            var stmt = new EmailStatement(
                new LiteralExpression(emailList, TokenType.STRING),
                new LiteralExpression("from@me.com", TokenType.STRING),
                new LiteralExpression("Sub", TokenType.STRING),
                new LiteralExpression("Body", TokenType.STRING)
            );

            var context = new Mock<IExecutionContext>();
            context.Setup(c => c.EvaluateValue(It.IsAny<Expression>(), It.IsAny<Row>(), It.IsAny<bool>()))
                   .ReturnsAsync((Expression e, Row r, bool d) => (e as LiteralExpression)?.Value);
            
            // Mock connection for handler
            var mockSource = new Mock<IDataSource>();
            var connections = new Dictionary<string, IDataSource> { { "smtp", mockSource.Object } };
            context.Setup(c => c.Connections).Returns(connections);
            stmt.ConnectionName = new LiteralExpression("smtp", TokenType.STRING);

            if (shouldPass)
            {
                await handler.Execute(stmt, context.Object);
            }
            else
            {
                await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => handler.Execute(stmt, context.Object));
            }
        }
    }

    internal static class AsyncEnumerableExtensions
    {
        public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
        {
            var list = new List<T>();
            await foreach (var item in source) list.Add(item);
            return list;
        }
    }
}
