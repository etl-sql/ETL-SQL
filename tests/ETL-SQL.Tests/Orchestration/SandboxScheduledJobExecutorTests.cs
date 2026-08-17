using ETL_SQL.Core.Data;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SandboxScheduledJobExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"etlsql-scheduled-sandbox-{Guid.NewGuid():N}");

    [Fact]
    public async Task JobBindingAndServerPolicyReachProviderWithImmutableArtifactAndAdmission()
    {
        var provider = new CapturingProvider(new ScriptExecutionResult(true, 19, SessionId: "session-a"));
        var executor = CreateExecutor(provider, hostTenant: "tenant-a");
        var job = Job("tenant-a");

        var result = await executor.ExecuteAsync(
            job, 42, 2, "session-a", true, 11,
            new Dictionary<string, string> { ["region"] = "west" }, default);

        Assert.True(result.Success);
        Assert.Equal(19, result.RowsProcessed);
        var request = Assert.IsType<SandboxWorkloadRequest>(provider.Request);
        Assert.Equal("tenant-a", request.Assignment.Tenant.Tenant.Value);
        Assert.Equal(TenantContextOrigin.HostFixed, request.Assignment.Tenant.Origin);
        Assert.Equal("history-42", request.Assignment.RunId);
        Assert.Equal("attempt-2", request.Assignment.AttemptId);
        Assert.Equal(SandboxIsolationTier.Dedicated, request.RequiredIsolationTier);
        Assert.Equal("dedicated-tenant-a", request.AdmissionPolicy.PoolId);
        Assert.StartsWith("sha256-", request.ArtifactId, StringComparison.Ordinal);
        Assert.StartsWith("sha256:", request.ArtifactHash, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(request.AdmissionId));
        Assert.True(request.ResumeFromCheckpoint);
        Assert.Equal("session-a", request.CheckpointHandle);
        Assert.Equal("west", request.VariableOverrides["region"]);
        Assert.False(Directory.Exists(provider.WorkspaceRoot));
    }

    [Fact]
    public async Task HostFixedTenantCannotBeReplacedByPersistedForeignJobBinding()
    {
        var provider = new CapturingProvider(new ScriptExecutionResult(true, 0));
        var executor = CreateExecutor(provider, hostTenant: "tenant-a");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync(
            Job("tenant-b"), 1, 1, "session-a", false, 0, null, default));

        Assert.Null(provider.Request);
    }

    private SandboxScheduledJobExecutor CreateExecutor(CapturingProvider provider, string hostTenant)
    {
        var workspace = new FileSystemSandboxWorkspaceProvider(
            new FileSystemSandboxWorkspaceOptions { RootPath = Path.Combine(_root, "workspaces") });
        var admissions = new FairShareSandboxAdmissionController(
            new SandboxAdmissionControllerOptions
            {
                PoolCapacities = new Dictionary<string, int> { ["dedicated-tenant-a"] = 1 }
            });
        var policies = new SandboxWorkloadPolicyResolver(new SandboxWorkloadPolicyCatalog
        {
            Profiles = new Dictionary<string, SandboxExecutionProfile>
            {
                ["dedicated"] = new()
                {
                    PoolId = "dedicated-tenant-a",
                    IsolationTier = SandboxIsolationTier.Dedicated,
                    Limits = new SandboxResourceLimits
                    {
                        MaxDuration = TimeSpan.FromMinutes(5),
                        MaxMemoryBytes = 1024,
                        MaxScratchBytes = 1024,
                        MaxProcesses = 2,
                        MaxCpuCores = 1
                    }
                }
            },
            Tenants = new Dictionary<string, SandboxTenantAdmissionPolicy>
            {
                ["tenant-a"] = new()
                {
                    DefaultProfile = "dedicated",
                    AllowedProfiles = ["dedicated"],
                    Weight = 1,
                    MaxConcurrentAttempts = 1,
                    MaxQueuedAttempts = 2
                }
            }
        });
        return new SandboxScheduledJobExecutor(
            new SandboxScheduledJobExecutorOptions
            {
                PolicyVersion = "policy-v1",
                BindingVersion = "binding-v1"
            },
            new SandboxTenantContextResolver(hostTenant),
            policies,
            new FileSystemImmutableSandboxArtifactStore(
                new ImmutableSandboxArtifactStoreOptions { RootPath = Path.Combine(_root, "artifacts") }),
            new SandboxExecutionCoordinator(workspace, admissions),
            provider);
    }

    private static JobDefinition Job(string tenant) => new(
        "job-a", "PRINT 'sandbox';", 1, "HOUR", null, null, null,
        Options: "{\"SandboxProfile\":\"dedicated\"}", TenantId: tenant);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class CapturingProvider(ScriptExecutionResult result) : ISandboxExecutionProvider
    {
        public SandboxWorkloadRequest? Request { get; private set; }
        public string WorkspaceRoot { get; private set; } = string.Empty;

        public Task<ISandboxAttempt> PrepareAsync(
            SandboxWorkloadRequest request,
            SandboxWorkspaceAssignment workspace,
            CancellationToken cancellationToken)
        {
            Request = request;
            WorkspaceRoot = workspace.RootPath;
            return Task.FromResult<ISandboxAttempt>(new Attempt(result));
        }

        private sealed class Attempt(ScriptExecutionResult result) : ISandboxAttempt
        {
            public SandboxProviderEvidence Evidence { get; } = new(
                "fake", "1", "runsc", SandboxIsolationTier.Dedicated,
                "sha256:" + new string('a', 64), "host-policy-v1");

            public Task<SandboxExecutionOutcome> RunAsync(CancellationToken cancellationToken) =>
                Task.FromResult(new SandboxExecutionOutcome(
                    SandboxTerminalStatus.Succeeded, 0, null, result));

            public Task DestroyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
