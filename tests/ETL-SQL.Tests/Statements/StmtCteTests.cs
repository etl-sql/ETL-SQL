using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements.Statements
{
    public class RecursiveCteTests
    {
        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();
        private static Evaluator CreateEvaluator() => DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        [Fact]
        public async Task RecursiveCte_OrgChart_UnionAll_Works()
        {
            var script = @"
CREATE TABLE employees (id INT, name STRING, manager_id INT);
INSERT INTO employees (id, name, manager_id) VALUES (1, 'CEO', NULL);
INSERT INTO employees (id, name, manager_id) VALUES (2, 'VP Sales', 1);
INSERT INTO employees (id, name, manager_id) VALUES (3, 'VP Eng', 1);
INSERT INTO employees (id, name, manager_id) VALUES (4, 'Sales Rep', 2);

WITH RECURSIVE subordinates AS (
    SELECT id, name, manager_id, 0 AS level
    FROM employees
    WHERE manager_id IS NULL
    UNION ALL
    SELECT e.id, e.name, e.manager_id, s.level + 1
    FROM employees e
    JOIN subordinates s ON e.manager_id = s.id
)
SELECT id, name, level FROM subordinates ORDER BY level, id;
";
            var evaluator = CreateEvaluator();
            await evaluator.Evaluate(Parse(script));

            var result = evaluator.LastResult;
            Assert.NotNull(result);
            Assert.Equal(4, result.Rows.Count);

            // Level 0: CEO
            Assert.Equal("CEO", result.Rows[0]["name"]);
            Assert.Equal(0, Convert.ToInt32(result.Rows[0]["level"]));

            // Level 1: VP Sales, VP Eng
            Assert.Equal(1, Convert.ToInt32(result.Rows[1]["level"]));
            Assert.Equal(1, Convert.ToInt32(result.Rows[2]["level"]));

            // Level 2: Sales Rep
            Assert.Equal("Sales Rep", result.Rows[3]["name"]);
            Assert.Equal(2, Convert.ToInt32(result.Rows[3]["level"]));
            Assert.Equal(0, evaluator.CurrentRecursiveDepth);
        }

        [Fact]
        public async Task RecursiveCte_Union_StopsOnDuplicate_Works()
        {
            var script = @"
CREATE TABLE links (src INT, dest INT);
INSERT INTO links VALUES (1, 2);
INSERT INTO links VALUES (2, 3);
INSERT INTO links VALUES (3, 1);

WITH RECURSIVE reachable AS (
    SELECT 1 AS node
    UNION
    SELECT dest FROM links l JOIN reachable r ON l.src = r.node
)
SELECT node FROM reachable ORDER BY node;
";
            var evaluator = CreateEvaluator();
            await evaluator.Evaluate(Parse(script));

            var result = evaluator.LastResult;
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count); // Should only have nodes 1, 2, 3
            Assert.Equal(1, Convert.ToInt32(result.Rows[0]["node"]));
            Assert.Equal(2, Convert.ToInt32(result.Rows[1]["node"]));
            Assert.Equal(3, Convert.ToInt32(result.Rows[2]["node"]));
        }

        [Fact]
        public async Task RecursiveCte_MaxDepth_ThrowsException()
        {
            var script = @"
WITH RECURSIVE infinite AS (
    SELECT 1 AS n
    UNION ALL
    SELECT n + 1 FROM infinite
)
SELECT * FROM infinite;
";
            var evaluator = CreateEvaluator();
            evaluator.MaxRecursiveDepth = 5; // Low limit for test

            var ex = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => evaluator.Evaluate(Parse(script)));
            Assert.Contains("maximum recursion", ex.Message);
            Assert.Equal(0, evaluator.CurrentRecursiveDepth);
        }
    }
}
