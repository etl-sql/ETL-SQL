using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class ParallelCommandTests
    {
        [Fact]
        public async Task Parallel_ShouldForkVariablesSafely()
        {
            // Arrange
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            // Initial variables test

            // Simple parallel block
            string script = @"
            DECLARE @x INT = 0;
            PARALLEL (2) BEGIN
                BEGIN SET @x = @x + 10; END
                BEGIN SET @x = @x + 20; END
            END
            ";

            var lexer = new Lexer(script);
            var statements = new Parser(lexer.Tokenize()).Parse().Statements;

            // Act
            foreach (var stmt in statements) await evaluator.EvaluateStatement(stmt);

            // Assert
            // The merge is deterministic and ordered by statement index, so the last statement wins.
            Assert.Equal(20L, Convert.ToInt64(evaluator.Variables["@x"]));
        }

        [Fact]
        public async Task Parallel_ShouldMergeResultsInOrder()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            // The merge order prioritizes the FIRST statement to modify a variable if multiple modify the same one.
            // Since they execute concurrently, without deterministic merging, @val could end up as 1 or 2 randomly.
            // The engine's indexing ensures branch 0 merges over branch 1's changes if both touch the same new var 
            // (or rather, branch 0 applies, then branch 1 applies sequentially, so branch 1 actually wins in traditional merge,
            // let's verify exact merge behavior: context.Merge(fork) overwrites existing keys in the parent).

            string script = @"
            DECLARE @val INT;
            PARALLEL BEGIN
                BEGIN SET @val = 1; END
                BEGIN SET @val = 2; END
            END
            ";

            var lexer = new Lexer(script);
            var statements = new Parser(lexer.Tokenize()).Parse().Statements;

            // Act
            foreach (var stmt in statements) await evaluator.EvaluateStatement(stmt);

            // Assert: Branch 1 (SET @val = 2) should merge last and win due to index ordering.
            Assert.Equal(2L, Convert.ToInt64(evaluator.Variables["@val"]));
        }

        [Fact]
        public async Task Parallel_ShouldMergeOnlyChangedVariables()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            string script = @"
            DECLARE @x INT = 0;
            DECLARE @y INT = 0;
            PARALLEL BEGIN
                BEGIN SET @x = 10; END
                BEGIN SET @y = 20; END
            END
            ";

            var lexer = new Lexer(script);
            var statements = new Parser(lexer.Tokenize()).Parse().Statements;

            foreach (var stmt in statements) await evaluator.EvaluateStatement(stmt);

            Assert.Equal(10L, Convert.ToInt64(evaluator.Variables["@x"]));
            Assert.Equal(20L, Convert.ToInt64(evaluator.Variables["@y"]));
        }

        [Fact]
        public async Task Parallel_ShouldBubbleExceptions()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            string script = @"
            DECLARE @a INT;
            PARALLEL BEGIN
                BEGIN SET @a = 1; END
                BEGIN THROW 'Test Error'; END
            END
            ";

            var lexer = new Lexer(script);
            var statements = new Parser(lexer.Tokenize()).Parse().Statements;

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
            {
                foreach (var stmt in statements) await evaluator.EvaluateStatement(stmt);
            });

            Assert.Contains("Test Error", ex.Message);
        }
    }
}
