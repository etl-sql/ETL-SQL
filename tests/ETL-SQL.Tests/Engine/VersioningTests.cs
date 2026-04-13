using System;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Common;
using System.Linq;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Engine
{
    public class VersioningTests
    {
        private readonly IServiceProvider _serviceProvider;

        public VersioningTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task ShowVersion_ReturnsCorrectVersionTable()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var parser = new Parser(new Lexer("SHOW VERSION;").Tokenize());
            var script = parser.Parse();

            await evaluator.Evaluate(script);

            var result = evaluator.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal("ETL-SQL Engine", result.Rows[0]["Component"]);
            Assert.Equal(LanguageMetadata.EngineVersion, result.Rows[0]["Version"]);
        }

        [Fact]
        public async Task AtAtVersion_ReturnsCorrectString()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var parser = new Parser(new Lexer("PRINT @@VERSION;").Tokenize());
            var script = parser.Parse();

            await evaluator.Evaluate(script);
            
            // Verification via expression evaluation directly
            var exprParser = new Parser(new Lexer("@@VERSION").Tokenize());
            var expr = exprParser.ParseExpression();
            var value = await evaluator.EvaluateValue(expr, new Row());
            
            Assert.Equal(LanguageMetadata.GetFullVersionString(), value);
        }

        [Fact]
        public async Task ScriptMetadata_IsExtractedAndDefaultAuthorSet()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            string sql = @"
/* 
   @author: Chuck 
   @version: 1.2.3 
*/
PRINT 'Test';";
            var parser = new Parser(new Lexer(sql).Tokenize());
            var script = parser.Parse();

            Assert.Equal("Chuck", script.Metadata["author"]);
            Assert.Equal("1.2.3", script.Metadata["version"]);

            await evaluator.Evaluate(script);

            // Verify LineageTracker has the metadata
            Assert.Equal("Chuck", evaluator.LineageTracker.GlobalMetadata["author"]);
            Assert.Equal("1.2.3", evaluator.LineageTracker.GlobalMetadata["version"]);
            Assert.Equal(LanguageMetadata.EngineVersion, evaluator.LineageTracker.GlobalMetadata["engine_version"]);
        }

        [Fact]
        public async Task MissingAuthor_DefaultsToSystemUser()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var parser = new Parser(new Lexer("PRINT 'Test';").Tokenize());
            var script = parser.Parse();

            await evaluator.Evaluate(script);

            Assert.Equal(Environment.UserName, evaluator.LineageTracker.GlobalMetadata["author"]);
        }
    }
}
