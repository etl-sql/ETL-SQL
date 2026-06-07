using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Launches the report preview for the TUI. It runs the ReportPlayer web app directly
    /// (not the `serve` CLI verb), because re-invoking the full ETL-SQL app headless trips
    /// console-handle setup ("The handle is invalid"). ReportPlayer just writes plain lines,
    /// including REPORT_URL=&lt;actual bound url&gt;, which we parse for the URL to open/show.
    /// </summary>
    public static class ReportLauncher
    {
        /// <summary>
        /// Locates the ReportPlayer: a sibling exe (production), the dev build next to this
        /// app's bin, or — as a last resort — `dotnet run` against the project (which builds).
        /// Returns null when it can't be found.
        /// </summary>
        public static (string exe, string[] prefixArgs)? FindReportPlayer()
        {
            string exeName = OperatingSystem.IsWindows() ? "ETL-SQL.ReportPlayer.exe" : "ETL-SQL.ReportPlayer";
            string appPath = Environment.ProcessPath ?? "";

            // 1. Sibling executable (production install — both binaries in one directory).
            string exeDir = Path.GetDirectoryName(appPath) ?? ".";
            string sibling = Path.Combine(exeDir, exeName);
            if (File.Exists(sibling)) return (sibling, Array.Empty<string>());

            // 2. Dev: the ReportPlayer's own build, by convention next to the App's bin.
            if (appPath.Contains("ETL-SQL.App"))
            {
                string candidate = appPath.Replace("ETL-SQL.App", "ETL-SQL.ReportPlayer");
                if (File.Exists(candidate)) return (candidate, Array.Empty<string>());
            }

            // 3. Dev fallback: walk up to the solution root and `dotnet run` the project.
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (Directory.GetFiles(dir, "*.slnx").Length > 0 || Directory.GetFiles(dir, "*.sln").Length > 0)
                {
                    string project = Path.Combine(dir, "src", "ETL-SQL.ReportPlayer");
                    if (Directory.Exists(project))
                        return ("dotnet", new[] { "run", "--project", project, "--" });
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == dir) break;
                dir = parent;
            }

            return null;
        }

        public static ProcessStartInfo BuildServeProcess(string exePath, string[] prefixArgs, string scriptFullPath)
            => BuildPlayerProcess(exePath, prefixArgs, scriptFullPath);

        /// <summary>Multi-report mode: serve every report listed in a reports.json manifest.</summary>
        public static ProcessStartInfo BuildManifestProcess(string exePath, string[] prefixArgs, string manifestPath)
            => BuildPlayerProcess(exePath, prefixArgs, "--manifest", manifestPath);

        private static ProcessStartInfo BuildPlayerProcess(string exePath, string[] prefixArgs, params string[] playerArgs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var p in prefixArgs) psi.ArgumentList.Add(p);
            foreach (var a in playerArgs) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--no-browser"); // the TUI opens the browser itself, at the reported URL
            return psi;
        }

        /// <summary>Extracts the URL from a "REPORT_URL=&lt;url&gt;" line the player prints, or null.</summary>
        public static string? ParseReportUrl(string? line)
        {
            var m = Regex.Match(line ?? "", @"^\s*REPORT_URL=(\S+)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>First non-empty, non-boilerplate line from captured process output.</summary>
        public static string? FirstMeaningfulLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("ReportPlayer: serving", StringComparison.OrdinalIgnoreCase)) continue;
                return line;
            }
            return null;
        }

        public static void OpenBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { /* best effort — the URL is also shown for the user to click */ }
        }
    }
}
