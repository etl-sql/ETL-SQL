using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Covers low-coverage linting rules:
    /// DatasetEncryptWithoutKeyRule, VisualSourceExistsRule, UnusedConnectionRule,
    /// AbsolutePathRule, CredentialLeakRule, DashboardKeywordConflictRule,
    /// CreateDirectoryInReportRule, BeginEndBalanceRule, ReportKeywordLintRule,
    /// AggregateWithoutGroupByRule, FileSystemSecurityRule.
    /// </summary>
    public class YetMoreLintRuleTests
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

        // ── DatasetEncryptWithoutKeyRule ──────────────────────────────────────

        [Fact]
        public async Task DatasetEncryptWithoutKey_PasswordMode_NoPassword_Error()
        {
            // The transport-credential requirement now applies to EXPORT/PUBLISH, not CREATE.
            var rule = new DatasetEncryptWithoutKeyRule();
            var results = await Lint(rule,
                "EXPORT DATASET &sales TO 'sales.parquet' ENCRYPT = PASSWORD;");
            Assert.NotEmpty(results);
            Assert.Equal("DatasetEncryptWithoutKey", results[0].RuleName);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
        }

        [Fact]
        public async Task DatasetEncryptWithoutKey_PasswordMode_HasPassword_NoError()
        {
            var rule = new DatasetEncryptWithoutKeyRule();
            var results = await Lint(rule,
                "EXPORT DATASET &sales TO 'sales.parquet' ENCRYPT = PASSWORD PASSWORD = 'secret';");
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatasetEncryptWithoutKey_KeyFileMode_NoKeyFile_Error()
        {
            var rule = new DatasetEncryptWithoutKeyRule();
            var results = await Lint(rule,
                "EXPORT DATASET &sales TO 'sales.parquet' ENCRYPT = KEYFILE;");
            Assert.NotEmpty(results);
            Assert.Equal("DatasetEncryptWithoutKey", results[0].RuleName);
        }

        [Fact]
        public async Task DatasetEncryptWithoutKey_KeyFileMode_HasKeyFile_NoError()
        {
            var rule = new DatasetEncryptWithoutKeyRule();
            var results = await Lint(rule,
                "EXPORT DATASET &sales TO 'sales.parquet' ENCRYPT = KEYFILE KEYFILE = '/keys/k.pem';");
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatasetEncryptWithoutKey_CreateDataset_NoError()
        {
            // CREATE DATASET no longer carries a credential requirement (at rest uses the portal key).
            var rule = new DatasetEncryptWithoutKeyRule();
            var results = await Lint(rule,
                "CREATE DATASET &sales ENCRYPT = PASSWORD AS (SELECT 1 AS v);");
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatasetEncryptWithoutKey_NoEncryption_NoError()
        {
            var rule = new DatasetEncryptWithoutKeyRule();
            var results = await Lint(rule,
                "CREATE DATASET &sales AS (SELECT 1 AS v);");
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatasetEncryptWithoutKey_NonDatasetStatement_NoError()
        {
            var rule = new DatasetEncryptWithoutKeyRule();
            var results = await Lint(rule, "SELECT 1 AS n;");
            Assert.Empty(results);
        }

        // ── VisualSourceExistsRule ────────────────────────────────────────────

        [Fact]
        public async Task VisualSourceExists_TempTableDefined_NoWarning()
        {
            var rule = new VisualSourceExistsRule();
            var results = await Lint(rule,
                "CREATE DATASET &sales AS (SELECT 1 AS v);" +
                "CREATE VISUAL mybar AS BAR (SOURCE = #sales, MAPPINGS (X = v, Y = v));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualSourceExists_TempTableUndefined_Warning()
        {
            var rule = new VisualSourceExistsRule();
            var results = await Lint(rule,
                "CREATE VISUAL mybar AS BAR (SOURCE = #undefined_ds, MAPPINGS (X = v, Y = v));");
            Assert.NotEmpty(results);
            Assert.Equal("VisualSourceExists", results[0].RuleName);
        }

        [Fact]
        public async Task VisualSourceExists_InlineSelect_NoWarning()
        {
            var rule = new VisualSourceExistsRule();
            var results = await Lint(rule,
                "CREATE VISUAL mybar AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = n, Y = n));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualSourceExists_SelectInto_SourceDefined_NoWarning()
        {
            var rule = new VisualSourceExistsRule();
            var results = await Lint(rule,
                "SELECT 1 AS v INTO #mysource;" +
                "CREATE VISUAL mybar AS BAR (SOURCE = #mysource, MAPPINGS (X = v, Y = v));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualSourceExists_NoVisuals_Empty()
        {
            var rule = new VisualSourceExistsRule();
            var results = await Lint(rule, "SELECT 1 AS n;");
            Assert.Empty(results);
        }

        // ── UnusedConnectionRule ──────────────────────────────────────────────

        [Fact]
        public async Task UnusedConnection_NoConnections_Empty()
        {
            var rule = new UnusedConnectionRule();
            var results = await Lint(rule, "SELECT 1 AS n;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UnusedConnection_Defined_Used_NoWarning()
        {
            var rule = new UnusedConnectionRule();
            var results = await Lint(rule,
                "CREATE CONNECTION myconn AS FLATFILE('/data/file.csv');" +
                "SELECT * FROM myconn.data;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UnusedConnection_Defined_NotUsed_Warning()
        {
            var rule = new UnusedConnectionRule();
            var results = await Lint(rule,
                "CREATE CONNECTION unusedconn AS FLATFILE('/data/file.csv');");
            Assert.NotEmpty(results);
            Assert.Equal("UnusedConnection", results[0].RuleName);
        }

        [Fact]
        public async Task UnusedConnection_UsedInInsert_NoWarning()
        {
            var rule = new UnusedConnectionRule();
            var results = await Lint(rule,
                "CREATE CONNECTION targetconn AS FLATFILE('/out.csv');" +
                "INSERT INTO targetconn.table (Id) VALUES (1);");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UnusedConnection_UsedInAlter_NoWarning()
        {
            var rule = new UnusedConnectionRule();
            var results = await Lint(rule,
                "CREATE CONNECTION ac AS FLATFILE('/f.csv');" +
                "ALTER CONNECTION ac WITH (ENCODING = 'UTF-8');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UnusedConnection_UsedInDrop_NoWarning()
        {
            var rule = new UnusedConnectionRule();
            var results = await Lint(rule,
                "CREATE CONNECTION dc AS FLATFILE('/f.csv');" +
                "DROP CONNECTION dc;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UnusedConnection_MultipleConnections_OnlyUnusedWarned()
        {
            var rule = new UnusedConnectionRule();
            var results = await Lint(rule,
                "CREATE CONNECTION used AS FLATFILE('/f1.csv');" +
                "CREATE CONNECTION unused AS FLATFILE('/f2.csv');" +
                "SELECT * FROM used.data;");
            Assert.Single(results);
            Assert.Contains("unused", results[0].Message);
        }

        // ── AbsolutePathRule ──────────────────────────────────────────────────

        [Fact]
        public async Task AbsolutePath_RelativePath_FlatFile_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('data/file.csv');");
            Assert.NotEmpty(results);
            Assert.Equal("AbsolutePath", results[0].RuleName);
        }

        [Fact]
        public async Task AbsolutePath_WindowsAbsolute_NoWarning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('C:\\data\\file.csv');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AbsolutePath_UnixAbsolute_NoWarning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('/data/file.csv');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AbsolutePath_UncPath_NoWarning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('\\\\server\\share\\file.csv');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AbsolutePath_EncPath_NoWarning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('ENC:encrypted_path');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AbsolutePath_Url_NoWarning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('s3://bucket/file.csv');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AbsolutePath_FileOperation_RelativePath_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "COPY FILE 'relative/source.txt' TO '/absolute/dest.txt';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_DirectoryOperation_RelativePath_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE DIRECTORY 'relative/mydir';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_RunScript_RelativePath_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "RUN SCRIPT 'relative/script.etlsql';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_SqlServer_NotChecked_NoWarning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION mssql AS MSSQL(SERVER = 'server', DATABASE = 'db', USERNAME = 'u', PASSWORD = 'p');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AbsolutePath_ExcelRelative_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION ec AS EXCEL('relative/workbook.xlsx');");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_JsonRelative_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "CREATE CONNECTION jc AS JSON('relative/data.json');");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_InWhileBlock_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "DECLARE @i INT = 0;" +
                "WHILE @i < 1 BEGIN CREATE DIRECTORY 'relative/mydir'; SET @i = @i + 1; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_InForLoop_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN CREATE DIRECTORY 'relative/mydir'; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AbsolutePath_InIfBlock_Warning()
        {
            var rule = new AbsolutePathRule();
            var results = await Lint(rule,
                "IF 1 = 1 BEGIN CREATE DIRECTORY 'relative/dir'; END");
            Assert.NotEmpty(results);
        }

        // ── DashboardKeywordConflictRule ──────────────────────────────────────

        [Fact]
        public async Task DashboardKeywordConflict_ReservedVisualName_Warning()
        {
            var rule = new DashboardKeywordConflictRule();
            var results = await Lint(rule,
                "CREATE VISUAL Manifest AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = n, Y = n));");
            Assert.NotEmpty(results);
            Assert.Equal("DashboardKeywordConflict", results[0].RuleName);
        }

        [Fact]
        public async Task DashboardKeywordConflict_NormalVisualName_NoWarning()
        {
            var rule = new DashboardKeywordConflictRule();
            var results = await Lint(rule,
                "CREATE VISUAL sales_chart AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = n, Y = n));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task DashboardKeywordConflict_ReservedDatasetName_Warning()
        {
            var rule = new DashboardKeywordConflictRule();
            var results = await Lint(rule,
                "CREATE DATASET &Params AS (SELECT 1 AS v);");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task DashboardKeywordConflict_EngineReservedRowcount_Warning()
        {
            var rule = new DashboardKeywordConflictRule();
            var results = await Lint(rule,
                "CREATE VISUAL ROWCOUNT AS TABLE (SOURCE (SELECT 1 AS n));");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task DashboardKeywordConflict_NormalDatasetName_NoWarning()
        {
            var rule = new DashboardKeywordConflictRule();
            var results = await Lint(rule,
                "CREATE DATASET &sales_data AS (SELECT 1 AS v);");
            Assert.Empty(results);
        }

        // ── CreateDirectoryInReportRule ───────────────────────────────────────

        [Fact]
        public async Task CreateDirectoryInReport_WithVisual_Error()
        {
            var rule = new CreateDirectoryInReportRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = n, Y = n));" +
                "CREATE DIRECTORY '/some/path';");
            Assert.NotEmpty(results);
            Assert.Equal("CreateDirectoryInReport", results[0].RuleName);
        }

        [Fact]
        public async Task CreateDirectoryInReport_NoReportStatements_NoError()
        {
            var rule = new CreateDirectoryInReportRule();
            var results = await Lint(rule,
                "CREATE DIRECTORY '/some/path';");
            Assert.Empty(results);
        }

        [Fact]
        public async Task CreateDirectoryInReport_WithDataset_Error()
        {
            var rule = new CreateDirectoryInReportRule();
            var results = await Lint(rule,
                "CREATE DATASET &ds AS (SELECT 1 AS v);" +
                "CREATE DIRECTORY '/some/path';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CreateDirectoryInReport_WithPage_Error()
        {
            var rule = new CreateDirectoryInReportRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = n, Y = n));" +
                "CREATE PAGE pg1 AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = v1));" +
                "CREATE DIRECTORY '/logs';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CreateDirectoryInReport_WithContainer_Error()
        {
            var rule = new CreateDirectoryInReportRule();
            var results = await Lint(rule,
                "CREATE CONTAINER c1 AS BOX ();" +
                "CREATE DIRECTORY '/data';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CreateDirectoryInReport_SelectOnly_NoError()
        {
            var rule = new CreateDirectoryInReportRule();
            var results = await Lint(rule,
                "SELECT 1 AS n;" +
                "CREATE DIRECTORY '/data';");
            Assert.Empty(results);
        }

        // ── BeginEndBalanceRule ───────────────────────────────────────────────

        [Fact]
        public async Task BeginEndBalance_NoPushdown_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule, "SELECT 1 AS n;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_BalancedPushdown_NoError()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule,
                "EXECUTE myconn INTO #out BEGIN SELECT Id FROM Orders END;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_InsideTryCatch_Checked()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule,
                "BEGIN TRY " +
                "  EXECUTE myconn INTO #out BEGIN SELECT Id FROM Orders END; " +
                "END TRY BEGIN CATCH SELECT @@ERROR; END CATCH");
            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_InsideWhile_Checked()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule,
                "WHILE 1 = 0 BEGIN EXECUTE myconn INTO #out BEGIN SELECT 1 AS n END; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_InsideFor_Checked()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule,
                "FOR @i = 1 TO 1 BEGIN EXECUTE myconn INTO #out BEGIN SELECT 1 AS n END; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task BeginEndBalance_InsideIf_Checked()
        {
            var rule = new BeginEndBalanceRule();
            var results = await Lint(rule,
                "IF 1 = 1 BEGIN EXECUTE myconn INTO #out BEGIN SELECT 1 AS n END; END");
            Assert.Empty(results);
        }

        // ── ReportKeywordLintRule ─────────────────────────────────────────────

        [Fact]
        public async Task ReportKeyword_NonKeywordName_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule,
                "CREATE VISUAL sales_chart AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = n, Y = n));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ReportKeyword_NonKeywordPage_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT 1 AS n), MAPPINGS (X = n, Y = n));" +
                "CREATE PAGE my_dashboard AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = v1));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ReportKeyword_NormalContainer_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule, "CREATE CONTAINER my_container AS BOX ();");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ReportKeyword_NormalDataset_NoWarning()
        {
            var rule = new ReportKeywordLintRule();
            var results = await Lint(rule,
                "CREATE DATASET &revenue_data AS (SELECT 1 AS v);");
            Assert.Empty(results);
        }

        // ── AggregateWithoutGroupByRule ───────────────────────────────────────

        [Fact]
        public async Task AggregateWithoutGroupBy_MixedColumns_Info()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "SELECT name, COUNT(*) AS cnt FROM #t;");
            Assert.NotEmpty(results);
            Assert.Equal("AggregateWithoutGroupBy", results[0].RuleName);
            Assert.Equal(LintSeverity.Info, results[0].Severity);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_WithGroupBy_NoInfo()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "SELECT name, COUNT(*) AS cnt FROM #t GROUP BY name;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_AllAggregates_NoInfo()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "SELECT COUNT(*) AS cnt, SUM(val) AS total FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_NoAggregates_NoInfo()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "SELECT name, val FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_InsideWhile_Checked()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "WHILE 1 = 0 BEGIN SELECT name, SUM(val) AS total FROM #t; END");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task AggregateWithoutGroupBy_SumOnly_NoInfo()
        {
            var rule = new AggregateWithoutGroupByRule();
            var results = await Lint(rule,
                "SELECT SUM(val) AS total FROM #t;");
            Assert.Empty(results);
        }

        // ── CredentialLeakRule ────────────────────────────────────────────────

        [Fact]
        public async Task CredentialLeak_PrintSensitiveVar_Warning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @password VARCHAR = 'secret';" +
                "PRINT @password;");
            Assert.NotEmpty(results);
            Assert.Equal("CredentialLeak", results[0].RuleName);
        }

        [Fact]
        public async Task CredentialLeak_PrintNonSensitiveVar_NoWarning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @username VARCHAR = 'alice';" +
                "PRINT @username;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task CredentialLeak_RaiseErrorSensitive_Warning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @token VARCHAR = 'abc';" +
                "RAISERROR('Token: ' + @token, 16, 1);");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_DeclaredEncrypted_Warning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @apikey ENCRYPTED = 'enc:xxx';" +
                "PRINT @apikey;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_NoSensitiveVars_NoWarning()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @count INT = 5;" +
                "PRINT 'Count: ';");
            Assert.Empty(results);
        }

        [Fact]
        public async Task CredentialLeak_SetVariableTaint_TracksLeak()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "DECLARE @password VARCHAR = 'secret';" +
                "DECLARE @msg VARCHAR = 'x';" +
                "SET @msg = @password;" +
                "PRINT @msg;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task CredentialLeak_ForLoopSensitiveVar_Tracked()
        {
            var rule = new CredentialLeakRule();
            var results = await Lint(rule,
                "FOR @secret = 1 TO 3 BEGIN PRINT @secret; END");
            Assert.NotEmpty(results);
        }

        // ── FileSystemSecurityRule ────────────────────────────────────────────

        [Fact]
        public async Task FileSystemSecurity_NormalPath_NoWarning()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('/data/safe/file.csv');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_DriveRoot_Warning()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('C:\\');");
            var warnings = results.Where(r => r.Severity == LintSeverity.Warning).ToList();
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public async Task FileSystemSecurity_SystemDirectory_Warning()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('C:\\WINDOWS\\system32\\file.txt');");
            var warnings = results.Where(r => r.Severity == LintSeverity.Warning).ToList();
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public async Task FileSystemSecurity_EncPath_NoResult()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('ENC:encpath');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_Url_NoResult()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "CREATE CONNECTION fc AS FLATFILE('https://example.com/file.csv');");
            Assert.Empty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_FileOperation_Checked()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "COPY FILE 'C:\\WINDOWS\\source.txt' TO 'C:\\WINDOWS\\dest.txt';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_DirectoryOperation_Checked()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "CREATE DIRECTORY 'C:\\WINDOWS\\mydir';");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task FileSystemSecurity_RunScript_Checked()
        {
            var rule = new FileSystemSecurityRule();
            var results = await Lint(rule,
                "RUN SCRIPT 'C:\\WINDOWS\\myscript.etlsql';");
            Assert.NotEmpty(results);
        }
    }
}
