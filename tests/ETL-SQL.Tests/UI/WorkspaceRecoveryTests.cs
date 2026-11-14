using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Services;
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

        [Fact]
        public async Task LoadAsync_DoesNotDecryptEncStringsOnOpen()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"etlsql-tui-load-{Guid.NewGuid():N}.etlsql");
            try
            {
                var security = new SecurityService(
                    ETL_SQL.TUI.Program.ServiceProvider.GetService(typeof(ILogger)) as ILogger
                    ?? throw new InvalidOperationException("logger"));
                var raw = "USE PASSWORD = 'pw-1';\nCREATE CONNECTION m AS MSSQL(SERVER='s', PASSWORD='secret');";
                var sealedContent = security.SecureScriptForSave(raw, "pw-1");
                Assert.Contains("ENC:", sealedContent, StringComparison.Ordinal);
                await File.WriteAllTextAsync(tempFile, sealedContent);

                var handler = new EditorFileHandler(new PhysicalFileSystem(), security);
                bool prompted = false;
                var (lines, _) = await handler.LoadAsync(tempFile, (_, _, _) =>
                {
                    prompted = true;
                    return Task.FromResult<string?>("pw-1");
                });

                Assert.False(prompted);
                var loaded = string.Join("\n", lines);
                Assert.Contains("ENC:", loaded, StringComparison.Ordinal);
                Assert.DoesNotContain("secret", loaded, StringComparison.Ordinal);
            }
            finally
            {
                try { File.Delete(tempFile); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task SaveScript_SealsSecretsReplacesBufferAndClearsUndoHistory()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"etlsql-tui-save-{Guid.NewGuid():N}.etlsql");
            try
            {
                var editor = new ConsoleEditor(tempFile, new Dictionary<string, IDataSource>());
                editor._renderer.Headless = true;

                editor._buffer.Load(new[] { "SELECT 1;" });
                editor.SaveUndoState();
                editor._buffer.Load(new[]
                {
                    "USE PASSWORD = 'tui-pw';",
                    "CREATE CONNECTION m AS MSSQL(SERVER='s', PASSWORD='my-db-password');"
                });
                editor.MarkDirty();

                Assert.True(await editor.SaveScript());

                var buffered = editor._buffer.GetText();
                Assert.DoesNotContain("tui-pw", buffered, StringComparison.Ordinal);
                Assert.DoesNotContain("my-db-password", buffered, StringComparison.Ordinal);
                Assert.Contains("USE PASSWORD PROMPT;", buffered, StringComparison.Ordinal);
                Assert.Contains("ENC:", buffered, StringComparison.Ordinal);

                // Ctrl+Z (Undo) must not restore plaintext password after save-time sealing.
                editor.Undo();
                var afterUndo = editor._buffer.GetText();
                Assert.DoesNotContain("tui-pw", afterUndo, StringComparison.Ordinal);
                Assert.DoesNotContain("my-db-password", afterUndo, StringComparison.Ordinal);
                Assert.Equal(buffered, afterUndo);
            }
            finally
            {
                try { File.Delete(tempFile); } catch (IOException) { }
            }
        }
    }
}
