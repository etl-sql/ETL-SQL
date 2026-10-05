using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.Tests.Orchestration;

public sealed class ProcessJobResultDiagnosticsTests
{
    [Fact]
    public void FailedCliCompletionRetainsTheErrorAndMetricsWithoutExposingCredentials()
    {
        const string stdout = """
            {"type":"done","exitCode":1,"rowsProcessed":13,"peakMemoryBytes":4096,"cpuTimeSeconds":1.25}
            """;
        const string stderr = "Execution error: login failed; PASSWORD=private-diagnostic-password\r\n";

        var result = ProcessJobExecutor.ParseResult(1, stdout, stderr, 0, 0);

        Assert.False(result.Success);
        Assert.Contains("Execution error", result.ErrorMessage);
        Assert.DoesNotContain("private-diagnostic-password", result.ErrorMessage);
        Assert.DoesNotContain("\r", result.ErrorMessage);
        Assert.DoesNotContain("\n", result.ErrorMessage);
        Assert.Equal(13, result.RowsProcessed);
        Assert.Equal(4096, result.PeakMemoryBytes);
        Assert.Equal(1.25, result.CpuTimeSeconds);
    }

    [Fact]
    public void FailedCompletionWithoutAnErrorStillNamesTheExitCode()
    {
        var result = ProcessJobExecutor.ParseResult(1, "{\"type\":\"done\",\"exitCode\":1}", "", 0, 0);

        Assert.False(result.Success);
        Assert.Contains("code 1", result.ErrorMessage);
    }

    [Fact]
    public void LegacyErrorEnvelopesAlsoRedactCredentials()
    {
        var result = ProcessJobExecutor.ParseResult(1,
            "{\"success\":false,\"error\":\"login failed; PASSWORD=private-envelope-password\"}", "", 0, 0);

        Assert.False(result.Success);
        Assert.Contains("login failed", result.ErrorMessage);
        Assert.DoesNotContain("private-envelope-password", result.ErrorMessage);
    }
}
