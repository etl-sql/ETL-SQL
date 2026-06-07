using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Builds the child-process invocation for report actions launched from the TUI
    /// (serve, and later publish/export). The TUI re-invokes the same ETL-SQL executable
    /// with the relevant CLI verb; output is redirected so the child never disturbs the
    /// terminal UI, and the actual bound URL is read from the player's "REPORT_URL=" line
    /// (the port is OS-assigned, so we can't construct the URL ourselves).
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
            psi.ArgumentList.Add("--no-browser"); // the TUI opens the browser itself, at the reported URL
            return psi;
        }

        /// <summary>Extracts the URL from a "REPORT_URL=&lt;url&gt;" line the player prints, or null.</summary>
        public static string? ParseReportUrl(string? line)
        {
            var m = Regex.Match(line ?? "", @"^\s*REPORT_URL=(\S+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        public static void OpenBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { /* best effort — the URL is also shown for the user to click */ }
        }
    }
}
