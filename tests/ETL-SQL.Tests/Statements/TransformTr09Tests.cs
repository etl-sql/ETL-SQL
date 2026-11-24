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

public sealed class TransformTr09Tests
{
    private static async Task<Evaluator> RunAsync(string sql)
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
        return eval;
    }

    [Fact]
    public async Task TransformMaskPii_HashPerturbWhatIf_AndNoRawValueLeakInError()
    {
        var eval = await RunAsync("""
            CREATE TABLE #s (Id INT, Email VARCHAR, Dob DATE);
            INSERT INTO #s VALUES
                (1, 'alice.secret@corp.com', '1990-06-15'),
                (2, NULL, NULL);

            SET WHAT_IF ON;
            TRANSFORM #s USING MASK_PII (COLS = 'Email', METHOD = 'HASH_SHA256', SALT = 'pepper');
            SET WHAT_IF OFF;

            TRANSFORM #hashed FROM #s USING MASK_PII (COLS = 'Email', METHOD = 'HASH_SHA256', SALT = 'pepper');
            TRANSFORM #perturbed FROM #s USING MASK_PII (COLS = 'Dob', METHOD = 'PERTURB_DATE', MAX_DAYS = 10, SEED = 7);

            SELECT
                (SELECT Email FROM #s WHERE Id = 1) AS orig_email,
                (SELECT Email FROM #hashed WHERE Id = 1) AS hash_email,
                (SELECT Email FROM #hashed WHERE Id = 2) AS hash_null,
                (SELECT CAST(Dob AS STRING) FROM #perturbed WHERE Id = 1) AS p_dob,
                (SELECT Dob FROM #perturbed WHERE Id = 2) AS p_null;
            """);

        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("alice.secret@corp.com", row["orig_email"]);
        var hash = Assert.IsType<string>(row["hash_email"]);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain("alice.secret", hash);
        Assert.Null(row["hash_null"]);
        Assert.NotEqual("1990-06-15", row["p_dob"]);
        Assert.Null(row["p_null"]);

        // Invalid date in PERTURB_DATE must fail WITHOUT echoing the raw PII value in the error!
        var ex = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #bad (Id INT, Dob VARCHAR);
            INSERT INTO #bad VALUES (1, 'SENSITIVE-RAW-TOKEN-999');
            TRANSFORM #out FROM #bad USING MASK_PII (COLS = 'Dob', METHOD = 'PERTURB_DATE');
            """));
        Assert.DoesNotContain("SENSITIVE-RAW-TOKEN-999", ex.Message);
    }

    [Fact]
    public async Task TransformScd2_WhatIfAndDuplicateKeyRejection_PreserveTarget()
    {
        var eval = await RunAsync("""
            CREATE TABLE #d1 (Id INT, Tier VARCHAR);
            INSERT INTO #d1 VALUES (1, 'Silver');
            TRANSFORM #dim FROM #d1 USING SCD2 (KEY_COLS = 'Id', TRACK_COLS = 'Tier', EFFECTIVE_DATE = '2026-01-01');

            CREATE TABLE #d2 (Id INT, Tier VARCHAR);
            INSERT INTO #d2 VALUES (1, 'Gold');

            SET WHAT_IF ON;
            TRANSFORM #dim FROM #d2 USING SCD2 (KEY_COLS = 'Id', TRACK_COLS = 'Tier', EFFECTIVE_DATE = '2026-02-01');
            SET WHAT_IF OFF;

            SELECT COUNT(*) AS cnt FROM #dim;
            """);

        Assert.Equal(1m, Convert.ToDecimal(Assert.Single(eval.LastResult!.Rows)["cnt"]));

        // Duplicate keys in incoming snapshot must fail before touching the target.
        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #dup (Id INT, Tier VARCHAR);
            INSERT INTO #dup VALUES (1, 'A'), (1, 'B');
            TRANSFORM #dim FROM #dup USING SCD2 (KEY_COLS = 'Id', TRACK_COLS = 'Tier');
            """));
    }

    [Fact]
    public void TransformSchemaProjection_ProjectsMaskPiiAndScd2Columns()
    {
        var maskSql = "TRANSFORM #out FROM #src USING MASK_PII (COLS = 'Ssn', METHOD = 'HASH_SHA256');";
        var maskStmt = (TransformStatement)new Parser(new Lexer(maskSql).Tokenize()).Parse().Statements[0];
        var maskCols = TransformSchemaProjection.ProjectColumns(maskStmt, [new ColumnMetadata("Ssn", "INT")]);
        Assert.Contains(maskCols, c => c.Name == "Ssn" && c.DataType == "VARCHAR");

        var scdSql = "TRANSFORM #dim FROM #src USING SCD2 (KEY_COLS = 'Id', TRACK_COLS = 'Tier');";
        var scdStmt = (TransformStatement)new Parser(new Lexer(scdSql).Tokenize()).Parse().Statements[0];
        var scdCols = TransformSchemaProjection.ProjectColumns(scdStmt, [new ColumnMetadata("Id", "INT"), new ColumnMetadata("Tier", "VARCHAR")]);
        Assert.Contains(scdCols, c => c.Name == "EffectiveFrom" && c.DataType == "DATE");
        Assert.Contains(scdCols, c => c.Name == "EffectiveTo" && c.DataType == "DATE");
        Assert.Contains(scdCols, c => c.Name == "IsCurrent" && c.DataType == "INT");
    }

    [Fact]
    public async Task TransformMaskPii_HashRequiresResolvedKeyAndIsKeyed()
    {
        // No key: an unkeyed SHA-256 of an SSN is reversible by hashing all SSNs.
        var noKey = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #p (Ssn VARCHAR);
            INSERT INTO #p VALUES ('123-45-6789');
            TRANSFORM #out FROM #p USING MASK_PII (COLS = 'Ssn', METHOD = 'HASH_SHA256');
            """));
        Assert.Contains("requires SALT", noKey.Message);

        // SECRET: is not resolved in TRANSFORM options; it must not become a known literal key.
        var unresolved = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync("""
            CREATE TABLE #p (Ssn VARCHAR);
            INSERT INTO #p VALUES ('123-45-6789');
            TRANSFORM #out FROM #p USING MASK_PII (COLS = 'Ssn', METHOD = 'HASH_SHA256', SALT = 'SECRET:pii/hash_key');
            """));
        Assert.Contains("unresolved secret reference", unresolved.Message);

        var eval = await RunAsync("""
            CREATE TABLE #p (Ssn VARCHAR);
            INSERT INTO #p VALUES ('123-45-6789');
            TRANSFORM #a FROM #p USING MASK_PII (COLS = 'Ssn', METHOD = 'HASH_SHA256', SALT = 'key-one');
            TRANSFORM #b FROM #p USING MASK_PII (COLS = 'Ssn', METHOD = 'HASH_SHA256', SALT = 'key-two');
            SELECT (SELECT Ssn FROM #a) AS a, (SELECT Ssn FROM #b) AS b;
            """);
        var row = Assert.Single(eval.LastResult!.Rows);
        var expected = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("key-one"), System.Text.Encoding.UTF8.GetBytes("123-45-6789"))).ToLowerInvariant();
        Assert.Equal(expected, row["a"]);
        Assert.NotEqual(row["a"], row["b"]);
    }

    [Fact]
    public async Task TransformScd2_TargetColumnNotInSource_FailsAndKeepsHistory()
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await eval.Evaluate(new Parser(new Lexer("""
            CREATE TABLE #d1 (Id INT, Tier VARCHAR);
            INSERT INTO #d1 VALUES (1, 'Silver');
            TRANSFORM #dim FROM #d1 USING SCD2 (KEY_COLS = 'Id', TRACK_COLS = 'Tier', EFFECTIVE_DATE = '2026-01-01');
            ALTER TABLE #dim ADD SurrogateKey INT;
            UPDATE #dim SET SurrogateKey = 1001;
            CREATE TABLE #d2 (Id INT, Tier VARCHAR);
            INSERT INTO #d2 VALUES (1, 'Gold');
            """).Tokenize()).Parse());

        // Rewriting #dim from #d2 would drop SurrogateKey; it must refuse before emptying the target.
        var ex = await Assert.ThrowsAsync<ExecutionException>(() => eval.Evaluate(new Parser(new Lexer(
            "TRANSFORM #dim FROM #d2 USING SCD2 (KEY_COLS = 'Id', TRACK_COLS = 'Tier', EFFECTIVE_DATE = '2026-02-01');").Tokenize()).Parse()));
        Assert.Contains("SurrogateKey", ex.Message);

        await eval.Evaluate(new Parser(new Lexer("SELECT Tier, SurrogateKey FROM #dim;").Tokenize()).Parse());
        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("Silver", row["Tier"]);
        Assert.Equal(1001m, Convert.ToDecimal(row["SurrogateKey"]));
    }
}
