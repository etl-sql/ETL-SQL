using System;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    /// <summary>
    /// The value formatter decides what a corpus query "returned". It used to answer "0" or "0.000"
    /// for any text in a numeric column and "NULL" for NaN in an integer column, so a wrong result
    /// could still match its expected value.
    /// </summary>
    public class SltValueFormatTests
    {
        [Theory]
        [InlineData('I')]
        [InlineData('R')]
        public void NonNumericTextInANumericColumn_Fails(char type)
        {
            var failure = Assert.Throws<FormatException>(() => SltRunner.FormatSltValue("abc", type));
            Assert.Contains("non-numeric value 'abc'", failure.Message);
        }

        [Fact]
        public void NumericValuesAndNumericText_FormatByColumnType()
        {
            Assert.Equal("3", SltRunner.FormatSltValue(3.9m, 'I'));
            Assert.Equal("-3", SltRunner.FormatSltValue("-3.9", 'I'));
            Assert.Equal("3.900", SltRunner.FormatSltValue(3.9m, 'R'));
            Assert.Equal("1.500", SltRunner.FormatSltValue("1.5", 'R'));
            Assert.Equal("1", SltRunner.FormatSltValue(true, 'I'));
            Assert.Equal("0.000", SltRunner.FormatSltValue(false, 'R'));
            Assert.Equal("3", SltRunner.FormatSltValue(3m, 'T'));
            Assert.Equal("abc", SltRunner.FormatSltValue("abc", 'T'));
        }

        [Fact]
        public void NullIsNotTheSameAsNaNOrInfinity()
        {
            Assert.Equal("NULL", SltRunner.FormatSltValue(null, 'I'));
            Assert.Equal("NULL", SltRunner.FormatSltValue(DBNull.Value, 'R'));
            Assert.Equal("NaN", SltRunner.FormatSltValue(double.NaN, 'I'));
            Assert.Equal("Inf", SltRunner.FormatSltValue(double.PositiveInfinity, 'I'));
            Assert.Equal("-Inf", SltRunner.FormatSltValue(double.NegativeInfinity, 'R'));
        }
    }

    /// <summary>
    /// The corpus runner builds its own evaluator. It must offer every statement handler the engine
    /// ships, and the ones it cannot build are a reviewed list, not a silent gap.
    /// </summary>
    public class SltRunnerHandlerTests
    {
        [Fact]
        public void HandlersTheRunnerCannotBuild_AreTheReviewedList()
        {
            using var runner = new SltRunner();

            // These need the job store, the bundle store, the Portal or the lineage catalog. None is
            // a SQL statement the corpus can exercise. A new name here needs a reason.
            var reviewed = new[]
            {
                "AlterJobStatementHandler",
                "AlterPortalSubscriptionHandler",
                "CreateJobStatementHandler",
                "CreatePortalSubscriptionHandler",
                "DisableJobStatementHandler",
                "DropJobStatementHandler",
                "EnableJobStatementHandler",
                "ExportScriptStatementHandler",
                "PublishBundleStatementHandler",
                "ShowBundleDependenciesStatementHandler",
                "ShowBundleFilesStatementHandler",
                "ShowBundleVersionsStatementHandler",
                "ShowHostMetricsStatementHandler",
                "ShowJobHistoryStatementHandler",
                "ShowJobStateStatementHandler",
                "ShowJobsStatementHandler",
                "ShowLineageHistoryForJobStatementHandler",
                "ShowLineageHistoryForMissingTagsStatementHandler",
                "ShowLineageHistoryForTableStatementHandler",
                "ShowLineageHistoryForTagStatementHandler",
                "ShowProtectedDataStatementHandler",
                "ShowPublishedBundlesStatementHandler",
                "TriggerJobStatementHandler",
            };

            Assert.Equal(reviewed, runner.UnavailableHandlers);
        }
    }
}
