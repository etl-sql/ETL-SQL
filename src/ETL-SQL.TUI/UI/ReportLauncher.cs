using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Builds the child-process invocation for report actions launched from the TUI
    /// (serve, and later publish/export). The TUI re-invokes the same ETL-SQL application
    /// with the relevant CLI verb; output is redirected so the child never disturbs the
    /// terminal UI, and the actual bound URL is read from the player's "REPORT_URL=" line.
    /// </summary>
    public static class ReportLauncher
    {
        /// <summary>
        /// Resolves how to re-invoke this application. When running under the dotnet host
        /// (e.g. `dotnet run` in dev), the entry assembly DLL is passed as the first arg so
        /// `dotnet &lt;app.dll&gt; serve …` works; otherwise the app exe is invoked directly.
        /// </summary>
        public static (string exe, string[] prefixArgs) ResolveSelfInvocation()
        {
            string exe = Environment.ProcessPath ?? "dotnet";
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var dll = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(dll))
                    return (exe, new[] { dll });
            }
            return (exe, Array.Empty<string>());
        }

        public static ProcessStartInfo BuildServeProcess(string exePath, string[] prefixArgs, string scriptFullPath)
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

        /// <summary>First non-empty, non-boilerplate line from captured process output.</summary>
        public static string? FirstMeaningfulLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("Starting report preview", StringComparison.OrdinalIgnoreCase)) continue;
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
