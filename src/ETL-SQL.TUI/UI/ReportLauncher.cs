using System.Diagnostics;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Builds the child-process invocations for report actions launched from the TUI
    /// (serve, and later publish/export). The TUI re-invokes the same ETL-SQL executable
    /// with the relevant CLI verb; output is redirected so it never disturbs the terminal UI.
    /// </summary>
    public static class ReportLauncher
    {
        public static ProcessStartInfo BuildServeProcess(string exePath, string scriptFullPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("serve");
            psi.ArgumentList.Add(scriptFullPath);
            return psi;
        }
    }
}
