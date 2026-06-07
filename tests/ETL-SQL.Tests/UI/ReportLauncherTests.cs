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
        public void BuildServeProcess_InvokesServeWithScript_OutputRedirected()
        {
            var psi = ReportLauncher.BuildServeProcess("etl-sql.exe", "C:\\reports\\sales.rptsql");

            Assert.Equal("etl-sql.exe", psi.FileName);
            Assert.Equal(new[] { "serve", "C:\\reports\\sales.rptsql" }, psi.ArgumentList.ToArray());
            Assert.False(psi.UseShellExecute);
            Assert.True(psi.RedirectStandardOutput);
            Assert.True(psi.RedirectStandardError);
            Assert.True(psi.CreateNoWindow);
        }
    }
}
