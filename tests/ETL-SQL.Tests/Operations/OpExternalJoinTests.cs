using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Operations.Operations
{
    public class ExternalJoinTests
    {
        private (Evaluator eval, ETL_SQL.Common.ILogger logger) BuildContext(int partitions = 4)
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            eval.ExternalHashPartitions = partitions;
            var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
            return (eval, logger);
        }

        [Fact]
        public async Task ApplyHashJoinExternal_CorrectlyPartitionsAndJoins()
        {
            var (eval, logger) = BuildContext(partitions: 4);
            var engine = new ExternalJoinEngine(eval, logger);

            var leftRows = new List<Row>();
            for (int i = 0; i < 20; i++) leftRows.Add(new Row { ["id"] = i, ["lval"] = $"l-{i}" });

            var rightRows = new List<Row>();
            for (int i = 0; i < 20; i += 2) rightRows.Add(new Row { ["id"] = i, ["rval"] = $"r-{i}" });

            var join = new JoinClause(
                "INNER",
                new TableReference("right"),
                new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id"))
            );

            var startSpill = eval.Telemetry.TotalSpilledBytes;
            var startParts = eval.Telemetry.PartitionsCount;

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(),
                rightRows.ToAsyncEnumerable(),
                join,
                new List<string> { "id" },
                new List<string> { "id" }).ToListAsync();

            Assert.Equal(10, results.Count);
            Assert.All(results, r => Assert.Equal(r["lval"]?.ToString().Replace("l-", ""), r["rval"]?.ToString().Replace("r-", "")));

            Assert.True(eval.Telemetry.TotalSpilledBytes > startSpill, "Should have reported spilled bytes");
            Assert.True(eval.Telemetry.PartitionsCount > startParts, "Should have reported used partition count");
        }

        [Fact]
        public async Task ApplyHashJoinExternal_LeftJoin_PreservesUnmatchedLeftRows()
        {
            var (eval, logger) = BuildContext(partitions: 2);
            var engine = new ExternalJoinEngine(eval, logger);

            var leftRows = new List<Row> { new Row { ["id"] = 1 }, new Row { ["id"] = 2 } };
            var rightRows = new List<Row> { new Row { ["id"] = 1 } };

            var join = new JoinClause(
                "LEFT",
                new TableReference("right"),
                new BinaryExpression(new IdentifierExpression("id"), TokenType.EQUALS, new IdentifierExpression("id"))
            );

            var results = await engine.ApplyHashJoinExternal(
                leftRows.ToAsyncEnumerable(),
                rightRows.ToAsyncEnumerable(),
                join,
                new List<string> { "id" },
                new List<string> { "id" }).ToListAsync();

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => Convert.ToInt32(r["id"]) == 2);
        }
    }
}
