using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.ReportPortal;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
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
                    new ScheduleInfo(1, "HOUR"),
                    new PrintStatement([new LiteralExpression("docker smoke", TokenType.STRING)]));

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
    }

    [Trait("Connector", "REPORTPORTAL")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    [Trait("Category", "Integration")]
    [Collection("Report portal collection")]
    public class ReportPortalDockerIntegrationTests
    {
        private readonly ReportPortalFixture _fixture;

        public ReportPortalDockerIntegrationTests(ReportPortalFixture fixture) => _fixture = fixture;

        [Fact]
        public async Task Connector_AuthenticatesAndShowsUsers_AgainstDockerPortal()
        {
            var ds = new ReportPortalDataSource(
                _fixture.BaseUrl,
                ReportPortalFixture.AdminUser,
                ReportPortalFixture.AdminPassword,
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
                    string.Equals(row["Username"]?.ToString(), ReportPortalFixture.AdminUser, StringComparison.OrdinalIgnoreCase));
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
                .WithEnvironment("Orchestrator__ScriptRoot", "/app/Reports")
                .WithEnvironment("Jobs__UseProcessSpawning", "false")
                .WithEnvironment("Session__Root", "/app/Sessions")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                    r.ForPort(ServicePort).ForPath("/health")))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(ServicePort);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
                await _container.StopAsync();
        }
    }

    public sealed class ReportPortalFixture : IAsyncLifetime
    {
        public const string AdminUser = "admin";
        public const string AdminPassword = "Admin@123456!";
        private const string InitialAdminPassword = "Admin@12345!";
        private const string ImageName = "etl-sql-reportportal-test:latest";
        private const int PortalPort = 5002;

        private IContainer? _container;

        public string BaseUrl => $"http://localhost:{Port}";
        public int Port { get; private set; }

        public async Task InitializeAsync()
        {
            await PlatformFixtureHelpers.BuildDockerImageAsync(
                "src/ETL-SQL.ReportPortal/Dockerfile",
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
                await _container.StopAsync();
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
            var output = new StringBuilder();
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add("--progress=plain");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(dockerfile);
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(imageName);
            startInfo.ArgumentList.Add(".");

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start docker build.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(600_000));
            if (!completed)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException($"Docker image build timed out for {imageName}.{Environment.NewLine}{output}");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Docker image build failed for {imageName} with exit code {process.ExitCode}.{Environment.NewLine}{output}");
            }
        }
    }

    [CollectionDefinition("Orchestrator service collection")]
    public class OrchestratorServiceCollection : ICollectionFixture<OrchestratorServiceFixture> { }

    [CollectionDefinition("Report portal collection")]
    public class ReportPortalCollection : ICollectionFixture<ReportPortalFixture> { }

    internal static class PlatformConnectorContextLock
    {
        public static readonly System.Threading.SemaphoreSlim Semaphore = new(1, 1);
    }
}
