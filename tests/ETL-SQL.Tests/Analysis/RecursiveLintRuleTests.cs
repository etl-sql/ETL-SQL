using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Analysis
{
    public class RecursiveLintRuleTests
    {
        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task<IList<LintResult>> Lint(ILintRule rule, string sql,
            ILintContext? ctx = null)
        {
            ctx ??= new DefaultLintContext();
            var results = await rule.AnalyzeAsync(Parse(sql), ctx);
            return results.ToList();
        }

        [Fact]
        public async Task VisualSourceExists_DatasetInsideIf_NoWarning()
        {
            var rule = new VisualSourceExistsRule();
            var sql = @"
                IF 1 = 1
                BEGIN
                    CREATE DATASET #nested_sales AS (SELECT 1 AS v);
                END
                CREATE VISUAL mybar AS BAR (SOURCE = #nested_sales, MAPPINGS (X = v, Y = v));
            ";
            var results = await Lint(rule, sql);
            Assert.Empty(results);
        }

        [Fact]
        public async Task LayerOrderRule_DatasetInsideIf_NoWarning()
        {
            var rule = new LayerOrderRule();
            var sql = @"
                IF 1 = 1
                BEGIN
                    CREATE DATASET #nested_sales AS (SELECT 1 AS v);
                END
                CREATE VISUAL mybar AS BAR (SOURCE = #nested_sales, MAPPINGS (X = v, Y = v));
            ";
            var results = await Lint(rule, sql);
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualSourceExists_DatasetInsideWhile_NoWarning()
        {
            var rule = new VisualSourceExistsRule();
            var sql = @"
                WHILE 1 = 0
                BEGIN
                    CREATE DATASET #loop_sales AS (SELECT 1 AS v);
                END
                CREATE VISUAL mybar AS BAR (SOURCE = #loop_sales, MAPPINGS (X = v, Y = v));
            ";
            var results = await Lint(rule, sql);
            Assert.Empty(results);
        }
        
        [Fact]
        public async Task VisualSourceExists_UnionSource_NoWarning()
        {
            var rule = new VisualSourceExistsRule();
            var sql = @"
                SELECT 1 AS v INTO #part1;
                SELECT 2 AS v INTO #part2;
                SELECT * INTO #combined FROM #part1 UNION SELECT * FROM #part2;
                CREATE VISUAL mybar AS BAR (SOURCE = #combined, MAPPINGS (X = v, Y = v));
            ";
            var results = await Lint(rule, sql);
            Assert.Empty(results);
        }
    }
}
