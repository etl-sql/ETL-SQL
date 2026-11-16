using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    /// <summary>
    /// A <c>statement error</c> record must fail when its statement succeeds. For a long time it
    /// could not: the runner raised "expected failure" inside the same <c>try</c> whose
    /// <c>catch</c> swallowed every exception of a record that expected one, so each such record
    /// passed whatever the engine did.
    /// </summary>
    public class SltRunnerStatementErrorTests
    {
        private static SltRecord Statement(string sql, bool expectSuccess, string? expectedError = null) => new()
        {
            Type = SltRecordType.Statement,
            Sql = sql,
            ExpectSuccess = expectSuccess,
            ExpectedError = expectedError,
            LineNumber = 7
        };

        [Fact]
        public async Task ExpectedError_ButTheStatementSucceeds_Fails()
        {
            using var runner = new SltRunner();

            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => runner.RunTestAsync(Statement("CREATE TABLE #expected_error (id INT);", expectSuccess: false)));

            Assert.Contains("Line 7: Expected failure, but statement succeeded", failure.Message);
        }

        [Fact]
        public async Task ExpectedError_AndTheStatementFails_Passes()
        {
            using var runner = new SltRunner();

            await runner.RunTestAsync(Statement("SELECT * FROM no_such_table_for_statement_error;", expectSuccess: false));
        }

        [Fact]
        public async Task ExpectedError_AndTheStatementDoesNotParse_Passes()
        {
            using var runner = new SltRunner();

            await runner.RunTestAsync(Statement("CREATE TABLE (;", expectSuccess: false));
        }

        [Fact]
        public async Task ExpectedOk_AndTheStatementDoesNotParse_Fails()
        {
            using var runner = new SltRunner();

            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => runner.RunTestAsync(Statement("CREATE TABLE (;", expectSuccess: true)));

            Assert.Contains("Line 7: Statement failed", failure.Message);
        }

        [Fact]
        public async Task ExpectedErrorText_MustAppearInTheMessage()
        {
            using var runner = new SltRunner();
            const string sql = "SELECT * FROM no_such_table_for_statement_error;";

            await runner.RunTestAsync(Statement(sql, expectSuccess: false, expectedError: "NO_SUCH_TABLE_FOR_STATEMENT_ERROR"));

            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => runner.RunTestAsync(Statement(sql, expectSuccess: false, expectedError: "division by zero")));
            Assert.Contains("Expected an error containing 'division by zero'", failure.Message);
        }

        [Fact]
        public void Parser_ReadsTheExpectedErrorText()
        {
            var path = Path.Combine(Path.GetTempPath(), $"slt-statement-error-{Guid.NewGuid():N}.test");
            File.WriteAllText(path, "statement error\nSELECT 1;\n\nstatement error no such table\nSELECT 2;\n\nstatement ok\nSELECT 3;\n");
            try
            {
                var records = SltParser.ParseFile(path).ToList();

                Assert.Equal(new[] { false, false, true }, records.Select(r => r.ExpectSuccess));
                Assert.Equal(new string?[] { null, "no such table", null }, records.Select(r => r.ExpectedError));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
