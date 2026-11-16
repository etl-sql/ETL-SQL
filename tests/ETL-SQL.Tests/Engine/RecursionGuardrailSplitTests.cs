using System.Security;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// The CTE iteration limit (Engine:MaxRecursiveDepth, 10000) and the script/directory nesting
/// guardrail (Security:MaxRecursiveNestingDepth, 5) are separate settings. Every Evaluator used
/// to copy the CTE limit into the shared nesting guardrail, so the guardrail was 10000.
/// </summary>
public class RecursionGuardrailSplitTests
{
    private static Evaluator Eval() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();

    [Fact]
    public void ConstructingAnEvaluator_LeavesTheNestingGuardrailAlone()
    {
        var eval = Eval();
        var security = ((IExecutionContext)eval).SecurityService;

        Assert.True(eval.MaxRecursiveDepth >= 1000);
        Assert.Equal(ETL_SQL.Services.SecurityService.DefaultMaxRecursiveDepth, security.MaxRecursiveDepth);
        Assert.Equal(eval.MaxRecursiveDepth, security.MaxCteRecursionDepth);
    }

    [Fact]
    public async Task RecursiveCte_IteratesPastTheNestingLimit()
    {
        var eval = Eval();
        await eval.Evaluate(Parse(@"
WITH RECURSIVE n AS (SELECT 1 AS i UNION ALL SELECT i + 1 FROM n WHERE i < 50)
SELECT COUNT(*) AS c FROM n;"));

        Assert.Equal(50m, System.Convert.ToDecimal(eval.LastResult!.Rows[0]["c"]));
    }

    [Fact]
    public async Task NestedDynamicExec_CountsTowardTheNestingLimit()
    {
        static string Nest(int levels)
        {
            var sql = "SELECT 1 AS x;";
            for (var i = 0; i < levels; i++)
                sql = "EXEC ('" + sql.Replace("'", "''") + "');";
            return sql;
        }

        await Eval().Evaluate(Parse(Nest(3)));

        // At depth 0, EXEC re-entered Evaluate as a fresh run, so nesting was never counted.
        var ex = await Assert.ThrowsAnyAsync<System.Exception>(() => Eval().Evaluate(Parse(Nest(8))));
        Assert.Contains("Recursive operation depth", ex.ToString());
    }

    [Theory]
    [InlineData("CREATE TOOL Masker AS EXECUTABLE (COMMAND = 'python');")]
    [InlineData("DROP TOOL Masker;")]
    public async Task ToolCatalogStatements_PointAtTheAdminCommand(string sql)
    {
        var ex = await Assert.ThrowsAnyAsync<System.Exception>(() => Eval().Evaluate(Parse(sql)));
        Assert.Contains("etl-sql admin machine tool", ex.ToString());
    }
}
