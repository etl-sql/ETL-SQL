using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.TUI.UI;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class ParserTests
    {
        [Fact]
        public void TestParseDeclare()
        {
            var source = "DECLARE @v INT; SET @v = 10;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Equal(2, script.Statements.Count);
            Assert.IsType<DeclareStatement>(script.Statements[0]);
            Assert.IsType<SetVariableStatement>(script.Statements[1]);

            var decl = (DeclareStatement)script.Statements[0];
            Assert.Equal("@v", decl.VariableName);
            Assert.Equal("INT", decl.DataType);
        }

        [Fact]
        public void TestParseSelect()
        {
            var source = "SELECT Col1, 1+1 AS Two FROM MyTable WHERE Col1 > 0 ORDER BY Col1 DESC LIMIT 10;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            Assert.IsType<SelectStatement>(script.Statements[0]);

            var select = (SelectStatement)script.Statements[0];
            Assert.Equal(2, select.Columns.Count);
            Assert.Equal("MyTable", select.FromTable.TableName);
            Assert.NotNull(select.WhereClause);
            Assert.Single(select.OrderBy);
            Assert.NotNull(select.LimitCount);
        }

        [Fact]
        public void TestParseCreateConnection()
        {
            var source = "CREATE CONNECTION my_conn AS FLATFILE('data.csv', DELIMITER=PIPE, HEADER=ON);";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            Assert.IsType<CreateConnectionStatement>(script.Statements[0]);
            var cc = (CreateConnectionStatement)script.Statements[0];
            Assert.Equal("my_conn", cc.ConnectionName);
            Assert.Equal("FLATFILE", cc.ConnectionType);
            Assert.Equal(2, cc.Options?.Count);
            var delimExpr = cc.Options?["DELIMITER"];
            var delimVal = delimExpr is LiteralExpression lit ? lit.Value?.ToString() : (delimExpr as IdentifierExpression)?.Name;
            Assert.Equal("PIPE", delimVal);
        }

        [Fact]
        public void ExportReport_WithPdfOptions_ParsesAndSerializes()
        {
            var source = "EXPORT REPORT 'sales.rptsql' FORMAT PDF TO 'sales.pdf' WITH (PDF_MODE = BROWSER, HOST = 'http://localhost:5080', BROWSER_PATH = 'C:\\Chrome\\chrome.exe');";
            var script = new Parser(new Lexer(source).Tokenize()).Parse();

            Assert.Empty(script.Diagnostics);
            var stmt = Assert.IsType<ExportReportStatement>(script.Statements.Single());

            Assert.Equal("PDF", stmt.Format);
            Assert.Equal("BROWSER", stmt.PdfMode);
            Assert.Equal("'http://localhost:5080'", stmt.Host?.ToSql());
            Assert.Equal("'C:\\Chrome\\chrome.exe'", stmt.BrowserPath?.ToSql());
            Assert.Equal(source, stmt.ToSql());
        }

        [Fact]
        public void ExportReport_WithAutoPdfMode_Parses()
        {
            var source = "EXPORT REPORT 'sales.rptsql' FORMAT PDF TO 'sales.pdf' WITH (PDF_MODE = AUTO);";
            var script = new Parser(new Lexer(source).Tokenize()).Parse();

            Assert.Empty(script.Diagnostics);
            var stmt = Assert.IsType<ExportReportStatement>(script.Statements.Single());

            Assert.Equal("AUTO", stmt.PdfMode);
            Assert.Null(stmt.Host);
            Assert.Null(stmt.BrowserPath);
        }

        [Fact]
        public void ExportReport_WithOptions_RejectsNonPdfFormat()
        {
            var source = "EXPORT REPORT 'sales.rptsql' FORMAT CSV TO 'sales.csv' WITH (PDF_MODE = STATIC);";
            var script = new Parser(new Lexer(source).Tokenize()).Parse();

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Contains("only valid when FORMAT PDF", diagnostic.Message);
        }

        [Fact]
        public void TestParseExpressionPrecedence()
        {
            var source = "PRINT 1 + 2 * 3;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.IsType<PrintStatement>(script.Statements[0]);
            var print = (PrintStatement)script.Statements[0];
            Assert.Single(print.Arguments);
            Assert.IsType<BinaryExpression>(print.Arguments[0]);

            var bin = (BinaryExpression)print.Arguments[0];
            // 1 + (2 * 3) -> Top level should be +
            Assert.Equal(TokenType.PLUS, bin.Operator);
            Assert.IsType<BinaryExpression>(bin.Right);
            var rightBin = (BinaryExpression)bin.Right;
            Assert.Equal(TokenType.STAR, rightBin.Operator);
        }

        [Fact]
        public void TestParseInsert()
        {
            var source = "INSERT INTO Dest SELECT * FROM Src;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            Assert.IsType<InsertStatement>(script.Statements[0]);

            var insert = (InsertStatement)script.Statements[0];
            Assert.Equal("Dest", insert.TargetTable.TableName);
            Assert.NotNull(insert.SelectQuery);
        }

        [Fact]
        public void TestParseQualify()
        {
            var source = "SELECT * FROM T QUALIFY ROW_NUMBER() OVER(ORDER BY ID) = 1;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            var select = (SelectStatement)script.Statements[0];
            Assert.NotNull(select.QualifyClause);
            Assert.IsType<BinaryExpression>(select.QualifyClause);
        }

        [Fact]
        public void TestParseFilterInWindow()
        {
            var source = "SELECT SUM(Val) FILTER (WHERE Val > 10) OVER(ORDER BY ID) FROM T;";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            var select = (SelectStatement)script.Statements[0];
            var col = select.Columns[0];
            var fce = (FunctionCallExpression)col.Expression;
            Assert.NotNull(fce.Filter);
            Assert.IsType<BinaryExpression>(fce.Filter);
        }

        [Fact]
        public void ParseBundlePublishValidateExportAndShowStatements()
        {
            var source = @"
PUBLISH BUNDLE 'finance-load' FROM 'C:\ETL\finance' ENTRY 'main.etlsql' WITH (PASSWORD = '1234', ENCRYPT = MACHINE);
VALIDATE BUNDLE 'finance-load' FROM 'C:\ETL\finance' ENTRY 'main.etlsql';
EXPORT SCRIPT 'orch://finance-load@1/main.etlsql' TO 'C:\Recovered\finance';
SHOW PUBLISHED BUNDLES;
SHOW BUNDLE VERSIONS 'finance-load';
SHOW BUNDLE FILES 'finance-load' VERSION 1;
SHOW BUNDLE DEPENDENCIES 'finance-load' VERSION 1;";
            var script = new Parser(new Lexer(source).Tokenize()).Parse();

            Assert.Empty(script.Diagnostics);
            Assert.IsType<PublishBundleStatement>(script.Statements[0]);
            Assert.IsType<ValidateBundleStatement>(script.Statements[1]);
            Assert.IsType<ExportScriptStatement>(script.Statements[2]);
            Assert.IsType<ShowPublishedBundlesStatement>(script.Statements[3]);
            Assert.IsType<ShowBundleVersionsStatement>(script.Statements[4]);
            Assert.IsType<ShowBundleFilesStatement>(script.Statements[5]);
            Assert.IsType<ShowBundleDependenciesStatement>(script.Statements[6]);

            // Assert round-trip SQL serialization
            Assert.Equal("PUBLISH BUNDLE 'finance-load' FROM 'C:\\ETL\\finance' ENTRY 'main.etlsql' WITH (PASSWORD = '1234', ENCRYPT = MACHINE);", script.Statements[0].ToSql());
            Assert.Equal("VALIDATE BUNDLE 'finance-load' FROM 'C:\\ETL\\finance' ENTRY 'main.etlsql';", script.Statements[1].ToSql());
            Assert.Equal("EXPORT SCRIPT 'orch://finance-load@1/main.etlsql' TO 'C:\\Recovered\\finance';", script.Statements[2].ToSql());
            Assert.Equal("SHOW PUBLISHED BUNDLES;", script.Statements[3].ToSql());
            Assert.Equal("SHOW BUNDLE VERSIONS 'finance-load';", script.Statements[4].ToSql());
            Assert.Equal("SHOW BUNDLE FILES 'finance-load' VERSION 1;", script.Statements[5].ToSql());
            Assert.Equal("SHOW BUNDLE DEPENDENCIES 'finance-load' VERSION 1;", script.Statements[6].ToSql());

            // Validate PASSWORD = PROMPT
            var promptSrc = "PUBLISH BUNDLE 'finance-load' FROM 'C:\\ETL\\finance' ENTRY 'main.etlsql' WITH (PASSWORD = PROMPT);";
            var promptScript = new Parser(new Lexer(promptSrc).Tokenize()).Parse();
            Assert.Empty(promptScript.Diagnostics);
            var pubPrompt = Assert.IsType<PublishBundleStatement>(promptScript.Statements[0]);
            Assert.Equal(BundleSecretMode.Prompt, pubPrompt.PasswordMode);
            Assert.Equal("PUBLISH BUNDLE 'finance-load' FROM 'C:\\ETL\\finance' ENTRY 'main.etlsql' WITH (PASSWORD = PROMPT, ENCRYPT = MACHINE);", pubPrompt.ToSql());
        }

        [Fact]
        public void ParsePublishBundleInsideBeginTry_NoDiagnostics()
        {
            // Regression: PUBLISH BUNDLE (and other statements that don't consume their own trailing
            // ';') parsed at top level but failed inside BEGIN TRY, because ParseBlock did not skip
            // the standalone ';' the way the top-level Parse() loop does.
            var source = @"
BEGIN TRY
    PUBLISH BUNDLE 'finance-load' FROM 'C:\ETL\finance' ENTRY 'main.etlsql' WITH (PASSWORD = '1234', ENCRYPT = MACHINE);
    VALIDATE BUNDLE 'finance-load' FROM 'C:\ETL\finance' ENTRY 'main.etlsql';
    PRINT 'done';
END TRY
BEGIN CATCH
    PRINT 'failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH;";
            var script = new Parser(new Lexer(source).Tokenize()).Parse();

            Assert.Empty(script.Diagnostics);
            var tryCatch = Assert.IsType<TryCatchStatement>(script.Statements[0]);
            var tryBody = Assert.IsType<BlockStatement>(tryCatch.TryBody);
            Assert.IsType<PublishBundleStatement>(tryBody.Statements[0]);
            Assert.IsType<ValidateBundleStatement>(tryBody.Statements[1]);
        }

        [Fact]
        public void ParseShowBundlesAliasStatement()
        {
            var source = @"
SHOW BUNDLES;
SHOW BUNDLES AT my_conn;
SHOW BUNDLES INTO #my_temp;
SHOW BUNDLES AT my_conn INTO #my_temp;";
            var script = new Parser(new Lexer(source).Tokenize()).Parse();

            Assert.Empty(script.Diagnostics);
            Assert.Equal(4, script.Statements.Count);

            var s1 = Assert.IsType<ShowPublishedBundlesStatement>(script.Statements[0]);
            Assert.True(s1.IsAlias);
            Assert.Null(s1.At);
            Assert.Null(s1.IntoTable);
            Assert.Equal("SHOW BUNDLES;", s1.ToSql());

            var s2 = Assert.IsType<ShowPublishedBundlesStatement>(script.Statements[1]);
            Assert.True(s2.IsAlias);
            Assert.Equal("my_conn", s2.At);
            Assert.Null(s2.IntoTable);
            Assert.Equal("SHOW BUNDLES AT my_conn;", s2.ToSql());

            var s3 = Assert.IsType<ShowPublishedBundlesStatement>(script.Statements[2]);
            Assert.True(s3.IsAlias);
            Assert.Null(s3.At);
            Assert.Equal("#my_temp", s3.IntoTable);
            Assert.Equal("SHOW BUNDLES INTO #my_temp;", s3.ToSql());

            var s4 = Assert.IsType<ShowPublishedBundlesStatement>(script.Statements[3]);
            Assert.True(s4.IsAlias);
            Assert.Equal("my_conn", s4.At);
            Assert.Equal("#my_temp", s4.IntoTable);
            Assert.Equal("SHOW BUNDLES AT my_conn INTO #my_temp;", s4.ToSql());
        }

        [Fact]
        public void ParseUsePasswordPrompt()
        {
            var script = new Parser(new Lexer("USE PASSWORD PROMPT;").Tokenize()).Parse();
            var stmt = Assert.IsType<UsePasswordStatement>(script.Statements[0]);
            Assert.True(stmt.Prompt);
        }

        [Fact]
        public void TestParseShowLineageForms()
        {
            var script = Parse(@"
SHOW LINEAGE;
SHOW LINEAGE FOR REPORT SalesDashboard;
SHOW LINEAGE FOR DATASET &CustomerMart;
SHOW LINEAGE FOR #Target COLUMN Revenue INTO #lineage;
");

            Assert.Equal(4, script.Statements.Count);
            Assert.All(script.Statements, stmt => Assert.IsType<LineageStatement>(stmt));

            var all = (LineageStatement)script.Statements[0];
            Assert.Null(all.TargetTable);

            var report = (LineageStatement)script.Statements[1];
            Assert.Equal("report:SalesDashboard", report.TargetTable?.TableName);

            var dataset = (LineageStatement)script.Statements[2];
            Assert.Equal("dataset:CustomerMart", dataset.TargetTable?.TableName);

            var table = (LineageStatement)script.Statements[3];
            Assert.Equal("#Target", table.TargetTable?.TableName);
            Assert.Equal("Revenue", table.ColumnName);
            Assert.Equal("#lineage", table.IntoTable);
        }

        [Fact]
        public void TestParseEnableDisableTriggerJob()
        {
            // 1. Without AT
            var script = Parse(@"
ENABLE JOB JobA;
DISABLE JOB JobB;
TRIGGER JOB JobC;
");
            Assert.Equal(3, script.Statements.Count);

            var enable = Assert.IsType<EnableJobStatement>(script.Statements[0]);
            Assert.Equal("JobA", enable.Name);
            Assert.Null(enable.At);
            Assert.Equal("ENABLE JOB JobA;", enable.ToSql());

            var disable = Assert.IsType<DisableJobStatement>(script.Statements[1]);
            Assert.Equal("JobB", disable.Name);
            Assert.Null(disable.At);
            Assert.Equal("DISABLE JOB JobB;", disable.ToSql());

            var trigger = Assert.IsType<TriggerJobStatement>(script.Statements[2]);
            Assert.Equal("JobC", trigger.Name);
            Assert.Null(trigger.At);
            Assert.Equal("TRIGGER JOB JobC;", trigger.ToSql());

            // 2. With AT
            var scriptWithAt = Parse(@"
ENABLE JOB JobA AT remote_conn;
DISABLE JOB JobB AT remote_conn;
TRIGGER JOB JobC AT remote_conn;
");
            Assert.Equal(3, scriptWithAt.Statements.Count);

            var enableAt = Assert.IsType<EnableJobStatement>(scriptWithAt.Statements[0]);
            Assert.Equal("JobA", enableAt.Name);
            Assert.Equal("remote_conn", enableAt.At);
            Assert.Equal("ENABLE JOB JobA AT remote_conn;", enableAt.ToSql());

            var disableAt = Assert.IsType<DisableJobStatement>(scriptWithAt.Statements[1]);
            Assert.Equal("JobB", disableAt.Name);
            Assert.Equal("remote_conn", disableAt.At);
            Assert.Equal("DISABLE JOB JobB AT remote_conn;", disableAt.ToSql());

            var triggerAt = Assert.IsType<TriggerJobStatement>(scriptWithAt.Statements[2]);
            Assert.Equal("JobC", triggerAt.Name);
            Assert.Equal("remote_conn", triggerAt.At);
            Assert.Equal("TRIGGER JOB JobC AT remote_conn;", triggerAt.ToSql());
        }

        [Fact]
        public void TestSyntaxExceptionSanitizesSecrets()
        {
            var ex = new ETL_SQL.Core.Common.Exceptions.SyntaxException("Failed with option PASSWORD = 'myPlaintextPassword'", 10, 5);
            // SecretRedactor preserves the value's surrounding quotes (normalizing only spaces around '=').
            Assert.Contains("PASSWORD='********'", ex.Message);
            Assert.DoesNotContain("myPlaintextPassword", ex.Message);

            var exEnc = new ETL_SQL.Core.Common.Exceptions.SyntaxException("Invalid value 'ENC:abc123xyz='", 1, 1);
            Assert.Contains("ENC:********", exEnc.Message);
            Assert.DoesNotContain("abc123xyz", exEnc.Message);
        }

        [Fact]
        public void TestDiagnosticSanitizesSecrets()
        {
            var diag = new ETL_SQL.Core.Common.Diagnostic("Error in connection PWD = 'secret_key' or ENC:abc123xyz=", 1, 1);
            Assert.Contains("PWD='********'", diag.Message);
            Assert.Contains("ENC:********", diag.Message);
            Assert.DoesNotContain("secret_key", diag.Message);
            Assert.DoesNotContain("abc123xyz", diag.Message);
        }

        [Fact]
        public void TestParseOperatorMemoryGrantAndConnectionPreviewLimit()
        {
            var script = Parse("SET OPERATOR_MEMORY_GRANT = 512; SET CONNECTION_PREVIEW_LIMIT = 50;");
            Assert.Equal(2, script.Statements.Count);

            Assert.IsType<SetThresholdStatement>(script.Statements[0]);
            var opMem = (SetThresholdStatement)script.Statements[0];
            Assert.Equal(ThresholdType.OperatorMemoryGrant, opMem.Type);
            Assert.Equal(512, Convert.ToInt32(((LiteralExpression)opMem.Value).Value));

            Assert.IsType<SetThresholdStatement>(script.Statements[1]);
            var prevLimit = (SetThresholdStatement)script.Statements[1];
            Assert.Equal(ThresholdType.ConnectionPreviewLimit, prevLimit.Type);
            Assert.Equal(50, Convert.ToInt32(((LiteralExpression)prevLimit.Value).Value));
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }
    }
}
