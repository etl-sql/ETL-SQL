using ETL_SQL.Core.Security;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class HelperPreviewPolicyTests
{
    [Theory]
    [InlineData("DELETE FROM m.Users;")]
    [InlineData("WAITFOR DELAY '00:00:30';")]
    [InlineData("SET OPERATOR_MEMORY_GRANT = 4096;")]
    [InlineData("SELECT 1 AS Id INTO m.Users;")]
    public void UnavailablePredecessorContextIsRefusedBeforeExecution(string predecessor)
    {
        var error = Assert.Throws<ArgumentException>(() => HelperPreviewPolicy.Compose(
            "SELECT @minimum AS Minimum FROM #stage;", predecessor + "\nSELECT 1;", 2));
        Assert.Contains("Nothing was executed", error.Message);
    }

    [Theory]
    [InlineData("EXECUTE db BEGIN DELETE FROM Users; END;")]
    [InlineData("EXECUTE db BEGIN SELECT 1; DELETE FROM Users; END;")]
    [InlineData("SELECT 1 AS Id INTO #stage;")]
    public void ThePreviewTargetCannotMutate(string query) =>
        Assert.Throws<ArgumentException>(() => HelperPreviewPolicy.Compose(query, null, null));

    [Fact]
    public void ExplicitContextIncludesVariablesAndStagesButNotLaterStatements()
    {
        var text = HelperPreviewPolicy.Compose("SELECT Id FROM #stage WHERE Id >= @minimum;",
            "DECLARE @minimum INT = 2;\nSELECT 2 AS Id INTO #stage;\nSELECT 99;\nDELETE FROM m.Users;", 3);
        Assert.Contains("DECLARE @minimum", text);
        Assert.Contains("INTO #stage", text);
        Assert.DoesNotContain("99", text);
        Assert.DoesNotContain("DELETE", text);
    }
}
