using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    /// <summary>
    /// Parser behaviour for skipif/onlyif directives. These run in the default lane (not gated by
    /// ETL_SQL_RUN_SLT) because a misparsed directive silently changes what the corpus runs.
    /// </summary>
    public class SltParserTests
    {
        [Fact]
        public void StackedDirectives_AllGuardTheFollowingRecord()
        {
            var records = Parse(
                "skipif mysql",
                "skipif etlsql",
                "statement ok",
                "SELECT 1",
                "",
                "onlyif etlsql",
                "onlyif mssql",
                "query I nosort",
                "SELECT 2",
                "----",
                "2",
                "",
                "skipif mysql",
                "skipif postgresql",
                "statement ok",
                "SELECT 3");

            Assert.Equal(3, records.Length);

            Assert.Equal(SltRecordType.Statement, records[0].Type);
            Assert.Equal(2, records[0].Conditions.Count);
            Assert.False(records[0].AppliesTo("etlsql"));
            Assert.False(records[0].AppliesTo("mysql"));
            Assert.True(records[0].AppliesTo("sqlite"));

            Assert.Equal(SltRecordType.Query, records[1].Type);
            Assert.Equal("SELECT 2", records[1].Sql);
            Assert.Equal("2", records[1].ExpectedResult!.Trim());
            Assert.False(records[1].AppliesTo("etlsql"));

            Assert.True(records[2].AppliesTo("etlsql"));
            Assert.Equal("SELECT 3", records[2].Sql);
        }

        [Fact]
        public void Directives_DoNotLeakPastTheRecordTheyGuard()
        {
            var records = Parse(
                "onlyif mssql",
                "statement ok",
                "SELECT 1",
                "",
                "statement ok",
                "SELECT 2");

            Assert.Equal(2, records.Length);
            Assert.False(records[0].AppliesTo("etlsql"));
            Assert.Empty(records[1].Conditions);
            Assert.True(records[1].AppliesTo("etlsql"));
        }

        [Fact]
        public void GuardedHalt_IsReportedWithItsConditionsAndParsingContinues()
        {
            var records = Parse(
                "onlyif mssql",
                "halt",
                "",
                "statement ok",
                "SELECT 1",
                "",
                "halt",
                "",
                "statement ok",
                "SELECT 2");

            Assert.Equal(new[] { SltRecordType.Halt, SltRecordType.Statement, SltRecordType.Halt, SltRecordType.Statement },
                records.Select(r => r.Type).ToArray());
            Assert.False(records[0].AppliesTo("etlsql"));
            Assert.True(records[2].AppliesTo("etlsql"));
        }

        private static SltRecord[] Parse(params string[] lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"slt-parser-{Guid.NewGuid():N}.test");
            File.WriteAllLines(path, lines);
            try
            {
                return SltParser.ParseFile(path).ToArray();
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
