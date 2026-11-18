using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.Portal.Tests;

public sealed class JobDependencyProjectionTests : IDisposable
{
    private readonly string _tempDir;

    public JobDependencyProjectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "etlsql-or05-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    private OrchestratorController CreateController(FakeOrchestratorHandler handler)
    {
        var config = new PortalConfig
        {
            Orchestrator = { ApiUrl = "https://orchestrator.example.invalid" }
        };
        var protector = new OrchestratorApiKeyProtector(DataProtectionProvider.Create(_tempDir));
        var settings = new OrchestratorSettingsService(config, protector);
        var proxy = new OrchestratorProxyService(
            new HttpClient(handler),
            settings,
            NullLogger<OrchestratorProxyService>.Instance);
        return new OrchestratorController(proxy, null!, new ScriptDagProjectionService(), null!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetJobDependencies_NormalizedAndBundleJobs_ResolvesTableAndExplicitEdges(bool nestedFile)
    {
        var handler = new FakeOrchestratorHandler
        {
            Jobs =
            [
                new JobDefinitionDto(
                    Name: "seed_job",
                    Script: "SELECT 1;",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn"),
                new JobDefinitionDto(
                    Name: "extract_job",
                    Script: "RUN SCRIPT 'etl/extract.etlsql';",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn",
                    JobType: JobTargetKind.Script,
                    TargetPath: "etl/extract.etlsql"),
                new JobDefinitionDto(
                    Name: "transform_job",
                    Script: "RUN SCRIPT 'etl/transform.etlsql';",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn",
                    JobType: JobTargetKind.Script,
                    TargetPath: "etl/transform.etlsql"),
                new JobDefinitionDto(
                    Name: "bundle_job",
                    Script: "RUN SCRIPT 'orch://sales-etl@2/main.etlsql';",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn",
                    Options: "{\"DependsOn\":[\"seed_job\"]}",
                    JobType: JobTargetKind.Script,
                    TargetPath: "orch://sales-etl@2/main.etlsql"),
                new JobDefinitionDto(
                    Name: "consumer_job",
                    Script: "SELECT * INTO #out FROM warehouse.DailySales;",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn"),
                new JobDefinitionDto(
                    Name: "audit_job",
                    Script: "SELECT 1;",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn"),
                new JobDefinitionDto(
                    Name: "notify_job",
                    Script: "SELECT 1;",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn")
            ],
            Scripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["etl/extract.etlsql"] = "SELECT * INTO warehouse.StageOrders FROM erp.Orders;",
                ["etl/transform.etlsql"] = """
                    INSERT INTO warehouse.FactOrders SELECT * FROM warehouse.StageOrders;
                    TRIGGER JOB audit_job;
                    """
            },
            BundleFiles = new Dictionary<(string, int), List<BundleFileInfo>>
            {
                [("sales-etl", 2)] =
                [
                    new BundleFileInfo("sales-etl", 2, "main.etlsql", "RUN SCRIPT 'steps/aggregate.etlsql';", "h1", 38, "text/plain"),
                    new BundleFileInfo("sales-etl", 2, "steps/aggregate.etlsql", """
                        ASSERT JOB extract_job (ROW_COUNT >= 0) ON FAILURE THROW;
                        SELECT * INTO warehouse.DailySales FROM warehouse.FactOrders;
                        TRIGGER JOB notify_job;
                        """, "h2", 90, "text/plain")
                ]
            }
        };

        if (nestedFile)
        {
            handler.Scripts["etl/steps/transform.etlsql"] = handler.Scripts["etl/transform.etlsql"];
            handler.Scripts["etl/transform.etlsql"] = "RUN SCRIPT './steps/transform.etlsql';";
        }
        var controller = CreateController(handler);

        // 1. Normalized transform_job sees upstream normalized extract_job, downstream bundle_job, and trigger audit_job.
        var transformAction = await controller.GetJobDependencies("transform_job");
        var transformOk = Assert.IsType<OkObjectResult>(transformAction);
        var transformChain = Assert.IsType<JobDependencyChainDto>(transformOk.Value);

        Assert.Contains("extract_job", transformChain.Upstream);
        Assert.Contains("bundle_job", transformChain.Downstream);
        Assert.Contains("audit_job", transformChain.Downstream);
        Assert.Contains(transformChain.Edges, e => e.From == "extract_job" && e.To == "transform_job" && e.Type == "Data");
        Assert.Contains(transformChain.Edges, e => e.From == "transform_job" && e.To == "bundle_job" && e.Type == "Data");
        Assert.Contains(transformChain.Edges, e => e.From == "transform_job" && e.To == "audit_job" && e.Type == "Trigger");

        // 2. Bundle job resolves relative nested entry, sees data upstream, Options.DependsOn, ASSERT JOB,
        //    TRIGGER JOB, and downstream consumer.
        var bundleAction = await controller.GetJobDependencies("bundle_job");
        var bundleOk = Assert.IsType<OkObjectResult>(bundleAction);
        var bundleChain = Assert.IsType<JobDependencyChainDto>(bundleOk.Value);

        Assert.Contains("transform_job", bundleChain.Upstream);
        Assert.Contains("seed_job", bundleChain.Upstream);
        Assert.Contains("extract_job", bundleChain.Upstream);
        Assert.Contains("consumer_job", bundleChain.Downstream);
        Assert.Contains("notify_job", bundleChain.Downstream);
        Assert.Contains(bundleChain.Edges, e => e.From == "seed_job" && e.To == "bundle_job" && e.Type == "Dependency");
        Assert.Contains(bundleChain.Edges, e => e.From == "extract_job" && e.To == "bundle_job" && e.Type == "Dependency");
        Assert.Contains(bundleChain.Edges, e => e.From == "transform_job" && e.To == "bundle_job" && e.Type == "Data");
        Assert.Contains(bundleChain.Edges, e => e.From == "bundle_job" && e.To == "consumer_job" && e.Type == "Data");
        Assert.Contains(bundleChain.Edges, e => e.From == "bundle_job" && e.To == "notify_job" && e.Type == "Trigger");
    }

    [Fact]
    public async Task GetJobDependencies_CyclicAndUnpinnedBundle_TerminatesAndResolvesLatestVersion()
    {
        var handler = new FakeOrchestratorHandler
        {
            Jobs =
            [
                new JobDefinitionDto(
                    Name: "unpinned_bundle_job",
                    Script: "RUN SCRIPT 'orch://cyclic/a.etlsql';",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn",
                    JobType: JobTargetKind.Script,
                    TargetPath: "orch://cyclic/a.etlsql"),
                new JobDefinitionDto(
                    Name: "reader_job",
                    Script: "SELECT * INTO warehouse.Final FROM warehouse.Stage;",
                    Interval: 60,
                    Unit: "MINUTE",
                    AtTime: null,
                    LastRun: null,
                    NextRun: null,
                    IsEnabled: true,
                    MaxRetries: 0,
                    RetryDelaySeconds: 30,
                    ScriptHash: null,
                    HashPolicy: "Warn")
            ],
            BundleVersions = new Dictionary<string, List<BundleVersionInfo>>(StringComparer.OrdinalIgnoreCase)
            {
                ["cyclic"] =
                [
                    new BundleVersionInfo("cyclic", 1, "a.etlsql", "h1", DateTime.UtcNow.AddDays(-1), "dev", null),
                    new BundleVersionInfo("cyclic", 3, "a.etlsql", "h3", DateTime.UtcNow, "dev", null)
                ]
            },
            BundleFiles = new Dictionary<(string, int), List<BundleFileInfo>>
            {
                [("cyclic", 3)] =
                [
                    new BundleFileInfo("cyclic", 3, "a.etlsql", "RUN SCRIPT 'b.etlsql'; SELECT * INTO warehouse.Stage FROM source_conn.Events;", "ha", 60, "text/plain"),
                    new BundleFileInfo("cyclic", 3, "b.etlsql", "RUN SCRIPT 'a.etlsql';", "hb", 20, "text/plain")
                ]
            }
        };

        var controller = CreateController(handler);
        var result = await controller.GetJobDependencies("unpinned_bundle_job");
        var ok = Assert.IsType<OkObjectResult>(result);
        var chain = Assert.IsType<JobDependencyChainDto>(ok.Value);

        Assert.Contains("reader_job", chain.Downstream);
        Assert.Contains(chain.Edges, e => e.From == "unpinned_bundle_job" && e.To == "reader_job" && e.Type == "Data");
        Assert.IsType<NotFoundObjectResult>(await controller.GetJobDependencies("does_not_exist"));
    }

    private sealed class FakeOrchestratorHandler : HttpMessageHandler
    {
        public List<JobDefinitionDto> Jobs { get; init; } = [];
        public Dictionary<string, string> Scripts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<BundleVersionInfo>> BundleVersions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(string Bundle, int Version), List<BundleFileInfo>> BundleFiles { get; init; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Equals("/api/scheduled-jobs", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(JsonResponse(Jobs));

            if (path.Equals("/api/scripts/content", StringComparison.OrdinalIgnoreCase))
            {
                var query = request.RequestUri?.Query ?? "";
                var prefix = "?path=";
                var rawPath = query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? Uri.UnescapeDataString(query[prefix.Length..])
                    : "";
                if (Scripts.TryGetValue(rawPath, out var content))
                    return Task.FromResult(JsonResponse(new { Path = rawPath, Content = content }));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (path.StartsWith("/api/bundles/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = path["/api/bundles/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1].Equals("versions", StringComparison.OrdinalIgnoreCase))
                {
                    var bundleName = Uri.UnescapeDataString(parts[0]);
                    return Task.FromResult(JsonResponse(
                        BundleVersions.TryGetValue(bundleName, out var vers) ? vers : []));
                }

                if (parts.Length == 4
                    && parts[1].Equals("versions", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[2], out var version))
                {
                    var bundleName = Uri.UnescapeDataString(parts[0]);
                    if (parts[3].Equals("files", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(JsonResponse(
                            BundleFiles.TryGetValue((bundleName, version), out var files) ? files : []));
                    }
                    if (parts[3].Equals("dependencies", StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(JsonResponse(Array.Empty<BundleDependencyInfo>()));
                    }
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(value, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
    }
}
