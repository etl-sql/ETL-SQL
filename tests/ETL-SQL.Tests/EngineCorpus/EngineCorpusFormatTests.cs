using System;
using Xunit;

namespace ETL_SQL.Tests.EngineCorpus;

public class EngineCorpusFormatTests
{
    [Fact]
    public void Parse_RecognizesPortalAndFileAssertions()
    {
        var records = EngineCorpusParser.Parse(
        [
            "portal",
            "assert file exists output/data.parquet",
            "assert file contains output/lineage.json eventType"
        ]);

        Assert.Collection(
            records,
            record => Assert.Equal(EngineRecordKind.Portal, record.Kind),
            record =>
            {
                Assert.Equal(EngineRecordKind.FileExists, record.Kind);
                Assert.Equal("output/data.parquet", record.Name);
            },
            record =>
            {
                Assert.Equal(EngineRecordKind.FileContains, record.Kind);
                Assert.Equal("output/lineage.json", record.Name);
                Assert.Equal("eventType", record.Body);
            });
    }

    [Theory]
    [InlineData("assert file contains output.json")]
    [InlineData("assert file contains  expected")]
    public void Parse_RejectsIncompleteFileContainsDirective(string directive)
    {
        var error = Assert.Throws<FormatException>(() => EngineCorpusParser.Parse([directive]));

        Assert.Contains("requires '<path> <expected text>'", error.Message, StringComparison.Ordinal);
    }
}
