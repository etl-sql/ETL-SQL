using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Common;
using System.Collections.Generic;
using ETL_SQL.Common;
using System.Linq;
using ETL_SQL.Core.Parser;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Operations
{
    public class FileDirectoryOpTests : IDisposable
    {
        private readonly string _testDir;

        public FileDirectoryOpTests()
        {
            _testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestFiles_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        private async Task RunScriptAsync(string sql)
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();
            
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            var script = parser.Parse();
            
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var errors = string.Join("\n", script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
                throw new Exception($"Parsing failed with errors:\n{errors}");
            }

            await evaluator.Evaluate(script);
        }

        [Fact]
        public async Task Test_FileCopy_Overwrite_Off()
        {
            string source = Path.Combine(_testDir, "source.txt");
            string dest = Path.Combine(_testDir, "dest.txt");
            File.WriteAllText(source, "hello");
            File.WriteAllText(dest, "already exists");

            // Escape backslashes for SQL
            string sourceEsc = source.Replace("\\", "\\\\");
            string destEsc = dest.Replace("\\", "\\\\");

            string script = $"COPY FILE '{sourceEsc}' TO '{destEsc}' WITH(OVERWRITE=OFF);";
            
            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () => await RunScriptAsync(script));
        }

        [Fact]
        public async Task Test_FileCopy_Overwrite_On()
        {
            string source = Path.Combine(_testDir, "source.txt");
            string dest = Path.Combine(_testDir, "dest.txt");
            File.WriteAllText(source, "hello");
            File.WriteAllText(dest, "already exists");

            string sourceEsc = source.Replace("\\", "\\\\");
            string destEsc = dest.Replace("\\", "\\\\");

            string script = $"COPY FILE '{sourceEsc}' TO '{destEsc}' WITH(OVERWRITE=ON);";
            
            await RunScriptAsync(script);
            Assert.Equal("hello", File.ReadAllText(dest));
        }

        [Fact]
        public async Task Test_DirectoryCompress_SqlStyle()
        {
            string subDir = Path.Combine(_testDir, "sub");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "file.txt"), "content");
            string zipPath = Path.Combine(_testDir, "archive.zip");

            string subDirEsc = subDir.Replace("\\", "\\\\");
            string zipPathEsc = zipPath.Replace("\\", "\\\\");

            string script = $"COMPRESS DIRECTORY '{subDirEsc}' TO '{zipPathEsc}';";
            
            await RunScriptAsync(script);
            Assert.True(File.Exists(zipPath));
        }

        [Fact]
        public async Task Test_DirectoryCompress_FunctionStyle()
        {
            string subDir = Path.Combine(_testDir, "sub2");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "file.txt"), "content");
            string zipPath = Path.Combine(_testDir, "archive2.zip");

            string subDirEsc = subDir.Replace("\\", "\\\\");
            string zipPathEsc = zipPath.Replace("\\", "\\\\");

            string script = $"COMPRESS_DIRECTORY('{subDirEsc}', '{zipPathEsc}');";
            
            await RunScriptAsync(script);
            Assert.True(File.Exists(zipPath));
        }

        [Fact]
        public async Task Test_DirectoryEncrypt_Decrypt()
        {
            string subDir = Path.Combine(_testDir, "data");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "secret.txt"), "my secret");
            
            string encDir = Path.Combine(_testDir, "encrypted_data");
            string decDir = Path.Combine(_testDir, "decrypted_data");

            string subDirEsc = subDir.Replace("\\", "\\\\");
            string encDirEsc = encDir.Replace("\\", "\\\\");
            string decDirEsc = decDir.Replace("\\", "\\\\");

            string script = $@"
                ENCRYPT DIRECTORY '{subDirEsc}' TO '{encDirEsc}';
                DECRYPT DIRECTORY '{encDirEsc}' TO '{decDirEsc}';
            ";
            
            await RunScriptAsync(script);
            
            Assert.True(File.Exists(Path.Combine(decDir, "secret.txt")));
            Assert.Equal("my secret", File.ReadAllText(Path.Combine(decDir, "secret.txt")));
        }

        [Fact]
        public async Task Test_DeleteFile_SqlStyle()
        {
            string file = Path.Combine(_testDir, "to_delete.txt");
            File.WriteAllText(file, "gone soon");

            string fileEsc = file.Replace("\\", "\\\\");
            string script = $"DELETE FILE '{fileEsc}';";
            
            await RunScriptAsync(script);
            Assert.False(File.Exists(file));
        }
    }
}
