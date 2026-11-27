using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// Executes the SQL recipes in the email and Slack/Teams alerting cookbooks end-to-end against
/// the engine (with local test doubles only for remote DB/SMTP/webhook endpoints) and verifies
/// they pass the static linter.
/// </summary>
public class AlertingCookbookRuntimeTests : IDisposable
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private readonly List<string> _sessionRoots = [];

    public void Dispose()
    {
        foreach (var dir in _sessionRoots)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of temp session dirs.
            }
        }
    }

    private Evaluator NewEvaluator(Dictionary<string, string?>? configOverrides = null)
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider(configOverrides).GetRequiredService<Evaluator>();
        var sessionRoot = Path.Combine(AppContext.BaseDirectory, "TestSessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        _sessionRoots.Add(sessionRoot);
        eval.SessionRoot = sessionRoot;
        return eval;
    }

    private static List<string> ExtractSqlBlocks(string relPath)
    {
        var fullPath = Path.Combine(RepoRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        var lines = File.ReadAllText(fullPath).Replace("\r\n", "\n").Split('\n');
        var blocks = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().ToLowerInvariant() is not ("```sql" or "```etlsql"))
                continue;
            var body = new List<string>();
            var j = i + 1;
            for (; j < lines.Length; j++)
            {
                if (lines[j].TrimStart() == "```") break;
                body.Add(lines[j]);
            }
            blocks.Add(string.Join("\n", body));
            i = j;
        }
        return blocks;
    }

    private static Script Parse(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        var script = new Parser(tokens, sql).Parse();
        var errors = script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Message)));
        return script;
    }

    [Theory]
    [InlineData("docs/cookbooks/etl/automated-email-alerting.md")]
    [InlineData("docs/cookbooks/etl/automated-slack-teams-alerting.md")]
    public async Task AlertingCookbooks_HaveNoLintErrors(string relPath)
    {
        var services = DependencyInjectionSetup.BuildServiceProvider();
        var linter = LinterFactory.CreateWithAllRules(services, readDependencyFiles: false);
        var blocks = ExtractSqlBlocks(relPath);
        Assert.NotEmpty(blocks);

        foreach (var block in blocks)
        {
            var script = Parse(block);
            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());
            var errors = results.Where(r => r.Severity == LintSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"{relPath} reported lint error(s):\n" +
                string.Join("\n", errors.Select(e => $"  [{e.RuleName}] line {e.LineNumber}: {e.Message}")));
        }
    }

    [Fact]
    public async Task AutomatedEmailAlerting_ExecutesEndToEnd()
    {
        var prefix = $"ETLSQL_TEST_{Guid.NewGuid():N}_";
        Environment.SetEnvironmentVariable(prefix + "smtp_password", "test-smtp-pass");
        try
        {
            var eval = NewEvaluator(new Dictionary<string, string?>
            {
                ["Governance:Secrets:Provider"] = "Environment",
                ["Governance:Secrets:EnvironmentPrefix"] = prefix
            });

            var blocks = ExtractSqlBlocks("docs/cookbooks/etl/automated-email-alerting.md");
            var script = Parse(Assert.Single(blocks));

            // Execute CREATE CONNECTION + CREATE PROCEDURE from the cookbook directly
            await eval.Evaluate(new Script { Statements = script.Statements.Take(2).ToList() });

            Assert.True(eval.Connections.TryGetValue("alerts_smtp", out var smtpDs));
            Assert.Equal("smtp.company.com", smtpDs.Path);
            Assert.Equal("SMTP", smtpDs.ConnectorType);

            // Swap with capturing sink to avoid dialing smtp.company.com:587
            var captured = new CapturingSink("SMTP");
            eval.Connections["alerts_smtp"] = captured;

            var tryCatchScript = new Script { Statements = script.Statements.Skip(2).ToList() };
            await Assert.ThrowsAsync<ExecutionException>(() => eval.Evaluate(tryCatchScript));

            var sent = Assert.Single(captured.Rows);
            Assert.Equal("etl@company.com", sent["From"]);
            Assert.Equal("oncall@company.com", sent["To"]);
            Assert.Equal("[CRITICAL] ETL Pipeline Alert", sent["Subject"]);
            Assert.StartsWith("Nightly Load Failed:", sent["Body"]?.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "smtp_password", null);
        }
    }

    [Fact]
    public async Task AutomatedSlackTeamsAlerting_ExecutesAllBranchesAndOrchestratorAlert()
    {
        using var server = new LoopbackWebhookServer();
        var prefix = $"ETLSQL_TEST_{Guid.NewGuid():N}_";
        Environment.SetEnvironmentVariable(prefix + "crm_db_password", "db-secret");
        Environment.SetEnvironmentVariable(prefix + "slack_webhook_url", server.BaseUrl + "slack");
        Environment.SetEnvironmentVariable(prefix + "teams_webhook_url", server.BaseUrl + "teams");

        try
        {
            var blocks = ExtractSqlBlocks("docs/cookbooks/etl/automated-slack-teams-alerting.md");
            Assert.Equal(2, blocks.Count);

            var block1 = Parse(blocks[0]);
            var slackTeamsSetup = new Script { Statements = block1.Statements.Skip(2).Take(2).ToList() };
            var pipelineTryCatch = new Script { Statements = block1.Statements.Skip(4).ToList() };

            // 1) Clean run -> Slack alert
            {
                server.Requests.Clear();
                var eval = NewEvaluator(new Dictionary<string, string?>
                {
                    ["Governance:Secrets:Provider"] = "Environment",
                    ["Governance:Secrets:EnvironmentPrefix"] = prefix
                });
                await eval.Evaluate(slackTeamsSetup);

                var crm = new FakeTableHostDataSource(
                [
                    MakeOrderRow(1m, 100m, 50.00m, DateTime.UtcNow),
                    MakeOrderRow(2m, 101m, 75.00m, DateTime.UtcNow)
                ]);
                var dw = new FakeTableHostDataSource([], ["OrderId", "CustomerId", "Amount", "UpdatedAt"]);
                eval.Connections["crm_src"] = crm;
                eval.Connections["dw_dest"] = dw;

                await eval.Evaluate(pipelineTryCatch);

                Assert.Equal(2, dw.Rows.Count);
                var req = Assert.Single(server.Requests);
                Assert.Equal("/slack", req.Path);
                using var doc = JsonDocument.Parse(req.Body);
                Assert.Contains("2 rows merged", doc.RootElement.GetProperty("text").GetString());
            }

            // 2) Warning run (invalid row present) -> Teams alert
            {
                server.Requests.Clear();
                var eval = NewEvaluator(new Dictionary<string, string?>
                {
                    ["Governance:Secrets:Provider"] = "Environment",
                    ["Governance:Secrets:EnvironmentPrefix"] = prefix
                });
                await eval.Evaluate(slackTeamsSetup);

                var crm = new FakeTableHostDataSource(
                [
                    MakeOrderRow(1m, 100m, 50.00m, DateTime.UtcNow),
                    MakeOrderRow(2m, null, 75.00m, DateTime.UtcNow)
                ]);
                var dw = new FakeTableHostDataSource([], ["OrderId", "CustomerId", "Amount", "UpdatedAt"]);
                eval.Connections["crm_src"] = crm;
                eval.Connections["dw_dest"] = dw;

                await eval.Evaluate(pipelineTryCatch);

                Assert.Single(dw.Rows);
                var req = Assert.Single(server.Requests);
                Assert.Equal("/teams", req.Path);
                using var doc = JsonDocument.Parse(req.Body);
                Assert.Equal("MessageCard", doc.RootElement.GetProperty("@type").GetString());
                Assert.Equal("Nightly Orders Load Completed with Warnings", doc.RootElement.GetProperty("title").GetString());
                Assert.Contains("Loaded 1 clean rows; skipped 1 invalid rows", doc.RootElement.GetProperty("text").GetString());
            }

            // 3) Failure run -> Both Slack & Teams critical alerts + re-throw
            {
                server.Requests.Clear();
                var eval = NewEvaluator(new Dictionary<string, string?>
                {
                    ["Governance:Secrets:Provider"] = "Environment",
                    ["Governance:Secrets:EnvironmentPrefix"] = prefix
                });
                await eval.Evaluate(slackTeamsSetup);

                eval.Connections["crm_src"] = new FailingDataSource("CRM database unreachable");
                eval.Connections["dw_dest"] = new FakeTableHostDataSource([], ["OrderId", "CustomerId", "Amount", "UpdatedAt"]);

                await Assert.ThrowsAsync<ExecutionException>(() => eval.Evaluate(pipelineTryCatch));

                Assert.Equal(2, server.Requests.Count);
                Assert.Contains(server.Requests, r => r.Path == "/slack" && r.Body.Contains("[CRITICAL]"));
                Assert.Contains(server.Requests, r => r.Path == "/teams" && r.Body.Contains("CRITICAL: Nightly Orders Load Failed"));
            }

            // 4) Block 2: CREATE CONNECTION dq_slack + CREATE NOTIFICATION + ASSERT JOB
            {
                server.Requests.Clear();
                var eval = NewEvaluator(new Dictionary<string, string?>
                {
                    ["Governance:Secrets:Provider"] = "Environment",
                    ["Governance:Secrets:EnvironmentPrefix"] = prefix
                });
                var block2 = Parse(blocks[1]);
                await Assert.ThrowsAsync<ExecutionException>(() => eval.Evaluate(block2));

                var req = Assert.Single(server.Requests);
                Assert.Equal("/slack", req.Path);
                using var doc = JsonDocument.Parse(req.Body);
                Assert.Contains("ROW_COUNT > 0", doc.RootElement.GetProperty("text").GetString());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + "crm_db_password", null);
            Environment.SetEnvironmentVariable(prefix + "slack_webhook_url", null);
            Environment.SetEnvironmentVariable(prefix + "teams_webhook_url", null);
        }
    }

    private static Row MakeOrderRow(decimal orderId, decimal? customerId, decimal amount, DateTime updatedAt) => new()
    {
        ["OrderId"] = orderId,
        ["CustomerId"] = customerId,
        ["Amount"] = amount,
        ["UpdatedAt"] = updatedAt
    };

    private sealed class CapturingSink(string connectorType) : IDataSource
    {
        public List<Row> Rows { get; } = [];
        public string Path => "capture";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => connectorType;
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => AsyncEnumerable.Empty<DataTable>();
        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            await foreach (var batch in batches) Rows.AddRange(batch.Rows);
        }
        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>([]);
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingDataSource(string message) : IDataSource
    {
        public string Path => "failing";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "POSTGRES";
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            throw new ExecutionException(message);
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
            throw new ExecutionException(message);
        public Task<IEnumerable<string>> GetColumnsAsync() =>
            Task.FromResult<IEnumerable<string>>(["OrderId", "CustomerId", "Amount", "UpdatedAt"]);
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTableHostDataSource : IDataSource
    {
        private readonly List<string> _columns;
        public List<Row> Rows { get; }

        public FakeTableHostDataSource(IEnumerable<Row> initialRows, IEnumerable<string>? columns = null)
        {
            Rows = initialRows.ToList();
            _columns = columns?.ToList() ?? (Rows.FirstOrDefault()?.Columns.Keys.ToList() ?? []);
        }

        public string Path => "fake-db";
        public Dictionary<string, string>? Options => null;
        public string ConnectorType => "MOCKDB";

        public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
        {
            var dt = new DataTable();
            dt.SetColumns(_columns);
            foreach (var r in Rows) await dt.AddRowAsync(r);
            yield return dt;
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (!append) Rows.Clear();
            await foreach (var batch in batches) Rows.AddRange(batch.Rows);
        }

        public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult<IEnumerable<string>>(_columns);
        public object? Snapshot() => Rows.Select(r =>
        {
            var copy = new Row();
            foreach (var kv in r.Columns) copy[kv.Key] = kv.Value;
            return copy;
        }).ToList();
        public void Restore(object? snapshot)
        {
            if (snapshot is List<Row> prev)
            {
                Rows.Clear();
                Rows.AddRange(prev);
            }
        }
        public IDataSource WithTable(string tableName) => this;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LoopbackWebhookServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public string BaseUrl { get; }
        public List<(string Path, string Body)> Requests { get; } = [];

        public LoopbackWebhookServer()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();

            BaseUrl = $"http://127.0.0.1:{port}/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _loop = Task.Run(ListenAsync);
        }

        private async Task ListenAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    lock (Requests)
                    {
                        Requests.Add((ctx.Request.Url?.AbsolutePath ?? "", body));
                    }
                    var ok = Encoding.UTF8.GetBytes("ok");
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.OutputStream.WriteAsync(ok);
                    ctx.Response.Close();
                }
                catch when (_cts.IsCancellationRequested) { break; }
                catch { }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(1000); } catch { }
            _cts.Dispose();
        }
    }
}
