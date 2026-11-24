using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public sealed class TransformTr08Tests
{
    private static async Task<Evaluator> RunAsync(string sql)
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
        return eval;
    }

    [Fact]
    public async Task TransformImpute_MedianConstantAndWhatIf_WorkAndRejectBadNumericBeforeTruncate()
    {
        var eval = await RunAsync("""
            CREATE TABLE #s (Id INT, Val DECIMAL(18, 2), Tag VARCHAR);
            INSERT INTO #s VALUES (1, 10, NULL), (2, NULL, 'ok'), (3, 20, NULL), (4, 100, 'ok');

            SET WHAT_IF ON;
            TRANSFORM #s USING IMPUTE (COLS = 'Val', METHOD = 'MEDIAN');
            SET WHAT_IF OFF;

            TRANSFORM #med FROM #s USING IMPUTE (COLS = 'Val', METHOD = 'MEDIAN');
            TRANSFORM #cst FROM #s USING IMPUTE (COLS = 'Tag', METHOD = 'CONSTANT', VALUE = 'missing');

            SELECT
                (SELECT Val FROM #s WHERE Id = 2) AS orig_v2,
                (SELECT Val FROM #med WHERE Id = 2) AS med_v2,
                (SELECT Tag FROM #cst WHERE Id = 1) AS cst_t1;
            """);

        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Null(row["orig_v2"]);
        Assert.Equal(20m, Convert.ToDecimal(row["med_v2"]));
        Assert.Equal("missing", row["cst_t1"]);

        // Non-numeric value in MEAN column must fail before truncating the target.
        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #bad (Id INT, Val VARCHAR);
            INSERT INTO #bad VALUES (1, 'not-a-number'), (2, NULL);
            TRANSFORM #out FROM #bad USING IMPUTE (COLS = 'Val', METHOD = 'MEAN');
            """));
    }

    [Fact]
    public async Task TransformSplit_JsonArrayKeepEmptyOffAndInvalidJsonFailsClosed()
    {
        var eval = await RunAsync("""
            CREATE TABLE #p (Id INT, Payload VARCHAR);
            INSERT INTO #p VALUES
                (1, '{"items":[{"k":"a"},{"k":"b"}]}'),
                (2, '{"items":[]}'),
                (3, NULL);

            TRANSFORM #flat
            FROM #p
            USING EXPLODE (
                SOURCE_COL = 'Payload',
                MODE = 'ROWS',
                JSON_PATH = '$.items',
                INDEX_COL = 'Pos',
                KEEP_EMPTY = 'OFF'
            );

            SELECT Id, Pos, k FROM #flat ORDER BY Pos;
            """);

        Assert.Equal(2, eval.LastResult!.Rows.Count);
        Assert.Equal(1m, Convert.ToDecimal(eval.LastResult.Rows[0]["Pos"]));
        Assert.Equal("a", eval.LastResult.Rows[0]["k"]);
        Assert.Equal(2m, Convert.ToDecimal(eval.LastResult.Rows[1]["Pos"]));
        Assert.Equal("b", eval.LastResult.Rows[1]["k"]);

        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #bad (Id INT, Payload VARCHAR);
            INSERT INTO #bad VALUES (1, '{not valid json');
            TRANSFORM #out FROM #bad USING SPLIT (SOURCE_COL = 'Payload', JSON_PATH = '$.items');
            """));
    }

    [Fact]
    public void TransformSchemaProjection_ProjectsImputeAndSplitColumns()
    {
        var impSql = "TRANSFORM #out FROM #src USING IMPUTE (COLS = 'Score', METHOD = 'MEAN');";
        var impStmt = (TransformStatement)new Parser(new Lexer(impSql).Tokenize()).Parse().Statements[0];
        var impCols = TransformSchemaProjection.ProjectColumns(impStmt, [new ColumnMetadata("Score", "INT")]);
        Assert.Contains(impCols, c => c.Name == "Score" && c.DataType == "DECIMAL");

        var splitSql = "TRANSFORM #out FROM #src USING SPLIT (SOURCE_COL = 'Tags', MODE = 'ROWS', OUTPUT_COL = 'Tag', INDEX_COL = 'Pos');";
        var splitStmt = (TransformStatement)new Parser(new Lexer(splitSql).Tokenize()).Parse().Statements[0];
        var splitCols = TransformSchemaProjection.ProjectColumns(splitStmt, [new ColumnMetadata("Tags", "VARCHAR")]);
        Assert.Contains(splitCols, c => c.Name == "Tag" && c.DataType == "VARCHAR");
        Assert.Contains(splitCols, c => c.Name == "Pos" && c.DataType == "INT");
    }
}
