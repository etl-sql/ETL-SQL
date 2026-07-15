using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class ExpectSchemaTests
    {
        private static Evaluator GetEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        // ── Parser ────────────────────────────────────────────────────────────

        [Fact]
        public void ParsesBasicExpectSchema()
        {
            var script = Parse("EXPECT SCHEMA #data (Id INT, Name VARCHAR);");
            Assert.Single(script.Statements);
            var stmt = Assert.IsType<ExpectSchemaStatement>(script.Statements[0]);
            Assert.Equal("#data", stmt.Target);
            Assert.Equal(2, stmt.Columns.Count);
            Assert.Equal("Id", stmt.Columns[0].ColumnName);
            Assert.Equal("INT", stmt.Columns[0].DataType);
            Assert.Equal("Name", stmt.Columns[1].ColumnName);
            Assert.Equal("VARCHAR", stmt.Columns[1].DataType);
            Assert.False(stmt.WarnOnDrift);
        }

        [Fact]
        public void ParsesExpectSchemaWithOnDriftWarn()
        {
            var script = Parse("EXPECT SCHEMA #data (Id INT) ON DRIFT WARN;");
            var stmt = Assert.IsType<ExpectSchemaStatement>(script.Statements[0]);
            Assert.True(stmt.WarnOnDrift);
        }

        [Fact]
        public void ParsesExpectSchemaWithLengthType()
        {
            var script = Parse("EXPECT SCHEMA #data (Name VARCHAR(100), Amount DECIMAL(18,2));");
            var stmt = Assert.IsType<ExpectSchemaStatement>(script.Statements[0]);
            Assert.Equal("VARCHAR(100)", stmt.Columns[0].DataType);
            Assert.Equal("DECIMAL(18,2)", stmt.Columns[1].DataType);
        }

        [Fact]
        public void ParsesExpectSchemaWithNotNull()
        {
            var script = Parse("EXPECT SCHEMA #data (Id INT NOT NULL);");
            var stmt = Assert.IsType<ExpectSchemaStatement>(script.Statements[0]);
            Assert.True(stmt.Columns[0].NotNull);
        }

        // ── Execution: passing ────────────────────────────────────────────────

        [Fact]
        public async Task PassesWhenSchemaMatches()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Id INT, Name VARCHAR(50));
                INSERT INTO #data (Id, Name) VALUES (1, 'Alice');
                EXPECT SCHEMA #data (Id INT, Name VARCHAR);
            "));
            // No exception = pass
        }

        [Fact]
        public async Task PassesWhenTypeFamilyMatchesWithDifferentPrecision()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Amount DECIMAL(10,2));
                EXPECT SCHEMA #data (Amount DECIMAL(18,4));
            "));
        }

        [Fact]
        public async Task PassesWithSubsetOfColumns()
        {
            // EXPECT SCHEMA only checks declared columns — extra columns in the table are fine
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Id INT, Name VARCHAR, Score FLOAT);
                EXPECT SCHEMA #data (Id INT);
            "));
        }

        // ── Execution: missing column ─────────────────────────────────────────

        [Fact]
        public async Task ThrowsOnMissingColumn()
        {
            var ev = GetEvaluator();
            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
                await ev.Evaluate(Parse(@"
                    CREATE TABLE #data (Id INT);
                    EXPECT SCHEMA #data (Id INT, Name VARCHAR);
                ")));
            Assert.Contains("MISSING", ex.Message);
            Assert.Contains("Name", ex.Message);
        }

        // ── Execution: type family mismatch ───────────────────────────────────

        [Fact]
        public async Task ThrowsOnTypeFamilyMismatch()
        {
            var ev = GetEvaluator();
            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
                await ev.Evaluate(Parse(@"
                    CREATE TABLE #data (Id VARCHAR);
                    EXPECT SCHEMA #data (Id INT);
                ")));
            Assert.Contains("TYPE DRIFT", ex.Message);
            Assert.Contains("Id", ex.Message);
        }

        [Fact]
        public async Task DoesNotThrowForSameTypeFamily_IntegerVariants()
        {
            var ev = GetEvaluator();
            // INT vs BIGINT are the same family
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Id BIGINT);
                EXPECT SCHEMA #data (Id INT);
            "));
        }

        // ── Execution: ON DRIFT WARN ──────────────────────────────────────────

        [Fact]
        public async Task WarnOnDriftLogsWarningInsteadOfThrowing()
        {
            var ev = GetEvaluator();
            // Missing column with WARN should not throw
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Id INT);
                EXPECT SCHEMA #data (Id INT, Name VARCHAR) ON DRIFT WARN;
            "));
        }

        // ── Execution: unknown target ─────────────────────────────────────────

        [Fact]
        public async Task ThrowsWhenTargetNotFound()
        {
            var ev = GetEvaluator();
            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
                await ev.Evaluate(Parse(@"
                    EXPECT SCHEMA #nonexistent (Id INT);
                ")));
            Assert.Contains("not found", ex.Message);
        }

        // ── Parser FROM Clause ────────────────────────────────────────────────
        [Fact]
        public void ParsesExpectSchemaWithJsonSpec()
        {
            var script = Parse("EXPECT SCHEMA #data FROM 'tests/testdata/Specs/customer_spec.json';");
            Assert.Single(script.Statements);
            var stmt = Assert.IsType<ExpectSchemaStatement>(script.Statements[0]);
            Assert.Equal("#data", stmt.Target);
            Assert.Null(stmt.Columns);
            Assert.Equal("tests/testdata/Specs/customer_spec.json", stmt.SchemaPath);
            Assert.False(stmt.WarnOnDrift);
        }

        [Fact]
        public void ParsesExpectSchemaWithJsonSpecAndOnDriftWarn()
        {
            var script = Parse("EXPECT SCHEMA #data FROM 'tests/testdata/Specs/customer_spec.json' ON DRIFT WARN;");
            var stmt = Assert.IsType<ExpectSchemaStatement>(script.Statements[0]);
            Assert.True(stmt.WarnOnDrift);
        }

        // ── Execution FROM Clause ─────────────────────────────────────────────
        private static string FindProjectRoot()
        {
            string currentDir = System.IO.Directory.GetCurrentDirectory();
            while (currentDir != null && !System.IO.File.Exists(System.IO.Path.Combine(currentDir, "ETL-SQL.slnx")))
                currentDir = System.IO.Directory.GetParent(currentDir)?.FullName;
            return currentDir ?? System.IO.Directory.GetCurrentDirectory();
        }

        [Fact]
        public async Task PassesWhenJsonSpecMatches()
        {
            var ev = GetEvaluator();
            string specPath = System.IO.Path.Combine(FindProjectRoot(), "tests", "testdata", "Specs", "customer_spec.json").Replace("\\", "/");

            await ev.Evaluate(Parse($@"
                CREATE TABLE #data (
                    customer_id INT, 
                    first_name VARCHAR(50), 
                    last_name VARCHAR(50), 
                    email VARCHAR(255), 
                    signup_date DATE, 
                    is_active BIT
                );
                EXPECT SCHEMA #data FROM '{specPath}';
            "));
        }

        [Fact]
        public async Task ThrowsOnMissingColumnWithJsonSpec()
        {
            var ev = GetEvaluator();
            string specPath = System.IO.Path.Combine(FindProjectRoot(), "tests", "testdata", "Specs", "customer_spec.json").Replace("\\", "/");

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
                await ev.Evaluate(Parse($@"
                    CREATE TABLE #data (
                        customer_id INT, 
                        first_name VARCHAR(50)
                    );
                    EXPECT SCHEMA #data FROM '{specPath}';
                ")));
            Assert.Contains("MISSING", ex.Message);
            Assert.Contains("last_name", ex.Message);
        }

        [Fact]
        public async Task ThrowsOnTypeFamilyMismatchWithJsonSpec()
        {
            var ev = GetEvaluator();
            string specPath = System.IO.Path.Combine(FindProjectRoot(), "tests", "testdata", "Specs", "customer_spec.json").Replace("\\", "/");

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
                await ev.Evaluate(Parse($@"
                    CREATE TABLE #data (
                        customer_id VARCHAR, 
                        first_name VARCHAR(50), 
                        last_name VARCHAR(50), 
                        email VARCHAR(255), 
                        signup_date DATE, 
                        is_active BIT
                    );
                    EXPECT SCHEMA #data FROM '{specPath}';
                ")));
            Assert.Contains("TYPE DRIFT", ex.Message);
            Assert.Contains("customer_id", ex.Message);
        }

        [Fact]
        public async Task WarnOnDriftWithJsonSpecLogsWarningInsteadOfThrowing()
        {
            var ev = GetEvaluator();
            string specPath = System.IO.Path.Combine(FindProjectRoot(), "tests", "testdata", "Specs", "customer_spec.json").Replace("\\", "/");

            await ev.Evaluate(Parse($@"
                CREATE TABLE #data (
                    customer_id VARCHAR, 
                    first_name VARCHAR(50), 
                    last_name VARCHAR(50), 
                    email VARCHAR(255), 
                    signup_date DATE, 
                    is_active BIT
                );
                EXPECT SCHEMA #data FROM '{specPath}' ON DRIFT WARN;
            "));
        }
    }
}
