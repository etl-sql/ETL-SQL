using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Engines;

namespace ETL_SQL.Tests.Operations.Operations
{
    public class ExternalSortTests
    {
        private (Evaluator eval, ETL_SQL.Common.ILogger logger) BuildContext(int chunkSize = 100000)
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();
            eval.ExternalSortChunkSize = chunkSize;
            var logger = sp.GetRequiredService<ETL_SQL.Common.ILogger>();
            return (eval, logger);
        }

        [Fact]
        public async Task SortExternal_AcrossMultipleChunks_ProducesCorrectOrder()
        {
            // Forces 4 chunks: [10, 9], [8, 7], [6, 5], [4, 3, 2, 1] -> wait no, 2 per chunk
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>();
            for (int i = 10; i >= 1; i--)
            {
                rows.Add(new Row { ["id"] = i, ["val"] = $"val-{i}" });
            }

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), false) // ASC
            };

            var startSpill = eval.Telemetry.TotalSpilledBytes;
            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(10, sorted.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal((decimal)(i + 1), Convert.ToDecimal(sorted[i]["id"]));
            }

            Assert.True(eval.Telemetry.TotalSpilledBytes > startSpill, "Should have reported spilled bytes to context");
        }

        [Fact]
        public async Task SortExternal_DescendingOrder_ProducesCorrectOrder()
        {
            var (eval, logger) = BuildContext(chunkSize: 5);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>();
            for (int i = 1; i <= 10; i++)
            {
                rows.Add(new Row { ["id"] = i });
            }

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("id"), true) // DESC
            };

            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(10, sorted.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal((decimal)(10 - i), Convert.ToDecimal(sorted[i]["id"]));
            }
        }

        [Fact]
        public async Task SortExternal_MultiColumnSort_ProducesCorrectOrder()
        {
            var (eval, logger) = BuildContext(chunkSize: 2);
            var engine = new ExternalSortEngine(eval, logger);

            var rows = new List<Row>
            {
                new Row { ["cat"] = "B", ["val"] = 1m },
                new Row { ["cat"] = "A", ["val"] = 2m },
                new Row { ["cat"] = "A", ["val"] = 1m },
                new Row { ["cat"] = "B", ["val"] = 2m },
            };

            var orderBy = new List<OrderByClause>
            {
                new OrderByClause(new IdentifierExpression("cat"), false), // cat ASC
                new OrderByClause(new IdentifierExpression("val"), true)   // val DESC
            };

            var sorted = await engine.SortExternal(rows, orderBy);

            Assert.Equal(4, sorted.Count);
            
            // Expected: A/2, A/1, B/2, B/1
            Assert.Equal("A", sorted[0]["cat"]); Assert.Equal(2m, Convert.ToDecimal(sorted[0]["val"]));
            Assert.Equal("A", sorted[1]["cat"]); Assert.Equal(1m, Convert.ToDecimal(sorted[1]["val"]));
            Assert.Equal("B", sorted[2]["cat"]); Assert.Equal(2m, Convert.ToDecimal(sorted[2]["val"]));
            Assert.Equal("B", sorted[3]["cat"]); Assert.Equal(1m, Convert.ToDecimal(sorted[3]["val"]));
        }
    }
}
