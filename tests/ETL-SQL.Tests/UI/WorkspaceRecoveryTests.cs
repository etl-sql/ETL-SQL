using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Session capture: dirty buffers snapshot, clean ones don't, and secrets are excluded.</summary>
    public class WorkspaceRecoveryTests
    {
        static WorkspaceRecoveryTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        private static ConsoleEditor NewEditor()
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            return e;
        }

        [Fact]
        public void CaptureSession_DirtyPlainBuffer_SnapshotsRecoveryText()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "SELECT 1;" });
            e.MarkDirty();

            var tab = e.CaptureSession().Tabs[0];
            Assert.True(tab.IsDirty);
            Assert.Equal("SELECT 1;", tab.RecoveryText);
        }

        [Fact]
        public void CaptureSession_CleanBuffer_HasNoRecoveryText()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "SELECT 1;" });
            // not dirty → reopen from disk, nothing to recover

            var tab = e.CaptureSession().Tabs[0];
            Assert.False(tab.IsDirty);
            Assert.Null(tab.RecoveryText);
        }

        [Fact]
        public void CaptureSession_SecretBearingBuffer_IsNeverSnapshotted()
        {
            var e = NewEditor();
            // A connection with a literal password — the kind of secret we must not write to disk.
            var script = "CREATE CONNECTION m AS MSSQL(SERVER='s', PASSWORD='secret');";
            e._buffer.Load(new[] { script });
            e.MarkDirty();

            // Confirm this script actually trips the secret guard in the default config.
            var security = new ETL_SQL.Services.SecurityService(
                ETL_SQL.TUI.Program.ServiceProvider.GetService(typeof(ETL_SQL.Common.ILogger)) as ETL_SQL.Common.ILogger
                ?? throw new System.InvalidOperationException("logger"));
            Assert.True(security.RequiresSavePassword(script));

            var tab = e.CaptureSession().Tabs[0];
            Assert.True(tab.IsDirty);
            Assert.Null(tab.RecoveryText); // excluded — secret never persisted
        }
    }
}
