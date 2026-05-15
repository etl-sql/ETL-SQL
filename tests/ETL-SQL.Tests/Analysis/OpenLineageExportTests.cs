using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Engine.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Engine;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Analysis
{
    public class OpenLineageExportTests
    {
        // ── Namespace resolution ────────────────────────────────────────────

        [Fact]
        public void ResolveNamespace_TempTable_UsesSessionNamespace()
        {
            var (ns, name) = OpenLineageExporter.ResolveNamespace("#orders", "abc123");
            Assert.Equal("etl-sql://session/abc123", ns);
            Assert.Equal("#orders", name);
        }

        [Fact]
        public void ResolveNamespace_ReportNode_UsesReportNamespace()
        {
            var (ns, name) = OpenLineageExporter.ResolveNamespace("report:SalesChart", "sid");
            Assert.Equal("etl-sql://report/SalesChart", ns);
            Assert.Equal("SalesChart", name);
        }

        [Fact]
        public void ResolveNamespace_DatasetNode_UsesDatasetNamespace()
        {
            var (ns, name) = OpenLineageExporter.ResolveNamespace("dataset:MonthlyRevenue", "sid");
            Assert.Equal("etl-sql://dataset/MonthlyRevenue", ns);
            Assert.Equal("MonthlyRevenue", name);
        }

        [Fact]
        public void ResolveNamespace_ExternalTable_UsesExternalNamespace()
        {
            var (ns, name) = OpenLineageExporter.ResolveNamespace("prod.dbo.Customers", "sid");
            Assert.Equal("etl-sql://external", ns);
            Assert.Equal("prod.dbo.Customers", name);
        }

        // ── JSON structure ──────────────────────────────────────────────────

        [Fact]
        public void BuildRunEvent_EmptyTracker_ProducesValidJson()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            var json = OpenLineageExporter.BuildRunEvent(tracker, "test-session", "test-script");

            // Must be valid JSON
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("COMPLETE", root.GetProperty("eventType").GetString());
            Assert.Equal("etl-sql", root.GetProperty("job").GetProperty("namespace").GetString());
            Assert.Equal("test-script", root.GetProperty("job").GetProperty("name").GetString());
            Assert.True(root.TryGetProperty("run", out var run));
            Assert.True(run.TryGetProperty("runId", out _));
            Assert.True(root.TryGetProperty("inputs", out _));
            Assert.True(root.TryGetProperty("outputs", out _));
        }

        [Fact]
        public void BuildRunEvent_WithLineage_IncludesInputsAndOutputs()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#enriched", new[] { "prod.dbo.Customers" }, "INSERT",
                targetColumn: "email",
                sourceColumns: new[] { "EmailAddress" },
                transformationKind: TransformationKind.FunctionCall,
                transformationExpression: "LOWER(EmailAddress)");

            var json = OpenLineageExporter.BuildRunEvent(tracker, "s1", "etl-job");
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // prod.dbo.Customers is a pure source → inputs
            var inputs = root.GetProperty("inputs").EnumerateArray().ToList();
            Assert.Contains(inputs, i => i.GetProperty("name").GetString() == "prod.dbo.Customers");

            // #enriched is a target → outputs
            var outputs = root.GetProperty("outputs").EnumerateArray().ToList();
            Assert.Contains(outputs, o => o.GetProperty("name").GetString() == "#enriched");
        }

        [Fact]
        public void BuildRunEvent_WithColumnLineage_IncludesColumnLineageFacet()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#result", new[] { "src.dbo.Orders" }, "SELECT",
                targetColumn: "total",
                sourceColumns: new[] { "amount" },
                transformationKind: TransformationKind.Aggregation,
                transformationExpression: "SUM(amount)");

            var json = OpenLineageExporter.BuildRunEvent(tracker, "s1", "job");
            var doc = JsonDocument.Parse(json);
            var outputs = doc.RootElement.GetProperty("outputs").EnumerateArray().ToList();
            var result = outputs.First(o => o.GetProperty("name").GetString() == "#result");

            Assert.True(result.TryGetProperty("facets", out var facets));
            Assert.True(facets.TryGetProperty("columnLineage", out var cl));
            Assert.True(cl.TryGetProperty("fields", out var fields));
            Assert.True(fields.TryGetProperty("total", out var totalField));
            var inputFields = totalField.GetProperty("inputFields").EnumerateArray().ToList();
            Assert.NotEmpty(inputFields);
            var t = inputFields[0].GetProperty("transformations").EnumerateArray().First();
            Assert.Equal("INDIRECT", t.GetProperty("type").GetString());
            Assert.Equal("AGGREGATE", t.GetProperty("subtype").GetString());
        }

        [Fact]
        public void BuildRunEvent_WithMetadataTags_IncludesTagsFacet()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#out", new[] { "src.dbo.Patients" }, "SELECT",
                targetColumn: "ssn",
                metadata: new Dictionary<string, string> { ["pii"] = "true", ["classification"] = "restricted" });

            var json = OpenLineageExporter.BuildRunEvent(tracker, "s1", "job");
            var doc = JsonDocument.Parse(json);
            var outputs = doc.RootElement.GetProperty("outputs").EnumerateArray().ToList();
            var outNode = outputs.First(o => o.GetProperty("name").GetString() == "#out");

            Assert.True(outNode.TryGetProperty("facets", out var facets));
            Assert.True(facets.TryGetProperty("tags", out var tags));
            var tagList = tags.GetProperty("tags").EnumerateArray().ToList();
            Assert.Contains(tagList, t => t.GetProperty("name").GetString() == "pii" && t.GetProperty("value").GetString() == "true");
        }

        [Fact]
        public void BuildRunEvent_DirectTransformation_MapsToDirectType()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#out", new[] { "src.Orders" }, "SELECT",
                targetColumn: "id",
                sourceColumns: new[] { "OrderId" },
                transformationKind: TransformationKind.PassThrough);

            var json = OpenLineageExporter.BuildRunEvent(tracker, "s1", "j");
            var doc = JsonDocument.Parse(json);
            var outputs = doc.RootElement.GetProperty("outputs").EnumerateArray().ToList();
            var outNode = outputs.First(o => o.GetProperty("name").GetString() == "#out");
            var fields = outNode.GetProperty("facets").GetProperty("columnLineage").GetProperty("fields");
            var transform = fields.GetProperty("id").GetProperty("inputFields")
                .EnumerateArray().First()
                .GetProperty("transformations").EnumerateArray().First();
            Assert.Equal("DIRECT", transform.GetProperty("type").GetString());
        }

        // ── File export ─────────────────────────────────────────────────────

        [Fact]
        public async Task ExportToFile_AppendsJsonlLine()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#t", new[] { "src.T" }, "SELECT");

            var tmpFile = Path.GetTempFileName();
            try
            {
                await OpenLineageExporter.ExportToFileAsync(tracker, "sid", "job", tmpFile, NullLogger.Instance);
                var lines = await File.ReadAllLinesAsync(tmpFile);
                Assert.Single(lines, l => !string.IsNullOrWhiteSpace(l));
                // Each line must be valid JSON
                JsonDocument.Parse(lines[0]);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public async Task ExportToFile_AppendsTwice_TwoLines()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#t", new[] { "src.T" }, "SELECT");

            var tmpFile = Path.GetTempFileName();
            try
            {
                await OpenLineageExporter.ExportToFileAsync(tracker, "s1", "j", tmpFile, NullLogger.Instance);
                await OpenLineageExporter.ExportToFileAsync(tracker, "s2", "j", tmpFile, NullLogger.Instance);
                var lines = (await File.ReadAllLinesAsync(tmpFile))
                    .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                Assert.Equal(2, lines.Count);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        // ── End-to-end: parser + evaluator ─────────────────────────────────

        [Fact]
        public async Task ParseAndExecute_LineageExportOpenLineage_WritesFile()
        {
            var sp = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();

            var tmpFile = Path.GetTempFileName();
            try
            {
                var script = $@"
                    CREATE TABLE #src (id INT, name VARCHAR(50));
                    INSERT INTO #src VALUES (1, 'Alice');
                    CREATE TABLE #dst (id INT, name VARCHAR(50));
                    INSERT INTO #dst SELECT id, name FROM #src;
                    SHOW LINEAGE FOR #dst EXPORT AS OPENLINEAGE TO '{tmpFile.Replace("\\", "\\\\")}';";

                await TestHelpers.Execute(eval, script);

                var content = await File.ReadAllTextAsync(tmpFile);
                var nonEmpty = content.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                Assert.NotEmpty(nonEmpty);
                var doc = JsonDocument.Parse(nonEmpty[0]);
                Assert.Equal("COMPLETE", doc.RootElement.GetProperty("eventType").GetString());
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public async Task ParseAndExecute_WholeSessionExport_WritesFile()
        {
            var sp = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
            var eval = sp.GetRequiredService<Evaluator>();

            var tmpFile = Path.GetTempFileName();
            try
            {
                var script = $@"
                    CREATE TABLE #a (x INT);
                    INSERT INTO #a VALUES (1);
                    CREATE TABLE #b (x INT);
                    INSERT INTO #b SELECT x FROM #a;
                    SHOW LINEAGE EXPORT AS OPENLINEAGE TO '{tmpFile.Replace("\\", "\\\\")}';";

                await TestHelpers.Execute(eval, script);

                var content = await File.ReadAllTextAsync(tmpFile);
                Assert.Contains("COMPLETE", content);
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        // ── HTTP export ─────────────────────────────────────────────────────

        [Fact]
        public async Task ExportToHttp_PostsValidOpenLineageJson()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#t", new[] { "src.Orders" }, "SELECT",
                targetColumn: "total",
                sourceColumns: new[] { "amount" },
                transformationKind: TransformationKind.Aggregation,
                transformationExpression: "SUM(amount)");

            var port = GetFreePort();
            var prefix = $"http://localhost:{port}/ol/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            string? body = null;
            var serverTask = Task.Run(async () =>
            {
                var ctx = await listener.GetContextAsync();
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                body = await reader.ReadToEndAsync();
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            });

            await OpenLineageExporter.ExportToHttpAsync(tracker, "sid", "job", prefix, NullLogger.Instance);
            await serverTask;
            listener.Stop();

            Assert.NotNull(body);
            var doc = JsonDocument.Parse(body!);
            Assert.Equal("COMPLETE", doc.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("job", doc.RootElement.GetProperty("job").GetProperty("name").GetString());
        }

        [Fact]
        public async Task ExportToHttp_UnreachableEndpoint_LogsWarningWithoutThrowing()
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            tracker.Record("#t", new[] { "src.T" }, "SELECT");

            // Port 1 is reserved and will refuse the connection
            var ex = await Record.ExceptionAsync(() =>
                OpenLineageExporter.ExportToHttpAsync(tracker, "sid", "job",
                    "http://localhost:1/ol", NullLogger.Instance));

            Assert.Null(ex); // exception is caught and logged, not re-thrown
        }

        // ── Auto-export via config ──────────────────────────────────────────

        [Fact]
        public async Task AutoExport_ViaConfig_OpenLineageFile_WritesOnScriptCompletion()
        {
            var tmpFile = Path.GetTempFileName();
            try
            {
                var sp = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(
                    new Dictionary<string, string?> { ["Lineage:OpenLineageFile"] = tmpFile });
                var eval = sp.GetRequiredService<Evaluator>();

                await TestHelpers.Execute(eval, @"
                    CREATE TABLE #src (id INT);
                    INSERT INTO #src VALUES (1);
                    CREATE TABLE #dst (id INT);
                    INSERT INTO #dst SELECT id FROM #src;");

                var lines = (await File.ReadAllLinesAsync(tmpFile))
                    .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                Assert.Single(lines);
                var doc = JsonDocument.Parse(lines[0]);
                Assert.Equal("COMPLETE", doc.RootElement.GetProperty("eventType").GetString());
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        [Fact]
        public async Task AutoExport_ViaConfig_NestedScript_WritesOnlyOnce()
        {
            var tmpFile = Path.GetTempFileName();
            try
            {
                var sp = (ServiceProvider)DependencyInjectionSetup.BuildServiceProvider(
                    new Dictionary<string, string?> { ["Lineage:OpenLineageFile"] = tmpFile });
                var eval = sp.GetRequiredService<Evaluator>();

                // Two separate top-level Evaluate calls → two JSONL lines
                await TestHelpers.Execute(eval, "CREATE TABLE #a (x INT); INSERT INTO #a VALUES (1);");
                await TestHelpers.Execute(eval, "CREATE TABLE #b (x INT); INSERT INTO #b SELECT x FROM #a;");

                var lines = (await File.ReadAllLinesAsync(tmpFile))
                    .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                Assert.Equal(2, lines.Count);
                foreach (var line in lines)
                {
                    var doc = JsonDocument.Parse(line);
                    Assert.Equal("COMPLETE", doc.RootElement.GetProperty("eventType").GetString());
                }
            }
            finally
            {
                File.Delete(tmpFile);
            }
        }

        private static int GetFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
