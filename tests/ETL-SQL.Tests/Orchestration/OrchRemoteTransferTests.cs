using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class RemoteTransferTests
    {
        private class MockRemoteFileSystem : IRemoteFileSystem, IDataSource, IConnector
        {
            public Dictionary<string, string> RemoteFiles { get; } = new();
            public string Name => "MOCK_REMOTE";
            public IReadOnlyList<string> Aliases => new[] { "REMOTE" };
            public string Path => "mock://remote";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "MOCK_REMOTE";

            public async IAsyncEnumerable<FileMetaData> ListFilesAsync(string path)
            {
                await Task.CompletedTask;
                foreach (var k in RemoteFiles.Keys)
                    yield return new FileMetaData { Name = k, FullPath = k, Size = 100, IsDirectory = false };
            }

            public Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true)
            {
                if (!overwrite && RemoteFiles.ContainsKey(remotePath))
                    throw new Exception("Remote file already exists");
                RemoteFiles[remotePath] = File.ReadAllText(localPath);
                return Task.CompletedTask;
            }

            public Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
            {
                if (!overwrite && File.Exists(localPath))
                    throw new Exception("Local file already exists");

                if (RemoteFiles.TryGetValue(remotePath, out var content))
                {
                    File.WriteAllText(localPath, content);
                }
                return Task.CompletedTask;
            }

            public Task DeleteFileAsync(string remotePath)
            {
                RemoteFiles.Remove(remotePath);
                return Task.CompletedTask;
            }

            public Task<bool> FileExistsAsync(string remotePath)
            {
                return Task.FromResult(RemoteFiles.ContainsKey(remotePath));
            }

            public Task<bool> DirectoryExistsAsync(string remotePath)
            {
                string prefix = remotePath.EndsWith('/') ? remotePath : remotePath + "/";
                return Task.FromResult(RemoteFiles.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            }

            public Task RenameFileAsync(string remoteSource, string remoteDest, bool overwrite = true)
            {
                if (RemoteFiles.TryGetValue(remoteSource, out var content))
                {
                    if (overwrite || !RemoteFiles.ContainsKey(remoteDest))
                    {
                        RemoteFiles[remoteDest] = content;
                        RemoteFiles.Remove(remoteSource);
                    }
                }
                return Task.CompletedTask;
            }

            public Task CreateDirectoryAsync(string remotePath)
            {
                return Task.CompletedTask;
            }

            public Task DeleteDirectoryAsync(string remotePath)
            {
                string prefix = remotePath.EndsWith('/') ? remotePath : remotePath + "/";
                var keysToDelete = RemoteFiles.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var key in keysToDelete)
                {
                    RemoteFiles.Remove(key);
                }
                return Task.CompletedTask;
            }

            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
            {
                var table = new DataTable();
                table.ColumnNames.Add("Name");
                foreach (var f in RemoteFiles.Keys) await table.AddRowAsync(new Row { ["Name"] = f });
                yield return table;
                await Task.CompletedTask;
            }

            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => Task.CompletedTask;
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult((IEnumerable<string>)new[] { "Name" });
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("Mock 1.0");
            public HashSet<string> GetSupportedFunctions() => new();
            public HashSet<string> GetSupportedKeywords() => new();
            public Dictionary<string, string[]> GetSupportedOptions() => new();
            public Dictionary<string, string[]> GetOptionValues() => new();
            public string GetHelp() => "Mock Remote File System";
            public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null) => this;
            public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private async Task RunScriptAsync(Evaluator evaluator, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            var script = parser.Parse();
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new Exception(script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message);
            await evaluator.Evaluate(script);
        }

        [Fact]
        public async Task Test_SqlStyle_FileTransfer()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            string localFile = Path.Combine(Path.GetTempPath(), "sql_upload.txt");
            string downloadFile = Path.Combine(Path.GetTempPath(), "sql_download.txt");
            File.WriteAllText(localFile, "Initial Content");

            // 1. SEND FILE (Initial)
            await RunScriptAsync(evaluator, $"SEND FILE '{localFile.Replace("\\", "\\\\")}' TO 'remote.txt' AT MYREMOTE;");
            Assert.Equal("Initial Content", mockFs.RemoteFiles["remote.txt"]);

            // 2. SEND FILE (Overwrite ON)
            File.WriteAllText(localFile, "New Content");
            await RunScriptAsync(evaluator, $"SEND FILE '{localFile.Replace("\\", "\\\\")}' TO 'remote.txt' AT MYREMOTE WITH(OVERWRITE=ON);");
            Assert.Equal("New Content", mockFs.RemoteFiles["remote.txt"]);

            // 3. SEND FILE (Overwrite OFF - Success if not exists)
            await RunScriptAsync(evaluator, $"SEND FILE '{localFile.Replace("\\", "\\\\")}' TO 'new_remote.txt' AT MYREMOTE WITH(OVERWRITE=OFF);");
            Assert.True(mockFs.RemoteFiles.ContainsKey("new_remote.txt"));

            // 4. SEND FILE (Overwrite OFF - Failure if exists)
            await Assert.ThrowsAsync<Exception>(async () =>
                await RunScriptAsync(evaluator, $"SEND FILE '{localFile.Replace("\\", "\\\\")}' TO 'remote.txt' AT MYREMOTE WITH(OVERWRITE=OFF);"));

            // 5. RECEIVE FILE
            if (File.Exists(downloadFile)) File.Delete(downloadFile);
            await RunScriptAsync(evaluator, $"RECEIVE FILE FROM 'remote.txt' TO '{downloadFile.Replace("\\", "\\\\")}' AT MYREMOTE;");
            Assert.True(File.Exists(downloadFile));
            Assert.Equal("New Content", File.ReadAllText(downloadFile));

            // Clean up
            if (File.Exists(localFile)) File.Delete(localFile);
            if (File.Exists(downloadFile)) File.Delete(downloadFile);
        }

        [Fact]
        public async Task Test_SqlStyle_FileTransferOverwrite()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            string localFile = Path.Combine(Path.GetTempPath(), "func_upload.txt");
            File.WriteAllText(localFile, "Func Content");

            // 1. SEND FILE
            await RunScriptAsync(evaluator, $"SEND FILE '{localFile.Replace("\\", "\\\\")}' TO 'func_remote.txt' AT MYREMOTE WITH(OVERWRITE=ON);");
            Assert.Equal("Func Content", mockFs.RemoteFiles["func_remote.txt"]);

            // 2. SEND FILE Overwrite OFF
            await Assert.ThrowsAsync<Exception>(async () =>
                await RunScriptAsync(evaluator, $"SEND FILE '{localFile.Replace("\\", "\\\\")}' TO 'func_remote.txt' AT MYREMOTE WITH(OVERWRITE=OFF);"));

            // Cleanup
            if (File.Exists(localFile)) File.Delete(localFile);
        }

        [Fact]
        public async Task Test_RemoteFileOperations()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            // Delete Test
            mockFs.RemoteFiles["to_delete.txt"] = "delete me";
            await RunScriptAsync(evaluator, "DELETE FILE 'to_delete.txt' AT MYREMOTE;");
            Assert.False(mockFs.RemoteFiles.ContainsKey("to_delete.txt"));

            // Rename Test
            mockFs.RemoteFiles["old_name.txt"] = "rename me";
            await RunScriptAsync(evaluator, "RENAME FILE 'old_name.txt' TO 'new_name.txt' AT MYREMOTE;");
            Assert.False(mockFs.RemoteFiles.ContainsKey("old_name.txt"));
            Assert.Equal("rename me", mockFs.RemoteFiles["new_name.txt"]);

            // Move Test
            await RunScriptAsync(evaluator, "MOVE FILE 'new_name.txt' TO 'archive/moved.txt' AT MYREMOTE;");
            Assert.False(mockFs.RemoteFiles.ContainsKey("new_name.txt"));
            Assert.Equal("rename me", mockFs.RemoteFiles["archive/moved.txt"]);
        }

        [Fact]
        public async Task Test_RemoteDirectoryOperations()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            // Create Directory should succeed without error
            await RunScriptAsync(evaluator, "CREATE DIRECTORY 'some_dir/' AT MYREMOTE;");

            // Delete Directory
            mockFs.RemoteFiles["some_dir/file1.txt"] = "content1";
            mockFs.RemoteFiles["some_dir/file2.txt"] = "content2";
            mockFs.RemoteFiles["other_dir/file3.txt"] = "content3";

            await RunScriptAsync(evaluator, "DELETE DIRECTORY 'some_dir/' AT MYREMOTE;");

            Assert.False(mockFs.RemoteFiles.ContainsKey("some_dir/file1.txt"));
            Assert.False(mockFs.RemoteFiles.ContainsKey("some_dir/file2.txt"));
            Assert.True(mockFs.RemoteFiles.ContainsKey("other_dir/file3.txt"));
        }

        [Fact]
        public async Task Test_WildcardFileTransfers()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            string tempDir = Path.Combine(Path.GetTempPath(), "temp_wildcards_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string file1 = Path.Combine(tempDir, "a.txt");
            string file2 = Path.Combine(tempDir, "b.txt");
            string file3 = Path.Combine(tempDir, "c.log");

            File.WriteAllText(file1, "file1 content");
            File.WriteAllText(file2, "file2 content");
            File.WriteAllText(file3, "file3 content");

            try
            {
                // Send wildcard matching *.txt
                string sqlSend = $"SEND FILE '{Path.Combine(tempDir, "*.txt").Replace("\\", "\\\\")}' TO 'remote_wildcard/' AT MYREMOTE;";
                await RunScriptAsync(evaluator, sqlSend);

                Assert.True(mockFs.RemoteFiles.ContainsKey("remote_wildcard/a.txt"));
                Assert.True(mockFs.RemoteFiles.ContainsKey("remote_wildcard/b.txt"));
                Assert.False(mockFs.RemoteFiles.ContainsKey("remote_wildcard/c.log"));

                // Receive wildcard matching *.txt back to a new folder
                string receiveDir = Path.Combine(Path.GetTempPath(), "temp_received_" + Guid.NewGuid().ToString("N"));
                string sqlReceive = $"RECEIVE FILE FROM 'remote_wildcard/*.txt' TO '{receiveDir.Replace("\\", "\\\\")}' AT MYREMOTE;";
                await RunScriptAsync(evaluator, sqlReceive);

                Assert.True(File.Exists(Path.Combine(receiveDir, "a.txt")));
                Assert.True(File.Exists(Path.Combine(receiveDir, "b.txt")));
                Assert.False(File.Exists(Path.Combine(receiveDir, "c.log")));

                // Cleanup received directory
                if (Directory.Exists(receiveDir)) Directory.Delete(receiveDir, true);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Test_RemoteFileExistsFunction()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            mockFs.RemoteFiles["folder/test.txt"] = "content";

            // Test file exists
            string sqlExists = "SELECT REMOTE_FILE_EXISTS('MYREMOTE', 'folder/test.txt') AS ExistsVal;";
            var lexerExists = new Lexer(sqlExists);
            var parserExists = new Parser(lexerExists.Tokenize());
            var scriptExists = parserExists.Parse();
            var resultsExists = new List<DataTable>();
            await foreach (var batch in evaluator.ExecuteQuery(scriptExists.Statements[0])) resultsExists.Add(batch);

            Assert.Single(resultsExists);
            var rowsExists = resultsExists[0].Rows.ToList();
            Assert.Single(rowsExists);
            Assert.Equal(1m, rowsExists[0]["ExistsVal"]);

            // Test directory exists (matches prefix folder/)
            string sqlDirExists = "SELECT REMOTE_FILE_EXISTS('MYREMOTE', 'folder/') AS ExistsVal;";
            var lexerDirExists = new Lexer(sqlDirExists);
            var parserDirExists = new Parser(lexerDirExists.Tokenize());
            var scriptDirExists = parserDirExists.Parse();
            var resultsDirExists = new List<DataTable>();
            await foreach (var batch in evaluator.ExecuteQuery(scriptDirExists.Statements[0])) resultsDirExists.Add(batch);

            Assert.Single(resultsDirExists);
            var rowsDirExists = resultsDirExists[0].Rows.ToList();
            Assert.Single(rowsDirExists);
            Assert.Equal(1m, rowsDirExists[0]["ExistsVal"]);

            // Test not exists
            string sqlNotExists = "SELECT REMOTE_FILE_EXISTS('MYREMOTE', 'folder/notfound.txt') AS ExistsVal;";
            var lexerNotExists = new Lexer(sqlNotExists);
            var parserNotExists = new Parser(lexerNotExists.Tokenize());
            var scriptNotExists = parserNotExists.Parse();
            var resultsNotExists = new List<DataTable>();
            await foreach (var batch in evaluator.ExecuteQuery(scriptNotExists.Statements[0])) resultsNotExists.Add(batch);

            Assert.Single(resultsNotExists);
            var rowsNotExists = resultsNotExists[0].Rows.ToList();
            Assert.Single(rowsNotExists);
            Assert.Equal(0m, rowsNotExists[0]["ExistsVal"]);
        }
    }
}
