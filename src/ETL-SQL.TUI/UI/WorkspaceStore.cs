using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.TUI.UI
{
    /// <summary>One open tab's persisted state. Clean tabs reopen from <see cref="FilePath"/>;
    /// dirty tabs carry a <see cref="RecoveryText"/> snapshot (omitted when it holds secrets).</summary>
    public sealed class WorkspaceTab
    {
        public string? FilePath { get; set; }
        public bool IsDirty { get; set; }
        public int CursorLine { get; set; }
        public int CursorColumn { get; set; }
        public int ScrollLine { get; set; }
        public int ScrollCol { get; set; }
        public string? RecoveryText { get; set; }
    }

    /// <summary>The editor workspace for one working directory: which tabs were open and where.</summary>
    public sealed class WorkspaceSession
    {
        public string WorkingDirectory { get; set; } = "";
        public int ActiveTab { get; set; }
        public List<WorkspaceTab> Tabs { get; set; } = new();
        public DateTime SavedUtc { get; set; }
    }

    /// <summary>
    /// Persists/loads the editor workspace keyed by working directory, and tracks clean vs unclean
    /// shutdown via a sentinel file (created on start, removed on clean exit). Pure I/O — no engine.
    /// Recovery files are locked to the owner; secrets are never written (caller's responsibility).
    /// </summary>
    public class WorkspaceStore
    {
        /// <summary>Overridable for tests; defaults to %APPDATA%/etl-sql/workspace.</summary>
        public static string BaseDir { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "etl-sql", "workspace");

        private static string KeyFor(string workingDirectory)
        {
            string norm = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
            return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        }

        private static string SessionPath(string workingDirectory) =>
            Path.Combine(BaseDir, $"session_{KeyFor(workingDirectory)}.json");

        private static string SentinelPath(string workingDirectory) =>
            Path.Combine(BaseDir, $"session_{KeyFor(workingDirectory)}.lock");

        public void Save(WorkspaceSession session)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(BaseDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                var path = SessionPath(session.WorkingDirectory);
                File.WriteAllText(path, JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { /* persistence is best-effort */ }
        }

        public WorkspaceSession? Load(string workingDirectory)
        {
            try
            {
                var path = SessionPath(workingDirectory);
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<WorkspaceSession>(File.ReadAllText(path));
            }
            catch { }
            return null;
        }

        /// <summary>True if the previous run for this directory did not exit cleanly (sentinel present).</summary>
        public bool WasUncleanShutdown(string workingDirectory) => File.Exists(SentinelPath(workingDirectory));

        /// <summary>Drops the start-of-run sentinel; its presence at next launch signals a crash.</summary>
        public void MarkRunning(string workingDirectory)
        {
            try
            {
                Directory.CreateDirectory(BaseDir);
                File.WriteAllText(SentinelPath(workingDirectory), DateTime.UtcNow.ToString("o"));
            }
            catch { }
        }

        /// <summary>Removes the sentinel on a clean exit.</summary>
        public void MarkCleanExit(string workingDirectory)
        {
            try { File.Delete(SentinelPath(workingDirectory)); } catch { }
        }
    }
}
