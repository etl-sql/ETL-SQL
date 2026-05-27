using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.Integration.UI
{
    public class TelemetryTests : IDisposable
    {
        private readonly StringWriter _outWriter;
        private readonly TextWriter _originalOut;
        private readonly TextReader _originalIn;

        public TelemetryTests()
        {
            _outWriter = new StringWriter();
            _originalOut = Console.Out;
            _originalIn = Console.In;
            Console.SetOut(_outWriter);
            if (ETL_SQL.TUI.Program.ServiceProvider == null)
            {
                ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            }
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetIn(_originalIn);
            _outWriter.Dispose();
        }

        [Fact]
        public async Task ReplUi_Emits_Correct_Ready_Status()
        {
            // Arrange
            var ctx = new CliContext { IsJsonMode = true };
            var repl = new ReplUi(ctx, ETL_SQL.TUI.Program.ServiceProvider);

            // Act - Start and immediately exit
            var input = new StringReader("{\"action\":\"exit\"}\n");
            Console.SetIn(input);
            await repl.RunAsync();

            // Assert
            var output = _outWriter.ToString();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            Assert.Contains(lines, l => l.Contains("\"type\":\"status\"") && l.Contains("\"status\":\"ready\""));
            var readyLine = lines.First(l => l.Contains("\"status\":\"ready\""));
            var readyObj = JsonDocument.Parse(readyLine).RootElement;
            
            Assert.True(readyObj.TryGetProperty("buildId", out _), "ReplUi ready signal must contain buildId for diagnostics");
        }

        [Fact]
        public async Task ReplUi_Emits_Correct_Result_Format()
        {
            // Arrange
            var ctx = new CliContext { IsJsonMode = true };
            var repl = new ReplUi(ctx, ETL_SQL.TUI.Program.ServiceProvider);
            var script = "SELECT 1 as ID, 'Test' as Name;";
            var input = new StringReader($"{{\"action\":\"run\", \"script\":\"{script}\"}}\n{{\"action\":\"exit\"}}\n");
            Console.SetIn(input);

            // Act
            await repl.RunAsync();

            // Assert
            var output = _outWriter.ToString();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            var resultLine = lines.FirstOrDefault(l => l.Contains("\"type\":\"results\""));
            Assert.NotNull(resultLine);

            var doc = JsonDocument.Parse(resultLine);
            var root = doc.RootElement;
            
            Assert.Equal("results", root.GetProperty("type").GetString());
            Assert.True(root.TryGetProperty("columns", out var cols), "Results must have columns array");
            Assert.True(root.TryGetProperty("rows", out var rows), "Results must have rows array");
            
            Assert.Equal(2, cols.GetArrayLength());
            Assert.Equal("ID", cols[0].GetString());
            
            var firstRow = rows[0];
            Assert.Equal(1, firstRow.GetProperty("ID").GetInt32());
            Assert.Equal("Test", firstRow.GetProperty("Name").GetString());
        }

        [Fact]
        public async Task ReplUi_Emits_Correct_Progress_Format()
        {
            // Arrange
            var ctx = new CliContext { IsJsonMode = true };
            var repl = new ReplUi(ctx, ETL_SQL.TUI.Program.ServiceProvider);
            var script = "WAITFOR DELAY '00:00:01';"; // Ensure at least one heartbeat
            var input = new StringReader($"{{\"action\":\"run\", \"script\":\"{script}\"}}\n{{\"action\":\"exit\"}}\n");
            Console.SetIn(input);

            // Act
            await repl.RunAsync();

            // Assert
            var output = _outWriter.ToString();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            var progressLine = lines.FirstOrDefault(l => l.Contains("\"type\":\"progress\""));
            Assert.NotNull(progressLine);

            var doc = JsonDocument.Parse(progressLine);
            var root = doc.RootElement;
            
            Assert.Equal("progress", root.GetProperty("type").GetString());
            Assert.True(root.TryGetProperty("data", out var data), "Progress must have root 'data' property");
            Assert.Equal(JsonValueKind.Array, data.ValueKind); // ToSnapshot() returns a List<object>
        }

        [Fact]
        public async Task ReplUi_Emits_Correct_Performance_Format()
        {
            // Arrange
            var ctx = new CliContext { IsJsonMode = true };
            var repl = new ReplUi(ctx, ETL_SQL.TUI.Program.ServiceProvider);
            var script = "SELECT 1;"; 
            var input = new StringReader($"{{\"action\":\"run\", \"script\":\"{script}\"}}\n{{\"action\":\"exit\"}}\n");
            Console.SetIn(input);

            // Act
            await repl.RunAsync();

            // Assert
            var output = _outWriter.ToString();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            var perfLine = lines.FirstOrDefault(l => l.Contains("\"type\":\"performance\""));
            Assert.NotNull(perfLine);

            var doc = JsonDocument.Parse(perfLine);
            var root = doc.RootElement;
            
            Assert.Equal("performance", root.GetProperty("type").GetString());
            // ReplUi currently uses "metrics"
            Assert.True(root.TryGetProperty("metrics", out var metrics), "ReplUi performance must have 'metrics' property");
            Assert.True(metrics.TryGetProperty("executionMs", out _), "Metrics must contain execution duration");
            Assert.True(metrics.TryGetProperty("statements", out _), "Metrics must contain statements breakdown");
        }

        [Fact]
        public async Task ReplUi_Supports_Multiple_Sequential_Runs()
        {
            // Arrange
            var ctx = new CliContext { IsJsonMode = true };
            var repl = new ReplUi(ctx, ETL_SQL.TUI.Program.ServiceProvider);
            
            // A script that creates and drops a connection
            var script = "DROP CONNECTION IF EXISTS m; CREATE CONNECTION m AS MOCKDB(); SELECT 1;";
            
            // Run it twice sequentially in the SAME session
            var input = new StringReader(
                  $"{{\"action\":\"run\", \"script\":\"{script}\"}}\n" 
                + $"{{\"action\":\"run\", \"script\":\"{script}\"}}\n" 
                + $"{{\"action\":\"exit\"}}\n");
            Console.SetIn(input);

            // Act
            await repl.RunAsync();

            // Assert
            var output = _outWriter.ToString();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // Should see two "done" messages with exitCode 0
            var doneLines = lines.Where(l => l.Contains("\"type\":\"done\"") && l.Contains("\"exitCode\":0")).ToList();
            Assert.Equal(2, doneLines.Count);
        }
    }
}
