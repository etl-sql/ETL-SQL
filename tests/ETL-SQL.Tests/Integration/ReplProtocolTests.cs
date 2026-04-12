using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace ETL_SQL.Tests.Integration
{
    public class ReplProtocolTests : IDisposable
    {
        private readonly string _testExportPath;
        private readonly string _projectPath;

        public ReplProtocolTests()
        {
            // Resolve project path relative to bin/Debug/...
            var baseDir = AppContext.BaseDirectory;
            var root = baseDir.Substring(0, baseDir.IndexOf("\\tests\\"));
            _projectPath = Path.Combine(root, "src", "ETL-SQL.App", "ETL-SQL.App.csproj");
            _testExportPath = Path.Combine(root, "src", "ETL-SQL.App", "test_repl_export.csv");

            if (File.Exists(_testExportPath)) File.Delete(_testExportPath);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task REPL_Should_Emit_Variables_And_Export_CSV()
        {
            // Start the engine in REPL mode
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{_projectPath}\" -- ui repl",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // 1. Wait for ready status
            string? firstLine = await ReadUntilType(process.StandardOutput, "status");
            Assert.Contains("\"status\":\"ready\"", firstLine);

            // 2. Run a script with variables
            var runCmd = new { action = "run", script = "DECLARE @test = 'XUnit'; SELECT @test AS Col1;" };
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(runCmd));

            // Wait for results
            await ReadUntilType(process.StandardOutput, "results");

            // Wait for variables packet
            string? varLine = await ReadUntilType(process.StandardOutput, "variables");
            Assert.Contains("@test", varLine);
            Assert.Contains("XUnit", varLine);

            // Wait for done
            await ReadUntilType(process.StandardOutput, "done");

            // 3. Export the results (Use absolute path to be sure)
            var escapedPath = _testExportPath.Replace("\\", "\\\\");
            var exportCmd = new { action = "export", path = _testExportPath, format = "csv" };
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(exportCmd));

            // Wait for export verification message
            string? exportMsgLine = await ReadUntilContains(process.StandardOutput, "Successfully exported");
            Assert.NotNull(exportMsgLine);

            // 4. Verify file on disk
            Assert.True(File.Exists(_testExportPath), "Exported CSV file was not found on disk.");
            var csvContent = await File.ReadAllTextAsync(_testExportPath);
            Assert.Contains("Col1", csvContent);
            Assert.Contains("XUnit", csvContent);

            // 5. Exit
            await process.StandardInput.WriteLineAsync("{\"action\":\"exit\"}");
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        private async Task<string?> ReadUntilType(StreamReader output, string type)
        {
            while (true)
            {
                var line = await output.ReadLineAsync();
                if (line == null) return null;
                if (line.Trim().StartsWith("{") && line.Contains($"\"type\":\"{type}\"")) return line;
            }
        }

        private async Task<string?> ReadUntilContains(StreamReader output, string snippet)
        {
            while (true)
            {
                var line = await output.ReadLineAsync();
                if (line == null) return null;
                if (line.Contains(snippet)) return line;
            }
        }

        public void Dispose()
        {
            if (File.Exists(_testExportPath)) File.Delete(_testExportPath);
        }
    }
}
