using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// REPL protocol safety: control commands answered while a script runs, secret variables
    /// masked on stdout, and exports governed by the shared filesystem write policy.
    /// </summary>
    public class ReplUiSafetyTests : IDisposable
    {
        private readonly TextWriter _originalOut = Console.Out;
        private readonly TextReader _originalIn = Console.In;

        public ReplUiSafetyTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider ??= ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetIn(_originalIn);
        }

        [Fact]
        public async Task CancelAndPing_AreHandledWhileAScriptRuns()
        {
            var output = new SignallingWriter();
            var input = new QueueReader();
            Console.SetOut(output);
            Console.SetIn(input);

            var repl = new ReplUi(new CliContext { IsJsonMode = true }, ETL_SQL.TUI.Program.ServiceProvider);
            var clock = Stopwatch.StartNew();
            var session = Task.Run(repl.RunAsync);

            input.Push("{\"action\":\"run\",\"script\":\"WAITFOR DELAY '00:00:20';\"}");
            Assert.True(output.WaitFor("\"type\":\"progress\"", TimeSpan.FromSeconds(15)), output.Text);

            input.Push("{\"action\":\"ping\"}");
            Assert.True(output.WaitFor("\"type\":\"pong\"", TimeSpan.FromSeconds(5)), "ping was not answered while the script ran");
            input.Push("{\"action\":\"cancel\"}");
            Assert.True(output.WaitFor("\"type\":\"done\"", TimeSpan.FromSeconds(10)), "cancel did not stop the running script");
            input.Push("{\"action\":\"exit\"}");

            await session.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(18), $"run was not cancelled: {clock.Elapsed}");
            Assert.Contains("Execution cancellation requested.", output.Text);
            Assert.Contains("\"exitCode\":1", output.Text);
        }

        [Fact]
        public void UserVariables_MaskSecretAndSensitiveValues()
        {
            var vars = new Dictionary<string, object?>
            {
                ["@token"] = "plain-token-value",
                ["@pwd"] = "plain-password",
                ["@conn"] = "Server=x;Password=leaky;",
                ["@n"] = 7m,
                ["@@ROWCOUNT"] = 1m,
            };
            var meta = new Dictionary<string, VariableMetadata>
            {
                ["@token"] = new() { IsSecret = true },
                ["@pwd"] = new() { IsSensitive = true },
            };

            var result = ReplUi.BuildUserVariables(vars, n => meta.TryGetValue(n, out var m) ? m : null)
                .ToDictionary(v => v.Name);
            var json = System.Text.Json.JsonSerializer.Serialize(result.Values);

            Assert.Equal(SecretRedactor.Mask, result["@token"].Value);
            Assert.Equal(SecretRedactor.Mask, result["@pwd"].Value);
            Assert.DoesNotContain("plain-token-value", json);
            Assert.DoesNotContain("plain-password", json);
            Assert.DoesNotContain("leaky", json);
            Assert.Equal("7", result["@n"].Value);
            Assert.False(result.ContainsKey("@@ROWCOUNT"));
            Assert.Contains("\"name\":\"@n\"", json);
        }

        [Theory]
        [InlineData("script.etlsql")]
        [InlineData("report.rptsql")]
        [InlineData("query.sql")]
        public void ExportPath_RefusesScriptFiles(string fileName)
        {
            var evaluator = (Evaluator)ETL_SQL.TUI.Program.ServiceProvider.GetService(typeof(Evaluator))!;
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_repl_export_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var previousDirectory = evaluator.WorkingDirectory;
            try
            {
                evaluator.WorkingDirectory = dir;
                Assert.ThrowsAny<ETL_SQL.Services.SecurityException>(
                    () => ReplUi.AuthorizeExportPath(evaluator, fileName));
                var allowed = ReplUi.AuthorizeExportPath(evaluator, "results.csv");
                Assert.Equal(Path.Combine(dir, "results.csv"), allowed, ignoreCase: OperatingSystem.IsWindows());
            }
            finally
            {
                evaluator.WorkingDirectory = previousDirectory;
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>Hands lines to the REPL one at a time, blocking until the test pushes the next.</summary>
        private sealed class QueueReader : TextReader
        {
            private readonly BlockingCollection<string> _lines = new();
            public void Push(string line) => _lines.Add(line);
            public override string? ReadLine() =>
                _lines.TryTake(out var line, TimeSpan.FromSeconds(60)) ? line : null;
        }

        /// <summary>Thread-safe stdout capture that can wait for a marker to appear.</summary>
        private sealed class SignallingWriter : TextWriter
        {
            private readonly StringBuilder _text = new();
            private readonly object _gate = new();
            public override Encoding Encoding => Encoding.UTF8;

            public string Text { get { lock (_gate) return _text.ToString(); } }

            public override void Write(char value)
            {
                lock (_gate) { _text.Append(value); Monitor.PulseAll(_gate); }
            }

            public override void Write(string? value)
            {
                lock (_gate) { _text.Append(value); Monitor.PulseAll(_gate); }
            }

            public bool WaitFor(string marker, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;
                lock (_gate)
                {
                    while (!_text.ToString().Contains(marker, StringComparison.Ordinal))
                    {
                        var left = deadline - DateTime.UtcNow;
                        if (left <= TimeSpan.Zero || !Monitor.Wait(_gate, left))
                            return _text.ToString().Contains(marker, StringComparison.Ordinal);
                    }
                    return true;
                }
            }
        }
    }
}
