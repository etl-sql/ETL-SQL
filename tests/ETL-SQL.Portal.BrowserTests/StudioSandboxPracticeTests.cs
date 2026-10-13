using System.Security.Cryptography;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Portal.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>Opt-in real Docker/runc practice acceptance; Standard tier, not hostile-tenant certification.</summary>
[Trait("Category", "Integration")]
public sealed class StudioSandboxPracticeTests
{
    [Fact]
    public async Task PublisherPracticeCarriesIdentityDeniesEgressAndRemovesEverySandbox()
    {
        var image = await new ProcessSandboxCommandRunner().RunAsync("docker",
            ["image", "inspect", "etlsql-sandbox-worker-test:local", "--format", "{{.Id}}"]);
        Assert.True(image.ExitCode == 0, "Build the worker first with scripts/Test-SandboxWorkerImage.ps1 -Tag etlsql-sandbox-worker-test:local.");
        using var orchestrator = new SandboxOrchestratorFactory(image.StandardOutput.Trim());
        var fixture = new PortalJourneyFixture(orchestrator);
        try
        {
            await fixture.InitializeAsync();
            await new StudioPortalPracticeTests(fixture) { RequireSandboxEvidence = true }
                .ALearnerFailsRepairsLoadsAndCleansThePracticePipeline();
            Assert.True(orchestrator.Provider.Attempts.Count >= 6);
            var learnerAttempts = orchestrator.Provider.Attempts
                .Where(attempt => attempt.Request.ExecutionIdentity!.EffectiveHasRole("Publisher")).ToList();
            Assert.True(learnerAttempts.Count >= 5);
            Assert.All(learnerAttempts, attempt =>
            {
                Assert.Equal("portal-host", attempt.Request.ExecutionIdentity!.TenantId);
                Assert.False(attempt.Request.ExecutionIdentity.IsAdmin);
                Assert.True(attempt.Request.ExecutionIdentity.EffectiveHasRole("Publisher"));
                Assert.False(Directory.Exists(attempt.Root));
                Assert.Equal(SandboxIsolationTier.Standard, attempt.Evidence.IsolationTier);
            });
            Assert.True(orchestrator.Provider.Attempts.Count(a => a.Status == SandboxTerminalStatus.Failed) >= 2);
            Assert.True(orchestrator.Provider.Attempts.Count(a => a.Status == SandboxTerminalStatus.Succeeded) >= 3);
        }
        finally { await fixture.DisposeAsync(); }
    }

    private sealed class SandboxOrchestratorFactory(string imageId) : OrchestratorWebFactory(requireFederatedIdentity: true)
    {
        public RecordingProvider Provider { get; private set; } = null!;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                var root = Path.Combine(TempDir, "sandbox");
                var keys = Directory.CreateDirectory(Path.Combine(root, "keys")).FullName;
                File.WriteAllText(Path.Combine(keys, "portal-host.key"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                var docker = new ProcessSandboxCommandRunner();
                var image = "etlsql-sandbox-worker-test:local";
                // The actual local image identity is required; no mocked provider or executor fallback.
                var options = new DockerSandboxExecutionOptions
                {
                    Mode = DockerSandboxMode.Standard,
                    Image = image,
                    LocalImageId = imageId,
                    Runtime = "runc",
                    HostPolicyVersion = "studio-standard-v1",
                    SessionRoot = Path.Combine(root, "sessions"),
                    MachineKeyRoot = keys,
                    Entrypoint = "/app/etl-sql"
                };
                var artifacts = new FileSystemImmutableSandboxArtifactStore(
                    new ImmutableSandboxArtifactStoreOptions { RootPath = Path.Combine(root, "artifacts") });
                Provider = new RecordingProvider(new DockerSandboxExecutionProvider(options, docker, artifacts), docker);
                var profiles = new SandboxWorkloadPolicyResolver(new SandboxWorkloadPolicyCatalog
                {
                    Profiles = new Dictionary<string, SandboxExecutionProfile>
                    {
                        ["practice"] = new()
                        {
                            PoolId = "practice",
                            IsolationTier = SandboxIsolationTier.Standard,
                            Limits = new SandboxResourceLimits
                            {
                                MaxDuration = TimeSpan.FromSeconds(60),
                                MaxMemoryBytes = 1024L * 1024 * 1024,
                                MaxScratchBytes = 128L * 1024 * 1024,
                                MaxProcesses = 128,
                                MaxCpuCores = 1,
                                MaxConnectorConcurrency = 4
                            }
                        }
                    },
                    Tenants = new Dictionary<string, SandboxTenantAdmissionPolicy>
                    {
                        ["portal-host"] = new()
                        {
                            DefaultProfile = "practice",
                            AllowedProfiles = ["practice"],
                            Weight = 1,
                            MaxConcurrentAttempts = 1,
                            MaxQueuedAttempts = 2
                        }
                    }
                });
                var coordinator = new SandboxExecutionCoordinator(
                    new FileSystemSandboxWorkspaceProvider(new FileSystemSandboxWorkspaceOptions { RootPath = Path.Combine(root, "workspaces") }),
                    new FairShareSandboxAdmissionController(new SandboxAdmissionControllerOptions
                    { PoolCapacities = new Dictionary<string, int> { ["practice"] = 1 } }));
                services.AddSingleton<ISandboxScheduledJobExecutor>(new SandboxScheduledJobExecutor(
                    new SandboxScheduledJobExecutorOptions { PolicyVersion = "studio-practice-v1", BindingVersion = "studio-v1" },
                    new SandboxTenantContextResolver("portal-host"), profiles, artifacts, coordinator, Provider));
            });
        }
    }

    private sealed record AttemptEvidence(SandboxWorkloadRequest Request, string Root, SandboxProviderEvidence Evidence)
    {
        public SandboxTerminalStatus? Status { get; set; }
    }

    private sealed class RecordingProvider(ISandboxExecutionProvider provider, ISandboxCommandRunner commands) : ISandboxExecutionProvider
    {
        public List<AttemptEvidence> Attempts { get; } = [];
        public async Task<ISandboxAttempt> PrepareAsync(SandboxWorkloadRequest request, SandboxWorkspaceAssignment workspace, CancellationToken cancellationToken)
        {
            var attempt = await provider.PrepareAsync(request, workspace, cancellationToken);
            var network = await commands.RunAsync("docker", ["inspect", "--format", "{{.HostConfig.NetworkMode}}", "etlsql-" + workspace.AssignmentId], cancellationToken);
            Assert.Equal(0, network.ExitCode);
            Assert.Equal("none", network.StandardOutput.Trim());
            var evidence = new AttemptEvidence(request, workspace.RootPath, attempt.Evidence);
            Attempts.Add(evidence);
            return new RecordingAttempt(attempt, evidence);
        }
    }

    private sealed class RecordingAttempt(ISandboxAttempt attempt, AttemptEvidence evidence) : ISandboxAttempt
    {
        public SandboxProviderEvidence Evidence => attempt.Evidence;
        public async Task<SandboxExecutionOutcome> RunAsync(CancellationToken cancellationToken)
        {
            var result = await attempt.RunAsync(cancellationToken);
            evidence.Status = result.Status;
            return result;
        }
        public Task DestroyAsync(CancellationToken cancellationToken) => attempt.DestroyAsync(cancellationToken);
    }
}
