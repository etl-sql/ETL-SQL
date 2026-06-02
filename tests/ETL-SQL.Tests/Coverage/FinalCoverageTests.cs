using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.App;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Coverage
{
    /// <summary>
    /// Final coverage push targeting AvoidSelectStarRule, SafeDeleteUpdateRule,
    /// UndeclaredVariableRule, DialectKeywordRule, AlterTableStatementHandler,
    /// FileOperationStatementHandler, DirectoryOperationStatementHandler,
    /// and ClearSessionStatementHandler modes.
    /// </summary>
    public class FinalCoverageTests
    {
        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task<IList<LintResult>> Lint(ILintRule rule, string sql,
            ILintContext? ctx = null)
        {
            ctx ??= new DefaultLintContext();
            return (await rule.AnalyzeAsync(Parse(sql), ctx)).ToList();
        }

        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static async Task Run(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
        }

        private static async Task<Evaluator> RunAndGet(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        // Ensures ConnectorRegistry.Instance is non-null so DialectKeywordRule is exercised.
        private static void EnsureRegistry()
        {
            if (ConnectorRegistry.Instance == null)
                _ = new ConnectorRegistry(new IConnector[] { new PostgresConnector() });
        }

        // ── AvoidSelectStarRule ───────────────────────────────────────────────

        [Fact]
        public async Task AvoidStar_TopLevel_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(), "SELECT * FROM #t;");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.RuleName == "AvoidSelectStar");
        }

        [Fact]
        public async Task AvoidStar_ExplicitColumns_NoWarning()
        {
            var results = await Lint(new AvoidSelectStarRule(), "SELECT id, name FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AvoidStar_InSubqueryFrom_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "SELECT sub.id FROM (SELECT * FROM #t) AS sub;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InJoinSubquery_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "SELECT a.id FROM #t AS a INNER JOIN (SELECT * FROM #s) AS b ON a.id = b.id;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_UnionBothSides_TwoWarnings()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "SELECT * FROM #t UNION ALL SELECT * FROM #s;");
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task AvoidStar_UnionOneStar_OneWarning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "SELECT * FROM #t UNION ALL SELECT id FROM #s;");
            Assert.Single(results);
        }

        [Fact]
        public async Task AvoidStar_InsideForBody_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "FOR @i = 1 TO 3 BEGIN SELECT * FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InsideForeachBody_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "FOREACH @row IN (SELECT id FROM #t) BEGIN SELECT * FROM #items; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InsideTryCatch_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "BEGIN TRY SELECT * FROM #t; END TRY BEGIN CATCH SELECT 1 AS n; END CATCH");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InsideWhile_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "WHILE 1 = 1 BEGIN SELECT * FROM #t; BREAK; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InsideIfBody_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "IF 1 = 1 BEGIN SELECT * FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InsideElse_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "IF 1 = 0 BEGIN SELECT 1 AS n; END ELSE BEGIN SELECT * FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InsertSelect_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "INSERT INTO #dest SELECT * FROM #src;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AvoidStar_InsideElseIfBody_Warning()
        {
            var results = await Lint(new AvoidSelectStarRule(),
                "IF 1 = 0 BEGIN SELECT 1 AS n; END ELSE IF 1 = 1 BEGIN SELECT * FROM #t; END");
            Assert.NotEmpty(results);
        }

        // ── SafeDeleteUpdateRule ──────────────────────────────────────────────

        [Fact]
        public async Task SafeDelete_WithWhere_NoWarning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(), "DELETE FROM #t WHERE id = 1;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task SafeUpdate_WithWhere_NoWarning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(), "UPDATE #t SET name = 'x' WHERE id = 1;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task SafeDelete_NoWhere_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(), "DELETE FROM #t;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task SafeUpdate_NoWhere_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(), "UPDATE #t SET name = 'x';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task SafeDelete_InsideForBody_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(),
                "FOR @i = 1 TO 3 BEGIN DELETE FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task SafeUpdate_InsideForeachBody_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(),
                "FOREACH @r IN (SELECT id FROM #t) BEGIN UPDATE #t SET val = 0; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task SafeDelete_InsideTryCatch_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(),
                "BEGIN TRY DELETE FROM #t; END TRY BEGIN CATCH SELECT 1 AS n; END CATCH");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task SafeUpdate_InsideWhile_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(),
                "WHILE 1 = 1 BEGIN UPDATE #t SET v = 0; BREAK; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task SafeDelete_InsideElseIf_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(),
                "IF 1 = 0 BEGIN SELECT 1 AS n; END ELSE IF 1 = 1 BEGIN DELETE FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task SafeDelete_InsideElse_Warning()
        {
            var results = await Lint(new SafeDeleteUpdateRule(),
                "IF 1 = 0 BEGIN SELECT 1 AS n; END ELSE BEGIN DELETE FROM #t; END");
            Assert.NotEmpty(results);
        }

        // ── UndeclaredVariableRule ────────────────────────────────────────────

        [Fact]
        public async Task Undeclared_DeclaredVar_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @x INT = 5; SELECT @x AS n;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_NotDeclared_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(), "SELECT @x AS n;");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.Message.Contains("@x"));
        }

        [Fact]
        public async Task Undeclared_SystemVar_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(), "SELECT @@ROWCOUNT AS n;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_ForLoopVar_UsableInBody()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "FOR @i = 1 TO 5 BEGIN SELECT @i AS n; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_ForLoopBody_UndeclaredVar_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "FOR @i = 1 TO 5 BEGIN SELECT @undeclared AS n; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_ForLoopWithStep_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "FOR @i = 0 TO 10 STEP 2 BEGIN SELECT @i AS n; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_ParallelForLoopVar_UsableInBody()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "PARALLEL FOR @i = 1 TO 5 BEGIN SELECT @i AS n; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_ForeachVar_UsableInBody()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "FOREACH @row IN (SELECT 1 AS x) BEGIN SELECT @row AS n; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_PrintDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @msg VARCHAR = 'hello'; PRINT @msg;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_PrintUndeclared_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(), "PRINT @notdeclared;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_RaiseErrorUndeclared_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(), "RAISERROR(@msg, 16, 1);");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_ThrowDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @n INT = 50001; DECLARE @m VARCHAR = 'err'; DECLARE @s INT = 1;" +
                " THROW @n, @m, @s;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_TryCatch_UndeclaredInTry_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "BEGIN TRY SELECT @notdeclared AS n; END TRY BEGIN CATCH SELECT 1 AS n; END CATCH");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_ReturnDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @n INT = 5; RETURN @n;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_ProcedureParamUsableInBody()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "CREATE PROCEDURE myproc (@p INT) AS BEGIN SELECT @p AS result; END;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_FunctionParamUsableInBody()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "CREATE FUNCTION myfunc (@x INT) RETURNS INT AS BEGIN RETURN @x + 1; END;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_AssertDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @cond INT = 1; ASSERT @cond = 1;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_WaitForLiteral_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "WAITFOR DELAY '00:00:01';");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_InsertValuesDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @n INT = 1; INSERT INTO #t (id) VALUES (@n);");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_InsertValuesUndeclared_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "INSERT INTO #t (id) VALUES (@notdeclared);");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_JoinConditionDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @x INT = 1; SELECT a.id FROM #a AS a INNER JOIN #b AS b ON a.id = @x;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_SubqueryExprDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @x INT = 5; SELECT (SELECT @x) AS n;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_DeleteWhereDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @id INT = 1; DELETE FROM #t WHERE id = @id;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_UpdateSetUndeclared_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "UPDATE #t SET val = @notdeclared;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_SetVarUndeclaredRight_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @x INT = 0; SET @x = @notdeclared;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_WhileConditionDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @n INT = 0; WHILE @n < 3 BEGIN SET @n = @n + 1; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Undeclared_IfConditionUndeclared_Warning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "IF @notdeclared = 1 BEGIN SELECT 1 AS n; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task Undeclared_BinaryExprDeclared_NoWarning()
        {
            var results = await Lint(new UndeclaredVariableRule(),
                "DECLARE @a INT = 1; DECLARE @b INT = 2; SELECT @a + @b AS n;");
            Assert.Empty(results);
        }

        // ── DialectKeywordRule ────────────────────────────────────────────────

        [Fact]
        public async Task Dialect_PostgresConn_TopUsed_Warning()
        {
            EnsureRegistry();
            var rule = new DialectKeywordRule();
            var results = await Lint(rule,
                "CREATE CONNECTION pgconn AS POSTGRES('server=localhost;database=test');" +
                "SELECT TOP 10 id FROM pgconn.Orders;");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.Message.Contains("TOP"));
        }

        [Fact]
        public async Task Dialect_PostgresConn_ValidSql_NoWarning()
        {
            EnsureRegistry();
            var rule = new DialectKeywordRule();
            var results = await Lint(rule,
                "CREATE CONNECTION pgconn AS POSTGRES('server=localhost;database=test');" +
                "SELECT id, name FROM pgconn.Orders WHERE id = 1;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Dialect_NoConnectionRef_NoWarning()
        {
            EnsureRegistry();
            var rule = new DialectKeywordRule();
            // SELECT from local temp table — no connection reference → no dialect check
            var results = await Lint(rule, "SELECT TOP 10 id FROM #local;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Dialect_UnknownConn_NoWarning()
        {
            EnsureRegistry();
            var rule = new DialectKeywordRule();
            // Connection not in script or metadata → no exclusions found
            var results = await Lint(rule, "SELECT id FROM unknownconn.Orders;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Dialect_MockDbConn_LimitUsed_Warning()
        {
            EnsureRegistry();
            // MockDb is a test-only connector absent from the production registry, and
            // EnsureRegistry only seeds Postgres. The rule reads the shared global
            // ConnectorRegistry.Instance, so additively register MockDb on whatever
            // registry is current — this makes the test independent of suite ordering
            // (it previously passed only when an earlier test left MockDb registered).
            ConnectorRegistry.Instance!.Register(new MockDbConnector());
            var rule = new DialectKeywordRule();
            // MOCKDB excludes LIMIT
            var results = await Lint(rule,
                "CREATE CONNECTION mockconn AS MOCKDB();" +
                "SELECT id FROM mockconn.Orders LIMIT 10;");
            Assert.NotEmpty(results);
        }

        // ── AlterTableStatementHandler ────────────────────────────────────────

        [Fact]
        public async Task AlterTable_AddColumn_Succeeds()
        {
            await Run(
                "CREATE TABLE #t (Id INT);" +
                "ALTER TABLE #t ADD newcol VARCHAR;");
        }

        [Fact]
        public async Task AlterTable_AddColumn_ColumnIsVisible()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #t (Id INT);" +
                "ALTER TABLE #t ADD label VARCHAR;" +
                "SHOW COLUMNS FOR #t;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task AlterTable_DropColumn_Succeeds()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #t (Id INT, Name VARCHAR);" +
                "ALTER TABLE #t DROP COLUMN Name;" +
                "SHOW COLUMNS FOR #t;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task AlterTable_RenameColumn_Succeeds()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #t (Id INT, OldName VARCHAR);" +
                "ALTER TABLE #t RENAME COLUMN OldName TO NewName;" +
                "SHOW COLUMNS FOR #t;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task AlterTable_AddThenSelectNewColumn_Works()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #tcol (Id INT);" +
                "INSERT INTO #tcol VALUES (1);" +
                "ALTER TABLE #tcol ADD score INT;" +
                "SELECT Id, score FROM #tcol;");
            Assert.NotNull(eval.LastResult);
        }

        // ── FileOperationStatementHandler ─────────────────────────────────────

        [Fact]
        public async Task DeleteFile_IfExists_NonexistentFile_NoError()
        {
            var path = Path.Combine(Path.GetTempPath(), "del_" + Guid.NewGuid().ToString("N") + ".txt");
            var escaped = path.Replace("\\", "\\\\");
            // DELETE FILE IF EXISTS syntax: IF EXISTS comes before the path
            await Run($"DELETE FILE IF EXISTS '{escaped}';");
        }

        [Fact]
        public async Task DeleteFile_ExistingFile_Deletes()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "test content");
            var escaped = path.Replace("\\", "\\\\");
            try
            {
                await Run($"DELETE FILE '{escaped}';");
                Assert.False(File.Exists(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public async Task CopyFile_SourceToDestination_Succeeds()
        {
            var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(src, "copy test");
            try
            {
                var srcE = src.Replace("\\", "\\\\");
                var dstE = dst.Replace("\\", "\\\\");
                await Run($"COPY FILE '{srcE}' TO '{dstE}';");
                Assert.True(File.Exists(dst));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task MoveFile_SourceToDestination_Succeeds()
        {
            var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(src, "move test");
            try
            {
                var srcE = src.Replace("\\", "\\\\");
                var dstE = dst.Replace("\\", "\\\\");
                await Run($"MOVE FILE '{srcE}' TO '{dstE}';");
                Assert.False(File.Exists(src));
                Assert.True(File.Exists(dst));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task CompressFile_SourceToZip_Succeeds()
        {
            var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
            var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            File.WriteAllText(src, "compress test");
            try
            {
                var srcE = src.Replace("\\", "\\\\");
                var dstE = dst.Replace("\\", "\\\\");
                await Run($"COMPRESS FILE '{srcE}' TO '{dstE}';");
                Assert.True(File.Exists(dst));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        // ── DirectoryOperationStatementHandler ────────────────────────────────

        [Fact]
        public async Task CreateDirectory_NewDir_Succeeds()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etltest_" + Guid.NewGuid().ToString("N"));
            var escaped = dir.Replace("\\", "\\\\");
            try
            {
                await Run($"CREATE_DIRECTORY('{escaped}');");
                Assert.True(Directory.Exists(dir));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir); }
        }

        [Fact]
        public async Task DeleteDirectory_IfExists_NonexistentDir_NoError()
        {
            var dir = Path.Combine(Path.GetTempPath(), "del_" + Guid.NewGuid().ToString("N"));
            var escaped = dir.Replace("\\", "\\\\");
            // DELETE DIRECTORY IF EXISTS syntax: IF EXISTS comes before the path
            await Run($"DELETE DIRECTORY IF EXISTS '{escaped}';");
        }

        [Fact]
        public async Task DeleteDirectory_ExistingDir_Deletes()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etltest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var escaped = dir.Replace("\\", "\\\\");
            try
            {
                await Run($"DELETE DIRECTORY '{escaped}';");
                Assert.False(Directory.Exists(dir));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        // ── ClearSessionStatementHandler modes ────────────────────────────────

        [Fact]
        public async Task ClearSession_All_NoError()
        {
            await Run("CLEAR SESSION ALL;");
        }

        [Fact]
        public async Task ClearSession_Stale_NoError()
        {
            await Run("CLEAR SESSION STALE;");
        }

        [Fact]
        public async Task ClearSession_Single_NonexistentId_NoError()
        {
            await Run("CLEAR SESSION 'nonexistent_session_id_xyz';");
        }

        [Fact]
        public async Task ClearSessions_All_Plural_NoError()
        {
            await Run("CLEAR SESSIONS ALL;");
        }
    }
}
