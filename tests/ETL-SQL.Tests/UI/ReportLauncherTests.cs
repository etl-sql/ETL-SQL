using Xunit;
using System.Linq;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// The TUI serves a report by re-invoking the ETL-SQL executable with the `serve` verb;
    /// output is redirected so the child process never disturbs the terminal UI.
    /// </summary>
    public class ReportLauncherTests
    {
        [Fact]
        public void BuildServeProcess_InvokesServe_OutputRedirected()
        {
            var psi = ReportLauncher.BuildServeProcess("etl-sql.exe", System.Array.Empty<string>(), "C:\\reports\\sales.rptsql");

            Assert.Equal("etl-sql.exe", psi.FileName);
            Assert.Equal(new[] { "serve", "C:\\reports\\sales.rptsql", "--no-browser" }, psi.ArgumentList.ToArray());
            Assert.False(psi.UseShellExecute);
            Assert.True(psi.RedirectStandardOutput);
            Assert.True(psi.RedirectStandardError);
            Assert.True(psi.CreateNoWindow);
        }

        [Fact]
        public void BuildServeProcess_PrependsDotnetDll_WhenHostedByDotnet()
        {
            var psi = ReportLauncher.BuildServeProcess("dotnet", new[] { "/app/ETL-SQL.App.dll" }, "/r/sales.rptsql");
            Assert.Equal(new[] { "/app/ETL-SQL.App.dll", "serve", "/r/sales.rptsql", "--no-browser" }, psi.ArgumentList.ToArray());
        }

        [Fact]
        public void ResolveSelfInvocation_ReturnsAnExecutable()
        {
            var (exe, _) = ReportLauncher.ResolveSelfInvocation();
            Assert.False(string.IsNullOrEmpty(exe));
        }

        [Theory]
        [InlineData("Failed to start the ReportPlayer process.", "Failed to start the ReportPlayer process.")]
        [InlineData("Starting report preview server...\nReal error here", "Real error here")]
        [InlineData("   \n  \n", null)]
        public void FirstMeaningfulLine_SkipsBoilerplate(string text, string? expected)
        {
            Assert.Equal(expected, ReportLauncher.FirstMeaningfulLine(text));
        }

        [Theory]
        [InlineData("REPORT_URL=http://localhost:5173", "http://localhost:5173")]
        [InlineData("  REPORT_URL=http://127.0.0.1:8080/  ", "http://127.0.0.1:8080/")]
        [InlineData("Dashboard: http://localhost:1", null)]
        [InlineData("REPORT_URL=", null)]
        public void ParseReportUrl_ExtractsTheBoundUrl(string line, string? expected)
        {
            Assert.Equal(expected, ReportLauncher.ParseReportUrl(line));
        }
    }
}
