using System;
using System.Collections.Generic;
using ETL_SQL.Common;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Remembers each open file's last-write time at load/save and reports when the file has
    /// since been changed by another program, so the editor can warn before overwriting it.
    /// </summary>
    public class FileChangeTracker
    {
        private readonly IFileSystem _fs;
        private readonly Dictionary<string, DateTime> _times = new(StringComparer.OrdinalIgnoreCase);

        public FileChangeTracker(IFileSystem fs) => _fs = fs;

        /// <summary>Snapshots the file's current modified time (no-op for missing/blank paths).</summary>
        public void Record(string? path)
        {
            if (!string.IsNullOrEmpty(path) && _fs.Exists(path))
                _times[path] = _fs.GetLastWriteTimeUtc(path);
        }

        /// <summary>
        /// True when a tracked file's timestamp changes or the file is deleted.
        /// </summary>
        public bool HasChangedExternally(string? path)
        {
            if (string.IsNullOrEmpty(path) || !_times.TryGetValue(path, out var recorded)) return false;
            return !_fs.Exists(path) || _fs.GetLastWriteTimeUtc(path) != recorded;
        }

        public void Forget(string? path)
        {
            if (!string.IsNullOrEmpty(path)) _times.Remove(path);
        }
    }
}
