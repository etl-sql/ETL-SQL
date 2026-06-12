using System.Linq;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// The TUI serves a report by re-invoking the ETL-SQL executable with the `serve` verb;
    /// output is redirected so the child process never disturbs the terminal UI.
    /// </summary>
    public class ReportLauncherTests
    {
        [Fact]
        public void BuildServeProcess_RunsPlayerWithScript_OutputRedirected()
        {
            var psi = ReportLauncher.BuildServeProcess("ETL-SQL.ReportPlayer.exe", System.Array.Empty<string>(), "C:\\reports\\sales.rptsql");

            Assert.Equal("ETL-SQL.ReportPlayer.exe", psi.FileName);
            Assert.Equal(new[] { "C:\\reports\\sales.rptsql", "--no-browser" }, psi.ArgumentList.ToArray());
            Assert.False(psi.UseShellExecute);
            Assert.True(psi.RedirectStandardOutput);
            Assert.True(psi.RedirectStandardError);
            Assert.True(psi.CreateNoWindow);
        }

        [Fact]
        public void BuildServeProcess_SupportsDotnetRunPrefix()
        {
            var psi = ReportLauncher.BuildServeProcess("dotnet", new[] { "run", "--project", "P", "--" }, "/r/sales.rptsql");
            Assert.Equal(new[] { "run", "--project", "P", "--", "/r/sales.rptsql", "--no-browser" }, psi.ArgumentList.ToArray());
        }

        [Fact]
        public void BuildManifestProcess_PassesManifestFlag()
        {
            var psi = ReportLauncher.BuildManifestProcess("ETL-SQL.ReportPlayer.exe", System.Array.Empty<string>(), "C:\\r\\.etlsql-reports.json");
            Assert.Equal(new[] { "--manifest", "C:\\r\\.etlsql-reports.json", "--no-browser" }, psi.ArgumentList.ToArray());
        }

        [Fact]
        public void FindReportPlayer_ResolvesFromTheRepo()
        {
            // The test runs inside the repo, so the dev fallback should resolve the project.
            var found = ReportLauncher.FindReportPlayer();
            Assert.NotNull(found);
            Assert.False(string.IsNullOrEmpty(found!.Value.exe));
        }

        [Theory]
        [InlineData("Unhandled exception: boom", "Unhandled exception: boom")]
        [InlineData("ReportPlayer: serving sales.rptsql\nReal error here", "Real error here")]
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
