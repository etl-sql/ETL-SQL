using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Common;

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
        public async Task Test_FunctionStyle_FileTransfer()
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            var mockFs = new MockRemoteFileSystem();
            evaluator.Connections["MYREMOTE"] = mockFs;

            string localFile = Path.Combine(Path.GetTempPath(), "func_upload.txt");
            File.WriteAllText(localFile, "Func Content");

            // 1. SEND_FILE(local, conn, remote, over)
            await RunScriptAsync(evaluator, $"SEND_FILE('{localFile.Replace("\\", "\\\\")}', MYREMOTE, 'func_remote.txt', ON);");
            Assert.Equal("Func Content", mockFs.RemoteFiles["func_remote.txt"]);

            // 2. SEND_FILE Overwrite OFF
            await Assert.ThrowsAsync<Exception>(async () => 
                await RunScriptAsync(evaluator, $"SEND_FILE('{localFile.Replace("\\", "\\\\")}', MYREMOTE, 'func_remote.txt', OFF);"));

            // Cleanup
            if (File.Exists(localFile)) File.Delete(localFile);
        }
    }
}
