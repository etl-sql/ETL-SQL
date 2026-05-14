using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Coverage
{
    /// <summary>
    /// Targets remaining low-coverage rules and handlers to push line coverage past 70%.
    /// Covers: BeginEndBalanceRule (FOREACH/CreateFunction), ConnectionAuthConflictRule (recursion),
    /// AggregateWithoutGroupByRule (SetOp/IF/FOR/FOREACH/TryCatch), CredentialLeakRule (email/exec/blocks),
    /// FileSystemSecurityRule (BulkInsert/FOR/FOREACH/PARALLEL), FlatFileDelimiterConflictRule (BulkInsert/loops),
    /// AbsolutePathRule (BulkInsert/loops), PushdownValidationRule (FOR/FOREACH),
    /// SetSecurityOverrideStatementHandler (FileTypeExtension/LargeFileCount/DeepRecursion),
    /// ReportKeywordLintRule, LinterFactory.
    /// </summary>
    public class ExtraCoverageTests
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

        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static async Task Run(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
        }

        // ── BeginEndBalanceRule — uncovered paths ─────────────────────────────

        [Fact]
        public async Task BeginEndBalance_ForeachContainingPushdown_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule,
                "FOREACH @x IN (SELECT 1 AS n) BEGIN EXECUTE myconn INTO #out BEGIN SELECT @x END; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_CreateFunctionWithPushdown_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule,
                "CREATE FUNCTION myfunc (@n INT) RETURNS INT AS BEGIN EXECUTE myconn BEGIN SELECT @n; END; RETURN 1; END");
            Assert.Empty(results);
        }

        // ── ConnectionAuthConflictRule — recursion paths ──────────────────────

        [Fact]
        public async Task ConnectionAuthConflict_InsideIfBlock_Detected()
        {
            var rule = new ConnectionAuthConflictRule();
            var results = await Lint(rule,
                "IF 1 = 1 BEGIN " +
                "  CREATE CONNECTION c1 ON MSSQL() WITH (TRUSTED_CONNECTION = 'TRUE', USER_ID = 'sa'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ConnectionAuthConflict_InsideWhileBlock_Detected()
        {
            var rule = new ConnectionAuthConflictRule();
            var results = await Lint(rule,
                "WHILE 1 = 0 BEGIN " +
                "  CREATE CONNECTION c2 ON MSSQL() WITH (TRUSTED_CONNECTION = 'TRUE', USER_ID = 'sa'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ConnectionAuthConflict_InsideForBlock_Detected()
        {
            var rule = new ConnectionAuthConflictRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN " +
                "  CREATE CONNECTION c3 ON MSSQL() WITH (TRUSTED_CONNECTION = 'TRUE', USER_ID = 'sa'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ConnectionAuthConflict_InsideForeachBlock_Detected()
        {
            var rule = new ConnectionAuthConflictRule();
            var results = await Lint(rule,
                "FOREACH @x IN (SELECT 1 AS n) BEGIN " +
                "  CREATE CONNECTION c4 ON MSSQL() WITH (TRUSTED_CONNECTION = 'TRUE', USER_ID = 'sa'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ConnectionAuthConflict_InsideTryCatch_Detected()
        {
            var rule = new ConnectionAuthConflictRule();
            var results = await Lint(rule,
                "BEGIN TRY " +
                "  CREATE CONNECTION c5 ON MSSQL() WITH (TRUSTED_CONNECTION = 'TRUE', USER_ID = 'sa'); " +
                "END TRY BEGIN CATCH SELECT 1; END CATCH");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ConnectionAuthConflict_InsideBlock_Detected()
        {
            var rule = new ConnectionAuthConflictRule();
            var results = await Lint(rule,
                "BEGIN " +
                "  CREATE CONNECTION c6 ON MSSQL() WITH (TRUSTED_CONNECTION = 'TRUE', USER_ID = 'sa'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ConnectionAuthConflict_NoOptions_NoError()
        {
            var rule = new ConnectionAuthConflictRule();
            var results = await Lint(rule,
                "CREATE CONNECTION c7 ON MSSQL('server=localhost');");
            Assert.Empty(results);
        }

        // ── AggregateWithoutGroupByRule — uncovered paths ─────────────────────

        [Fact]
        public async Task AggregateWithoutGroupBy_SetOperation_BothSidesChecked()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "SELECT id, COUNT(*) AS cnt FROM #t UNION ALL SELECT id, SUM(v) AS s FROM #t2;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_InsideIf_Detected()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "IF 1 = 1 BEGIN SELECT id, COUNT(*) AS cnt FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_InsideFor_Detected()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN SELECT id, COUNT(*) AS cnt FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_InsideForeach_Detected()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "FOREACH @x IN (SELECT 1 AS n) BEGIN SELECT id, COUNT(*) AS cnt FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_InsideTryCatch_Detected()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "BEGIN TRY SELECT id, COUNT(*) AS cnt FROM #t; END TRY BEGIN CATCH SELECT 1; END CATCH");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_SubqueryInFrom_NoOuterWarning()
        {
            var rule = new AggregateWithoutGroupByRule();
            // The warning is on the inner subquery (has non-agg col), not the outer
            var results = await Lint(rule,
                "SELECT x FROM (SELECT id, COUNT(*) AS cnt FROM #t) AS sub;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_WindowFunction_NoWarning()
        {
            var rule = new AggregateWithoutGroupByRule();
            // ROW_NUMBER() OVER() is a window function, not an aggregate - no warning
            var results = await Lint(rule,
                "SELECT id, ROW_NUMBER() OVER (ORDER BY id) AS rn FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_HavingAggWithNonAgg_Warning()
        {
            var rule = new AggregateWithoutGroupByRule();
            // HAVING clause has aggregate, non-agg column present → warning
            var results = await Lint(rule,
                "SELECT id, COUNT(*) AS cnt FROM #t HAVING COUNT(*) > 5;");
            Assert.NotEmpty(results);
        }

        // ── CredentialLeakRule — uncovered paths ──────────────────────────────

        [Fact]
        public async Task CredentialLeak_EmailSubjectSensitive_Warning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @apikey VARCHAR = 'abc123';" +
                "SEND EMAIL TO 'admin@example.com' FROM 'noreply@example.com' SUBJECT @apikey BODY 'hello';");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.Message.Contains("SEND EMAIL subject"));
        }

        [Fact]
        public async Task CredentialLeak_ExecDynamicSqlWithSensitiveVar_Warning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @password VARCHAR = 'secret';" +
                "EXEC(@password);");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_ForeachSensitiveVarName_Warning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "FOREACH @token IN (SELECT val FROM #t) BEGIN PRINT @token; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_InsideIfBlock_Detected()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @secret VARCHAR = 'val';" +
                "IF 1 = 1 BEGIN PRINT @secret; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_InsideBlock_ScopedProperly()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @credentials VARCHAR = 'creds';" +
                "BEGIN PRINT @credentials; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_EncLiteralInPrint_Warning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "PRINT 'ENC:mysecret';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_SetVariableToSensitiveName_Tracked()
        {
            var rule = new CredentialLeakRule();
            // Target var name is sensitive by keyword → tainted even if RHS is not
            var results = await Lint(rule,
                "DECLARE @x VARCHAR = 'val';" +
                "DECLARE @auth VARCHAR = 'y';" +
                "SET @auth = @x;" +
                "PRINT @auth;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_PrintWithBinaryExpr_ChecksRecursive()
        {
            var rule = new CredentialLeakRule();
            // Binary expression: @key + ' suffix' — should detect @key as sensitive
            var results = await Lint(rule,
                "DECLARE @key VARCHAR = 'k';" +
                "PRINT @key + ' suffix';");
            Assert.NotEmpty(results);
        }

        // ── FileSystemSecurityRule — uncovered paths ──────────────────────────

        [Fact]
        public async Task FileSystemSecurity_BulkInsert_Checked()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "BULK INSERT #t FROM '/data/file.csv' WITH (FIELDTERMINATOR = ',');");
            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal("FileSystemSecurity", r.RuleName));
        }

        [Fact]
        public async Task FileSystemSecurity_BulkInsert_SystemDirectory_Warning()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "BULK INSERT #t FROM 'C:\\WINDOWS\\system32\\data.csv' WITH (FIELDTERMINATOR = ',');");
            var warnings = results.Where(r => r.Severity == LintSeverity.Warning).ToList();
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public async Task FileSystemSecurity_InsideForBlock_Checked()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('/data/file.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_InsideForeachBlock_Checked()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "FOREACH @x IN (SELECT 1 AS n) BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('/data/file.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_InsideParallel_Checked()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "PARALLEL BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('/data/file.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_SshKeyDir_Warning()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "COPY FILE '.ssh/key.pem' TO '/safe/key.pem';");
            var warnings = results.Where(r => r.Severity == LintSeverity.Warning).ToList();
            Assert.NotEmpty(warnings);
        }

        // ── FlatFileDelimiterConflictRule — uncovered paths ───────────────────

        [Fact]
        public async Task FlatFileDelimiterConflict_BulkInsert_SameFieldAndRowTerminator_Error()
        {
            var rule = new FlatFileDelimiterConflictRule();
            var results = await Lint(rule,
                "BULK INSERT #t FROM 'data.csv' WITH (FIELDTERMINATOR = ',', ROWTERMINATOR = ',');");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.Message.Contains("DELIMITER") || r.Message.Contains("ROW_DELIMITER"));
        }

        [Fact]
        public async Task FlatFileDelimiterConflict_BulkInsert_DifferentTerminators_NoError()
        {
            var rule = new FlatFileDelimiterConflictRule();
            var results = await Lint(rule,
                "BULK INSERT #t FROM 'data.csv' WITH (FIELDTERMINATOR = ',', ROWTERMINATOR = '\\n');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task FlatFileDelimiterConflict_InsideIfBlock_Checked()
        {
            var rule = new FlatFileDelimiterConflictRule();
            var results = await Lint(rule,
                "IF 1 = 1 BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('data.csv') WITH (DELIMITER = ';', ROW_DELIMITER = ';'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FlatFileDelimiterConflict_InsideWhileBlock_Checked()
        {
            var rule = new FlatFileDelimiterConflictRule();
            var results = await Lint(rule,
                "WHILE 1 = 0 BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('data.csv') WITH (DELIMITER = '|', ROW_DELIMITER = '|'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FlatFileDelimiterConflict_InsideForBlock_Checked()
        {
            var rule = new FlatFileDelimiterConflictRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('data.csv') WITH (DELIMITER = '\\t', ROW_DELIMITER = '\\t'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FlatFileDelimiterConflict_InsideForeachBlock_Checked()
        {
            var rule = new FlatFileDelimiterConflictRule();
            var results = await Lint(rule,
                "FOREACH @x IN (SELECT 1 AS n) BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('data.csv') WITH (DELIMITER = ',', ROW_DELIMITER = ','); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FlatFileDelimiterConflict_BlockStatement_Checked()
        {
            var rule = new FlatFileDelimiterConflictRule();
            var results = await Lint(rule,
                "BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('data.csv') WITH (DELIMITER = ' ', ROW_DELIMITER = ' '); " +
                "END");
            Assert.NotEmpty(results);
        }

        // ── AbsolutePathRule — uncovered paths ────────────────────────────────

        [Fact]
        public async Task AbsolutePath_BulkInsert_RelativePath_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "BULK INSERT #t FROM 'relative/file.csv' WITH (FIELDTERMINATOR = ',');");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_BulkInsert_AbsolutePath_NoWarning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "BULK INSERT #t FROM 'C:\\data\\file.csv' WITH (FIELDTERMINATOR = ',');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AbsolutePath_InsideIfBlock_Checked()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "IF 1 = 1 BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('relative/path.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_InsideWhileBlock_Checked()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "WHILE 1 = 0 BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('relative/path.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_InsideForBlock_Checked()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('relative/path.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_InsideForeachBlock_Checked()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "FOREACH @x IN (SELECT 1 AS n) BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('relative/path.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_InsideParallelBlock_Checked()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "PARALLEL BEGIN " +
                "  CREATE CONNECTION fc ON FLATFILE('relative/path.csv'); " +
                "END");
            Assert.NotEmpty(results);
        }

        // ── PushdownValidationRule — uncovered paths ──────────────────────────

        [Fact]
        public async Task PushdownValidation_InsideForBlock_Checked()
        {
            var rule = new PushdownValidationRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN EXECUTE myconn INTO #out BEGIN SELECT @i AS n END; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task PushdownValidation_InsideForeachBlock_Checked()
        {
            var rule = new PushdownValidationRule();
            var results = await Lint(rule,
                "FOREACH @x IN (SELECT 1 AS n) BEGIN EXECUTE myconn INTO #out BEGIN SELECT @x END; END");
            Assert.Empty(results);
        }

        // ── SetSecurityOverrideStatementHandler — uncovered switch cases ───────

        [Fact]
        public async Task SetSecurityOverride_FileTypeExtension_Csv_NoError()
        {
            await Run("SET ALLOW_FILE_TYPE_ACCESS = '.csv';");
        }

        [Fact]
        public async Task SetSecurityOverride_FileTypeExtension_Parquet_NoError()
        {
            await Run("SET ALLOW_FILE_TYPE_ACCESS = '.parquet';");
        }

        [Fact]
        public async Task SetSecurityOverride_LargeFileCount_On_NoError()
        {
            await Run("SET ALLOW_GREATER_THAN_50_FILE ON;");
        }

        [Fact]
        public async Task SetSecurityOverride_LargeFileCount_Off_NoError()
        {
            await Run("SET ALLOW_GREATER_THAN_50_FILE OFF;");
        }

        [Fact]
        public async Task SetSecurityOverride_DeepRecursion_On_NoError()
        {
            await Run("SET ALLOW_RECURSIVE_GREATER_THAN_10_LAYERS ON;");
        }

        [Fact]
        public async Task SetSecurityOverride_DeepRecursion_Off_NoError()
        {
            await Run("SET ALLOW_RECURSIVE_GREATER_THAN_10_LAYERS OFF;");
        }

        // ── ReportKeywordLintRule — all statement types ───────────────────────

        [Fact]
        public async Task ReportKeyword_CreateVisual_WithKeywordName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreateVisualStatement
            {
                Name = "SELECT",
                VisualType = VisualType.Bar,
                Source = new VisualSourceExpression()
            });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeyword_CreateVisual_NonKeyword_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule,
                "CREATE VISUAL myvisual AS BAR (SOURCE (SELECT 1 AS n));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ReportKeyword_CreatePage_WithKeywordName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreatePageStatement
            {
                Name = "SELECT",
                Structure = "A"
            });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeyword_CreateContainer_WithKeywordName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreateContainerStatement
            {
                Name = "FROM",
                ContainerType = "BOX"
            });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeyword_CreateStyle_WithKeywordName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreateStyleStatement { Name = "WHERE" });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeyword_CreateTemplate_WithKeywordName_Warning()
        {
            var rule = new ReportKeywordLintRule();
            var script = new Script();
            script.Statements.Add(new CreateTemplateStatement { Name = "ORDER" });
            var results = (await rule.AnalyzeAsync(script, new DefaultLintContext())).ToList();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ReportKeyword_CreateDataset_NonKeyword_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule,
                "CREATE DATASET #mydata AS (SELECT 1 AS n);");
            Assert.Empty(results);
        }

        // ── LinterFactory ─────────────────────────────────────────────────────

        [Fact]
        public void LinterFactory_CreateWithAllRules_WithoutProvider_ReturnsLinterWithRules()
        {
            var linter = LinterFactory.CreateWithAllRules();
            Assert.NotNull(linter);
        }

        [Fact]
        public async Task LinterFactory_CreateWithAllRules_WithoutProvider_CanLint()
        {
            var linter = LinterFactory.CreateWithAllRules();
            var script = Parse("SELECT * FROM #t;");
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            Assert.NotNull(results);
        }

        [Fact]
        public void LinterFactory_CreateWithAllRules_WithServiceProvider_ReturnsLinterWithRules()
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var linter = LinterFactory.CreateWithAllRules(sp);
            Assert.NotNull(linter);
        }

        // ── Additional AggregateWithoutGroupByRule edge cases ─────────────────

        [Fact]
        public async Task AggregateWithoutGroupBy_JoinSubquery_Checked()
        {
            var rule = new AggregateWithoutGroupByRule();
            // Inner subquery in JOIN has agg without group by → warning on inner
            var results = await Lint(rule,
                "SELECT t1.id FROM #t t1 " +
                "JOIN (SELECT id, COUNT(*) AS cnt FROM #t2) sub ON sub.id = t1.id;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_SetOp_AllAggregates_NoWarning()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "SELECT COUNT(*) FROM #t UNION ALL SELECT SUM(v) FROM #t2;");
            Assert.Empty(results);
        }
    }
}
