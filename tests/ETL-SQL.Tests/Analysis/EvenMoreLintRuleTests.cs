using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Covers low-coverage analysis linting rules:
    /// VisualMappingColumnExistsRule, UndeclaredVariableRule,
    /// PushdownValidationRule, DatabaseQualificationRule.
    /// </summary>
    public class EvenMoreLintRuleTests
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

        // ── VisualMappingColumnExistsRule ──────────────────────────────────────

        [Fact]
        public async Task VisualMapping_NoWarning_WhenColumnExistsInSelect()
        {
            var rule = new VisualMappingColumnExistsRule();
            var results = await Lint(rule,
                "CREATE VISUAL myvis AS BAR (SOURCE (SELECT revenue AS amt, month AS mon), MAPPINGS (x = mon, y = amt));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualMapping_Warning_WhenColumnMissing()
        {
            var rule = new VisualMappingColumnExistsRule();
            var results = await Lint(rule,
                "CREATE VISUAL myvis AS BAR (SOURCE (SELECT revenue AS amt), MAPPINGS (x = missing_col));");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.RuleName == "VisualMappingColumnExists");
        }

        [Fact]
        public async Task VisualMapping_NoWarning_WhenSelectStarUsed()
        {
            var rule = new VisualMappingColumnExistsRule();
            var results = await Lint(rule,
                "CREATE VISUAL myvis AS BAR (SOURCE (SELECT * FROM #t), MAPPINGS (x = anycol));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualMapping_NoWarning_WhenTempTableSource()
        {
            var rule = new VisualMappingColumnExistsRule();
            var results = await Lint(rule,
                "CREATE VISUAL myvis AS BAR (SOURCE = #sales, MAPPINGS (x = month, y = amount));");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualMapping_NoStatements_ReturnsEmpty()
        {
            var rule = new VisualMappingColumnExistsRule();
            var results = await Lint(rule, "SELECT 1 AS n;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task VisualMapping_MultipleWarnings_BothReported()
        {
            var rule = new VisualMappingColumnExistsRule();
            var results = await Lint(rule,
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT amt AS a), MAPPINGS (x = missing1, y = missing2));");
            Assert.Equal(2, results.Count);
        }

        // ── UndeclaredVariableRule ────────────────────────────────────────────

        [Fact]
        public async Task UndeclaredVariable_NoWarning_WhenDeclared()
        {
            var rule = new UndeclaredVariableRule();
            var results = await Lint(rule,
                "DECLARE @x INT = 0;" +
                "SELECT @x AS val;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UndeclaredVariable_Warning_WhenUsedBeforeDeclare()
        {
            var rule = new UndeclaredVariableRule();
            var results = await Lint(rule,
                "SELECT @undeclared AS val;");
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.RuleName == "UndeclaredVariable");
        }

        [Fact]
        public async Task UndeclaredVariable_NoWarning_WhenDeclaredBeforeUse()
        {
            var rule = new UndeclaredVariableRule();
            var results = await Lint(rule,
                "DECLARE @n INT = 5;" +
                "IF @n > 3 BEGIN SELECT @n; END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UndeclaredVariable_Warning_InWhereClause()
        {
            var rule = new UndeclaredVariableRule();
            var results = await Lint(rule,
                "SELECT * FROM #t WHERE Id = @undeclared;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task UndeclaredVariable_NoWarning_InDeclareInitializer()
        {
            var rule = new UndeclaredVariableRule();
            // @x declared before, @y uses @x in its initializer
            var results = await Lint(rule,
                "DECLARE @x INT = 5;" +
                "DECLARE @y INT = @x;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task UndeclaredVariable_Warning_InUpdateAssignment()
        {
            var rule = new UndeclaredVariableRule();
            var results = await Lint(rule,
                "UPDATE #t SET val = @notdeclared WHERE Id = 1;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task UndeclaredVariable_Warning_InDeleteWhere()
        {
            var rule = new UndeclaredVariableRule();
            var results = await Lint(rule,
                "DELETE FROM #t WHERE Id = @notdeclared;");
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task UndeclaredVariable_NestedBlock_Warning()
        {
            var rule = new UndeclaredVariableRule();
            var results = await Lint(rule,
                "BEGIN SELECT @nope; END");
            Assert.NotEmpty(results);
        }

        // ── PushdownValidationRule ────────────────────────────────────────────

        [Fact]
        public async Task PushdownValidation_ValidSql_NoWarning()
        {
            var rule = new PushdownValidationRule();
            var results = await Lint(rule,
                "EXECUTE myconn INTO #out BEGIN SELECT Id, Name FROM Customers WHERE Id > 0 END;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task PushdownValidation_NoWarning_ForNonPushdownStatements()
        {
            var rule = new PushdownValidationRule();
            var results = await Lint(rule, "SELECT 1 AS n; DECLARE @x INT = 0;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task PushdownValidation_InsideIfBlock_ChecksNestedStatement()
        {
            var rule = new PushdownValidationRule();
            var results = await Lint(rule,
                "IF 1 = 1 BEGIN " +
                "  EXECUTE myconn INTO #out BEGIN SELECT Id FROM Orders END; " +
                "END");
            Assert.Empty(results);
        }

        [Fact]
        public async Task PushdownValidation_InsideWhile_Checked()
        {
            var rule = new PushdownValidationRule();
            var results = await Lint(rule,
                "WHILE 1 = 0 BEGIN " +
                "  EXECUTE myconn INTO #out BEGIN SELECT 1 AS n END; " +
                "END");
            Assert.Empty(results);
        }

        // ── DatabaseQualificationRule ─────────────────────────────────────────

        [Fact]
        public async Task DatabaseQualification_NullMetadata_ReturnsEmpty()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext { Metadata = null };
            var results = await Lint(rule,
                "SELECT * FROM Orders;", ctx);
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatabaseQualification_SingleDbConnection_NoWarning()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "db1" },
                    connectionType: "MSSQL")
            };
            var results = await Lint(rule,
                "SELECT * FROM Orders;", ctx);
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatabaseQualification_MultipleConnections_UnqualifiedTable_Warning()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "db1", "db2" },
                    connectionType: "MSSQL")
            };
            var results = await Lint(rule,
                "SELECT * FROM Orders;", ctx);
            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.RuleName == "DatabaseQualification");
        }

        [Fact]
        public async Task DatabaseQualification_QualifiedTableRef_NoWarning()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "db1", "db2" },
                    connectionType: "MSSQL")
            };
            var results = await Lint(rule,
                "SELECT * FROM db1.Orders;", ctx);
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatabaseQualification_TempTableRef_NoWarning()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "db1", "db2" },
                    connectionType: "MSSQL")
            };
            var results = await Lint(rule,
                "SELECT * FROM #temp_table;", ctx);
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatabaseQualification_NoDbConnections_NoWarning()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "flat1", "flat2" },
                    connectionType: "FLATFILE")
            };
            var results = await Lint(rule,
                "SELECT * FROM Orders;", ctx);
            Assert.Empty(results);
        }

        [Fact]
        public async Task DatabaseQualification_InsertStatement_Checked()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "db1", "db2" },
                    connectionType: "POSTGRES")
            };
            var results = await Lint(rule,
                "INSERT INTO Orders (Id) VALUES (1);", ctx);
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task DatabaseQualification_UpdateStatement_Checked()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "db1", "db2" },
                    connectionType: "ORACLE")
            };
            var results = await Lint(rule,
                "UPDATE Orders SET Status = 'done' WHERE Id = 1;", ctx);
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task DatabaseQualification_DeleteStatement_Checked()
        {
            var rule = new DatabaseQualificationRule();
            var ctx = new DefaultLintContext
            {
                Metadata = new StubMetadataProvider(
                    connections: new[] { "db1", "db2" },
                    connectionType: "MSSQL")
            };
            var results = await Lint(rule,
                "DELETE FROM Orders WHERE Id = 1;", ctx);
            Assert.NotEmpty(results);
        }

        // ── Helper: stub metadata provider ───────────────────────────────────

        private sealed class StubMetadataProvider : IMetadataProvider
        {
            private readonly IEnumerable<string> _connections;
            private readonly string _connectionType;

            public StubMetadataProvider(IEnumerable<string> connections, string connectionType)
            {
                _connections = connections;
                _connectionType = connectionType;
            }

            public IEnumerable<string> GetConnections() => _connections;
            public string? GetConnectionType(string connectionName) => _connectionType;
            public Task<IEnumerable<string>> GetTablesAsync(string connectionName)
                => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName)
                => Task.FromResult(Enumerable.Empty<string>());
        }
    }
}
