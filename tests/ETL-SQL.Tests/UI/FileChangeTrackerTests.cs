using Xunit;
using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>External-modification detection used to guard saves against clobbering.</summary>
    public class FileChangeTrackerTests
    {
        // Minimal in-memory IFileSystem whose mtime we can move at will.
        private sealed class FakeFs : IFileSystem
        {
            public readonly Dictionary<string, DateTime> Times = new(StringComparer.OrdinalIgnoreCase);
            public bool Exists(string path) => Times.ContainsKey(path);
            public DateTime GetLastWriteTimeUtc(string path) => Times[path];
            public string[] ReadAllLines(string path) => Array.Empty<string>();
            public string ReadAllText(string path) => "";
            public void WriteAllText(string path, string contents) => Times[path] = DateTime.UtcNow;
            public string[] GetDirectories(string path) => Array.Empty<string>();
            public string[] GetFiles(string path, string searchPattern) => Array.Empty<string>();
        }

        [Fact]
        public void NoChange_AfterRecord_ReturnsFalse()
        {
            var fs = new FakeFs { Times = { ["a.etlsql"] = new DateTime(2026, 1, 1) } };
            var t = new FileChangeTracker(fs);
            t.Record("a.etlsql");
            Assert.False(t.HasChangedExternally("a.etlsql"));
        }

        [Fact]
        public void ExternalWrite_AfterRecord_IsDetected()
        {
            var fs = new FakeFs { Times = { ["a.etlsql"] = new DateTime(2026, 1, 1) } };
            var t = new FileChangeTracker(fs);
            t.Record("a.etlsql");

            fs.Times["a.etlsql"] = new DateTime(2026, 1, 2); // another program rewrote it
            Assert.True(t.HasChangedExternally("a.etlsql"));
        }

        [Fact]
        public void Untracked_File_IsNotReportedChanged()
        {
            var fs = new FakeFs { Times = { ["a.etlsql"] = new DateTime(2026, 1, 1) } };
            var t = new FileChangeTracker(fs);
            // never recorded
            Assert.False(t.HasChangedExternally("a.etlsql"));
        }

        [Fact]
        public void MissingFile_IsNotReportedChanged()
        {
            var t = new FileChangeTracker(new FakeFs());
            Assert.False(t.HasChangedExternally("ghost.etlsql"));
            Assert.False(t.HasChangedExternally(null));
        }

        [Fact]
        public void Record_AfterChange_ResetsBaseline()
        {
            var fs = new FakeFs { Times = { ["a.etlsql"] = new DateTime(2026, 1, 1) } };
            var t = new FileChangeTracker(fs);
            t.Record("a.etlsql");
            fs.Times["a.etlsql"] = new DateTime(2026, 1, 2);

            t.Record("a.etlsql"); // e.g. we just saved — adopt the new mtime
            Assert.False(t.HasChangedExternally("a.etlsql"));
        }
    }
}
