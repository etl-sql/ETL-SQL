using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.TestSupport;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Trait("Connector", "ORCHESTRATOR")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    [Trait("Category", "Integration")]
    [Collection("Orchestrator service collection")]
    public class OrchestratorServiceDockerIntegrationTests
    {
        private readonly OrchestratorServiceFixture _fixture;

        public OrchestratorServiceDockerIntegrationTests(OrchestratorServiceFixture fixture) => _fixture = fixture;

        [Fact]
        public async Task Connector_CreatesAndListsScheduledJob_AgainstDockerService()
        {
            var ds = new OrchestratorDataSource(_fixture.BaseUrl, OrchestratorServiceFixture.ApiKey, NullLogger.Instance);
            var context = SystemExecutionContext.Instance;
            await PlatformConnectorContextLock.Semaphore.WaitAsync();
            try
            {
                context.LastResult = null;
                context.LastResultSets.Clear();

                var jobName = "docker_smoke_" + Guid.NewGuid().ToString("N");
                var create = new CreateJobStatement(
                    jobName,
                    JobTargetKind.Script,
                    "docker-smoke.etlsql");

                await ds.ExecuteAdminStatementAsync(create, context);
                await ds.ExecuteAdminStatementAsync(new ShowJobsStatement(), context);

                Assert.NotNull(context.LastResult);
                Assert.Contains(context.LastResult!.Rows, row =>
                    string.Equals(row["Name"]?.ToString(), jobName, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                PlatformConnectorContextLock.Semaphore.Release();
            }
        }

        [Fact]
        public async Task Service_ExecutesScheduledJob_AgainstDockerService()
        {
            using var http = _fixture.CreateClient();
            var jobName = "docker_exec_" + Guid.NewGuid().ToString("N");

            var create = await http.PostAsJsonAsync("/api/scheduled-jobs", new
            {
                Name = jobName,
                ScriptText = "PRINT 'docker scheduled execution';",
                Interval = 1,
                Unit = "HOUR",
                MaxRetries = 0,
                RetryDelaySeconds = 30,
                HashPolicy = "Warn"
            });
            create.EnsureSuccessStatusCode();

            var history = await _fixture.PollHistoryUntilCompletedAsync(http, jobName);
            var entry = Assert.Single(history);
            Assert.True(
                string.Equals("SUCCESS", entry.GetProperty("status").GetString(), StringComparison.Ordinal),
                $"Expected SUCCESS but got {entry.GetProperty("status").GetString()}: {entry.GetProperty("errorMessage").GetString()}");
            Assert.True(entry.GetProperty("endTime").ValueKind != JsonValueKind.Null);
            Assert.True(entry.TryGetProperty("peakMemoryBytes", out _));
            Assert.True(entry.TryGetProperty("rowsProcessed", out _));
            Assert.True(entry.TryGetProperty("cpuTimeSeconds", out _));

            var jobs = await http.GetFromJsonAsync<JsonElement>("/api/scheduled-jobs");
            var job = jobs.EnumerateArray().Single(j =>
                string.Equals(j.GetProperty("name").GetString(), jobName, StringComparison.OrdinalIgnoreCase));
            Assert.True(job.GetProperty("lastRun").ValueKind != JsonValueKind.Null);
            Assert.True(job.GetProperty("nextRun").ValueKind != JsonValueKind.Null);
        }
    }

    [Trait("Connector", "PORTAL")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    [Trait("Category", "Integration")]
    [Collection("Portal collection")]
    public class PortalDockerIntegrationTests
    {
        private readonly PortalFixture _fixture;

        public PortalDockerIntegrationTests(PortalFixture fixture) => _fixture = fixture;

        [Fact]
        public async Task Connector_AuthenticatesAndShowsUsers_AgainstDockerPortal()
        {
            var ds = new PortalDataSource(
                _fixture.BaseUrl,
                PortalFixture.AdminUser,
                PortalFixture.AdminPassword,
                NullLogger.Instance);
            var context = SystemExecutionContext.Instance;
            await PlatformConnectorContextLock.Semaphore.WaitAsync();
            try
            {
                context.LastResult = null;
                context.LastResultSets.Clear();

                await ds.ExecuteAdminStatementAsync(new ShowPortalUsersStatement(), context);

                Assert.NotNull(context.LastResult);
                Assert.Contains(context.LastResult!.Rows, row =>
                    string.Equals(row["Username"]?.ToString(), PortalFixture.AdminUser, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                PlatformConnectorContextLock.Semaphore.Release();
            }
        }
    }

    public sealed class OrchestratorServiceFixture : IAsyncLifetime
    {
        public const string ApiKey = "docker-orchestrator-smoke-key";
        private const string ImageName = "etl-sql-orchestrator-service-test:latest";
        private const int ServicePort = 5001;

        private IContainer? _container;

        public string BaseUrl => $"http://localhost:{Port}";
        public int Port { get; private set; }

        public HttpClient CreateClient()
        {
            var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            http.DefaultRequestHeaders.Add("X-Orchestrator-Key", ApiKey);
            return http;
        }

        // Poll timeout must exceed the container's own Jobs__TimeoutSeconds (30s) with headroom: the
        // job runs via process spawning inside the container, and under full-lane load (concurrent
        // Docker image builds + container startup) the spawn+execute can approach 30s. A 30s poll would
        // then give up before a job that ultimately succeeds, producing a flaky timeout.
        public async Task<JsonElement[]> PollHistoryUntilCompletedAsync(HttpClient http, string jobName, int timeoutSeconds = 90)
        {
            return await LoadAwareWait.UntilAsync(
                $"completed Docker service history for job '{jobName}'",
                async cancellationToken =>
                {
                    var history = await http.GetFromJsonAsync<JsonElement[]>(
                        $"/api/scheduled-jobs/{Uri.EscapeDataString(jobName)}/history",
                        cancellationToken);
                    return history?
                        .Where(h => h.GetProperty("endTime").ValueKind != JsonValueKind.Null)
                        .ToArray() ?? [];
                },
                completed => completed.Length > 0,
                TimeSpan.FromSeconds(timeoutSeconds),
                pollInterval: TimeSpan.FromMilliseconds(500),
                describe: completed => $"completed history entries={completed.Length}");
        }

        public async Task InitializeAsync()
        {
            await PlatformFixtureHelpers.BuildDockerImageAsync(
                "src/ETL-SQL.Orchestrator.Service/Dockerfile",
                ImageName);

            _container = new ContainerBuilder(ImageName)
                .WithName("etl-sql-orchestrator")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithImagePullPolicy(PullPolicy.Never)
                .WithPortBinding(ServicePort, true)
                .WithEnvironment("ASPNETCORE_URLS", $"http://+:{ServicePort}")
                .WithEnvironment("Orchestrator__ApiKey", ApiKey)
                .WithEnvironment("Orchestrator__RequireFederatedIdentity", "false")
                .WithEnvironment("Orchestrator__IdentitySigningSecret", "docker-orchestrator-smoke-key-32bytes")
                .WithEnvironment("Orchestrator__DatabasePath", "/app/data/etlsql.db")
                .WithEnvironment("Orchestrator__ScriptRoot", "/app/Reports")
                .WithEnvironment("Jobs__UseProcessSpawning", "true")
                .WithEnvironment("Jobs__TimeoutSeconds", "30")
                .WithEnvironment("Session__Root", "/app/Sessions")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                    r.ForPort(ServicePort).ForPath("/health")))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(ServicePort);
        }

        public async Task DisposeAsync()
        {
            // DisposeAsync removes the container (and its anonymous volumes); StopAsync would leak
            // both if Ryuk never runs (e.g. a killed/crashed test process).
            if (_container != null)
                await _container.DisposeAsync();

            // The image is built fresh every run via raw `docker build`, so Ryuk does not track it.
            // Remove it on teardown to stop dangling `<none>` layers from accumulating run over run.
            await PlatformFixtureHelpers.RemoveDockerImageAsync(ImageName);
        }
    }

    public sealed class PortalFixture : IAsyncLifetime
    {
        public const string AdminUser = "admin";
        public const string AdminPassword = "Admin@123456!";
        private const string InitialAdminPassword = "Admin@12345!";
        private const string ImageName = "etl-sql-portal-test:latest";
        private const int PortalPort = 5002;

        private IContainer? _container;

        public string BaseUrl => $"http://localhost:{Port}";
        public int Port { get; private set; }

        public async Task InitializeAsync()
        {
            await PlatformFixtureHelpers.BuildDockerImageAsync(
                "src/ETL-SQL.Portal/Dockerfile",
                ImageName);

            _container = new ContainerBuilder(ImageName)
                .WithName("etl-sql-portal")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithImagePullPolicy(PullPolicy.Never)
                .WithPortBinding(PortalPort, true)
                .WithEnvironment("ASPNETCORE_URLS", $"http://+:{PortalPort}")
                .WithEnvironment("Portal__DatabasePath", "/app/data/portal.db")
                .WithEnvironment("Portal__ScriptRootPath", "/app/Reports")
                .WithEnvironment("Portal__SnapshotDirectory", "/app/Snapshots")
                .WithEnvironment("Portal__Jwt__Secret", "DockerSmokeJwtSecretThatIsAtLeast32Chars!")
                .WithEnvironment("Portal__FirstRun__AdminUsername", AdminUser)
                .WithEnvironment("Portal__FirstRun__AdminPassword", InitialAdminPassword)
                .WithEnvironment("Portal__Dataset__AllowMachineFallback", "true")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                    r.ForPort(PortalPort).ForPath("/health")))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(PortalPort);
            await ChangeInitialPasswordAsync();
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
                await _container.DisposeAsync();

            await PlatformFixtureHelpers.RemoveDockerImageAsync(ImageName);
        }

        private async Task ChangeInitialPasswordAsync()
        {
            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/") };
            var login = await http.PostAsJsonAsync("api/auth/login", new
            {
                Username = AdminUser,
                Password = InitialAdminPassword
            });
            login.EnsureSuccessStatusCode();

            var body = await login.Content.ReadFromJsonAsync<JsonElement>();
            var token = body.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("Portal login did not return a token.");

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var change = await http.PostAsJsonAsync("api/auth/change-password", new
            {
                CurrentPassword = InitialAdminPassword,
                NewPassword = AdminPassword
            });
            change.EnsureSuccessStatusCode();
        }
    }

    internal static class PlatformFixtureHelpers
    {
        public static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("Cannot find repo root (.git directory).");
        }

        public static async Task BuildDockerImageAsync(string dockerfile, string imageName)
        {
            var repoRoot = FindRepoRoot();
            const int maxAttempts = 3;
            var lastExitCode = -1;
            var lastOutput = string.Empty;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var (exitCode, output) = await RunDockerAsync(
                    repoRoot,
                    timeoutMs: 600_000,
                    "build", "--progress=plain", "-f", dockerfile, "-t", imageName, ".");

                if (exitCode == 0)
                {
                    return;
                }

                lastExitCode = exitCode;
                lastOutput = output;

                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
                }
            }

            throw new InvalidOperationException(
                $"Docker image build failed for {imageName} after {maxAttempts} attempts with exit code {lastExitCode}.{Environment.NewLine}{lastOutput}");
        }

        /// <summary>
        /// Removes a locally-built image so re-tagging <c>:latest</c> on the next run does not
        /// orphan the previous layers as dangling images. Best-effort: failures are swallowed so
        /// teardown never masks a test result.
        /// </summary>
        public static async Task RemoveDockerImageAsync(string imageName)
        {
            try
            {
                await RunDockerAsync(FindRepoRoot(), timeoutMs: 60_000, "rmi", "-f", imageName);
            }
            catch
            {
                // Image may already be gone, or docker unavailable during teardown — ignore.
            }
        }

        private static async Task<(int ExitCode, string Output)> RunDockerAsync(
            string workingDirectory, int timeoutMs, params string[] args)
        {
            var output = new StringBuilder();
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start docker process.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeoutMs));
            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException($"Docker command timed out: docker {string.Join(' ', args)}.{Environment.NewLine}{output}");
            }

            return (process.ExitCode, output.ToString());
        }
    }

    [CollectionDefinition("Orchestrator service collection")]
    public class OrchestratorServiceCollection : ICollectionFixture<OrchestratorServiceFixture> { }

    [CollectionDefinition("Portal collection")]
    public class PortalCollection : ICollectionFixture<PortalFixture> { }

    internal static class PlatformConnectorContextLock
    {
        public static readonly System.Threading.SemaphoreSlim Semaphore = new(1, 1);
    }
}
