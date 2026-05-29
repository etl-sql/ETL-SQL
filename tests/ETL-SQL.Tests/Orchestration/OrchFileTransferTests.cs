using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;

using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Orchestration
{
    public class FileTransferTests
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
                RemoteFiles[remotePath] = File.ReadAllText(localPath);
                return Task.CompletedTask;
            }

            public Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true)
            {
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

        [Fact]
        public async Task Test_FileSend_And_Receive()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            string localFile = Path.Combine(Path.GetTempPath(), "test_upload.txt");
            string downloadFile = Path.Combine(Path.GetTempPath(), "test_download.txt");
            File.WriteAllText(localFile, "Hello Remote World");

            // 1. FILE_SEND
            string sqlSend = $"FILE_SEND '{localFile.Replace("\\", "\\\\")}', MYREMOTE, 'remote/test.txt'";
            var lexerSend = new Lexer(sqlSend);
            var parserSend = new Parser(lexerSend.Tokenize());
            var scriptSend = parserSend.Parse();
            await evaluator.EvaluateStatement(scriptSend.Statements[0]);

            Assert.True(mockFs.RemoteFiles.ContainsKey("remote/test.txt"));
            Assert.Equal("Hello Remote World", mockFs.RemoteFiles["remote/test.txt"]);

            // 2. REMOTE_FILE_LIST
            string sqlList = "SELECT * FROM REMOTE_FILE_LIST('MYREMOTE', 'remote/')";
            var lexerList = new Lexer(sqlList);
            var parserList = new Parser(lexerList.Tokenize());
            var scriptList = parserList.Parse();
            var results = new List<DataTable>();
            await foreach (var batch in evaluator.ExecuteQuery(scriptList.Statements[0])) results.Add(batch);

            Assert.Single(results);
            Assert.Contains(results[0].Rows, r => r["Name"].ToString() == "remote/test.txt");

            // 3. FILE_RECEIVE
            string sqlReceive = $"FILE_RECEIVE MYREMOTE, 'remote/test.txt', '{downloadFile.Replace("\\", "\\\\")}'";
            var lexerReceive = new Lexer(sqlReceive);
            var parserReceive = new Parser(lexerReceive.Tokenize());
            var scriptReceive = parserReceive.Parse();
            await evaluator.EvaluateStatement(scriptReceive.Statements[0]);

            Assert.True(File.Exists(downloadFile));
            Assert.Equal("Hello Remote World", File.ReadAllText(downloadFile));

            // Cleanup
            if (File.Exists(localFile)) File.Delete(localFile);
            if (File.Exists(downloadFile)) File.Delete(downloadFile);
        }
    }
}
