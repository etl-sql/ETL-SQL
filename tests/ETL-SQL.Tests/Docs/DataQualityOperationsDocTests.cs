using System.Text.RegularExpressions;
using Xunit;

namespace ETL_SQL.Tests.Docs;

public sealed class DataQualityOperationsDocTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Guide() => Regex.Replace(File.ReadAllText(Path.Combine(
        RepoRoot, "docs", "guides", "data-quality", "automating-quality-gates.md")), @"\s+", " ");

    [Fact]
    public void GuidePinsZeroServiceAndLocalOrchestratorPatterns()
    {
        var guide = Guide();
        Assert.Contains("Windows Task Scheduler action", guide);
        Assert.Contains("Cron entry", guide);
        Assert.Contains("--output-json", guide);
        Assert.Contains("if: always()", guide);
        Assert.Contains("default SQLite store", guide);
        Assert.Contains("does not require Portal", guide);
        Assert.Contains("eng.data_quality_status", guide);
        Assert.Contains("recovery notifications", guide);
        Assert.Contains("SMTP and WEBHOOK connections are optional", guide);
        Assert.Contains("non-zero process exit", guide);
    }
}
