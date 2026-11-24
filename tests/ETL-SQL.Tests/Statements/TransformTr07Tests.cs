using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public sealed class TransformTr07Tests
{
    private static async Task<Evaluator> RunAsync(string sql)
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
        return eval;
    }

    [Fact]
    public async Task TransformBin_QuantileAndByGroup_ProducesExpectedIntervalsAndPreservesNulls()
    {
        var eval = await RunAsync("""
            CREATE TABLE #s (Grp VARCHAR, Id INT, Val DECIMAL(18, 2));
            INSERT INTO #s VALUES
                ('A', 1, 0), ('A', 2, 10), ('A', 3, 20), ('A', 4, 30), ('A', 5, NULL),
                ('B', 6, 100), ('B', 7, 200);

            TRANSFORM #binned
            FROM #s
            USING BIN (
                VALUE_COL = 'Val',
                METHOD = 'QUANTILE',
                BINS = 2,
                BY_GROUP = 'Grp'
            );

            SELECT Id, Bin_Start, Bin_End, Bin_Label FROM #binned ORDER BY Id;
            """);

        Assert.Equal(7, eval.LastResult!.Rows.Count);
        Assert.Equal(0m, Convert.ToDecimal(eval.LastResult.Rows[0]["Bin_Start"]));
        Assert.Equal(15m, Convert.ToDecimal(eval.LastResult.Rows[0]["Bin_End"]));
        Assert.Equal(15m, Convert.ToDecimal(eval.LastResult.Rows[2]["Bin_Start"]));
        Assert.Equal(30m, Convert.ToDecimal(eval.LastResult.Rows[2]["Bin_End"]));
        Assert.Null(eval.LastResult.Rows[4]["Bin_Start"]);
        Assert.Null(eval.LastResult.Rows[4]["Bin_Label"]);
        Assert.Equal(100m, Convert.ToDecimal(eval.LastResult.Rows[5]["Bin_Start"]));
        Assert.Equal(150m, Convert.ToDecimal(eval.LastResult.Rows[5]["Bin_End"]));
    }

    [Fact]
    public async Task TransformOutliers_ClipNullZScorePercentileAndWhatIf_Work()
    {
        var eval = await RunAsync("""
            CREATE TABLE #s (Id INT, Val DECIMAL(18, 2));
            INSERT INTO #s VALUES (1, 10), (2, 11), (3, 12), (4, 13), (5, 100), (6, NULL);

            SET WHAT_IF ON;
            TRANSFORM #s USING OUTLIERS (VALUE_COL = 'Val', METHOD = 'IQR', ACTION = 'NULL');
            SET WHAT_IF OFF;

            TRANSFORM #nulled FROM #s USING OUTLIERS (VALUE_COL = 'Val', METHOD = 'IQR', ACTION = 'NULL');
            TRANSFORM #clipped FROM #s USING OUTLIERS (VALUE_COL = 'Val', METHOD = 'PERCENTILE', LOWER_PCT = 10, UPPER_PCT = 90, ACTION = 'CLIP');
            TRANSFORM #zflagged FROM #s USING OUTLIERS (VALUE_COL = 'Val', METHOD = 'Z_SCORE', THRESHOLD = 1.5, ACTION = 'FLAG', FLAG_COL = 'ZOut');

            SELECT
                (SELECT Val FROM #s WHERE Id = 5) AS orig_v5,
                (SELECT Val FROM #nulled WHERE Id = 5) AS null_v5,
                (SELECT Val FROM #clipped WHERE Id = 5) AS clip_v5,
                (SELECT ZOut FROM #zflagged WHERE Id = 5) AS z_v5,
                (SELECT ZOut FROM #zflagged WHERE Id = 6) AS z_null;
            """);

        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal(100m, Convert.ToDecimal(row["orig_v5"]));
        Assert.Null(row["null_v5"]);
        Assert.True(Convert.ToDecimal(row["clip_v5"]) < 100m);
        Assert.Equal(1m, Convert.ToDecimal(row["z_v5"]));
        Assert.Null(row["z_null"]);
    }

    [Fact]
    public void TransformSchemaProjection_ProjectsBinAndOutliersColumns()
    {
        var binSql = "TRANSFORM #out FROM #src USING BIN (VALUE_COL = 'Val', BINS = 4);";
        var binStmt = (TransformStatement)new Parser(new Lexer(binSql).Tokenize()).Parse().Statements[0];
        var binCols = TransformSchemaProjection.ProjectColumns(binStmt, [new ColumnMetadata("Val", "DECIMAL")]);
        Assert.Contains(binCols, c => c.Name == "Bin_Start" && c.DataType == "DECIMAL");
        Assert.Contains(binCols, c => c.Name == "Bin_End" && c.DataType == "DECIMAL");
        Assert.Contains(binCols, c => c.Name == "Bin_Label" && c.DataType == "VARCHAR");

        var outSql = "TRANSFORM #out FROM #src USING OUTLIERS (VALUE_COL = 'Val', ACTION = 'FLAG', FLAG_COL = 'Flag');";
        var outStmt = (TransformStatement)new Parser(new Lexer(outSql).Tokenize()).Parse().Statements[0];
        var outCols = TransformSchemaProjection.ProjectColumns(outStmt, [new ColumnMetadata("Val", "DECIMAL")]);
        Assert.Contains(outCols, c => c.Name == "Flag" && c.DataType == "INT");
    }

    [Fact]
    public async Task TransformBinAndOutliers_AddedColumnsAreNotIdentityAndNonNumericValuesFail()
    {
        var eval = await RunAsync("""
            CREATE TABLE #s (Id INT, Val INT NOT NULL);
            INSERT INTO #s VALUES (1, 10), (2, 11), (3, 12), (4, 13), (5, 100);
            TRANSFORM #binned FROM #s USING BIN (VALUE_COL = 'Val', BINS = 2);
            TRANSFORM #flagged FROM #s USING OUTLIERS (VALUE_COL = 'Val', ACTION = 'FLAG');
            TRANSFORM #clipped FROM #s USING OUTLIERS (VALUE_COL = 'Val', ACTION = 'CLIP');
            TRANSFORM #nulled FROM #s USING OUTLIERS (VALUE_COL = 'Val', ACTION = 'NULL');
            """);

        // ColumnDefinition's third argument is isIdentity; the added columns must be ordinary nullable columns.
        var binned = Assert.IsType<ETL_SQL.Data.InMemoryDataSource>(eval.Connections["#binned"]);
        foreach (var col in new[] { "Bin_Start", "Bin_End", "Bin_Label" })
        {
            Assert.False(binned.Schema[col].IsIdentity, col);
            Assert.True(binned.Schema[col].IsNullable, col);
        }
        var flagged = Assert.IsType<ETL_SQL.Data.InMemoryDataSource>(eval.Connections["#flagged"]);
        Assert.False(flagged.Schema["Val_IsOutlier"].IsIdentity);
        var clipped = Assert.IsType<ETL_SQL.Data.InMemoryDataSource>(eval.Connections["#clipped"]);
        Assert.False(clipped.Schema["Val"].IsIdentity);
        Assert.Equal("DECIMAL", clipped.Schema["Val"].DataType);
        Assert.False(clipped.Schema["Val"].IsNullable);
        var nulled = Assert.IsType<ETL_SQL.Data.InMemoryDataSource>(eval.Connections["#nulled"]);
        Assert.True(nulled.Schema["Val"].IsNullable);

        // A non-numeric value fails the statement instead of silently becoming NULL.
        foreach (var algo in new[] { "BIN (VALUE_COL = 'Val', BINS = 2)", "OUTLIERS (VALUE_COL = 'Val', ACTION = 'FLAG')" })
        {
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync($"""
                CREATE TABLE #t (Id INT, Val VARCHAR);
                INSERT INTO #t VALUES (1, '10'), (2, 'abc');
                TRANSFORM #out FROM #t USING {algo};
                """));
            Assert.Contains("not numeric", ex.Message);
        }
    }
}
