using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class SandboxExecutionCoordinatorTests
{
    [Theory]
    [InlineData(SandboxTerminalStatus.Succeeded)]
    [InlineData(SandboxTerminalStatus.Failed)]
    [InlineData(SandboxTerminalStatus.Ambiguous)]
    public async Task TerminalOutcomeDestroysRuntimeBeforeWorkspace(SandboxTerminalStatus status)
    {
        using var temp = new TempDirectory();
        var workspaceProvider = CreateWorkspaceProvider(temp.Root);
        var coordinator = CreateCoordinator(workspaceProvider);
        var provider = new FakeProvider(status);

        var outcome = await coordinator.ExecuteAsync(provider, Request());

        Assert.Equal(status, outcome.Status);
        Assert.True(provider.Attempt!.Destroyed);
        Assert.False(Directory.Exists(provider.WorkspaceRoot));
    }

    [Fact]
    public async Task CallerCancellationStillDestroysRuntimeAndWorkspace()
    {
        using var temp = new TempDirectory();
        var coordinator = CreateCoordinator(CreateWorkspaceProvider(temp.Root));
        var provider = new FakeProvider(SandboxTerminalStatus.Succeeded, waitForCancellation: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.ExecuteAsync(provider, Request(), cancellation.Token));

        // Cancellation before allocation is intentionally side-effect free.
        Assert.Null(provider.Attempt);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(temp.Root, "assignments")));
    }

    [Fact]
    public async Task CancellationAfterStartStillDestroysRuntimeAndWorkspace()
    {
        using var temp = new TempDirectory();
        var coordinator = CreateCoordinator(CreateWorkspaceProvider(temp.Root));
        using var cancellation = new CancellationTokenSource();
        var provider = new FakeProvider(
            SandboxTerminalStatus.Succeeded,
            waitForCancellation: true,
            onPrepared: cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.ExecuteAsync(provider, Request(), cancellation.Token));

        Assert.True(provider.Attempt!.Destroyed);
        Assert.False(Directory.Exists(provider.WorkspaceRoot));
    }

    [Fact]
    public async Task InsufficientIsolationFailsClosedAfterDestroyingAttempt()
    {
        using var temp = new TempDirectory();
        var coordinator = CreateCoordinator(CreateWorkspaceProvider(temp.Root));
        var provider = new FakeProvider(
            SandboxTerminalStatus.Succeeded,
            evidenceTier: SandboxIsolationTier.Standard);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            coordinator.ExecuteAsync(provider, Request()));

        Assert.False(provider.Attempt!.RunCalled);
        Assert.True(provider.Attempt.Destroyed);
        Assert.False(Directory.Exists(provider.WorkspaceRoot));
    }

    [Fact]
    public async Task TransactionalPrepareFailureRemovesUnclaimedWorkspace()
    {
        using var temp = new TempDirectory();
        var coordinator = CreateCoordinator(CreateWorkspaceProvider(temp.Root));
        var provider = new FakeProvider(SandboxTerminalStatus.Succeeded, prepareFailure: new IOException("prepare failed"));

        await Assert.ThrowsAsync<IOException>(() => coordinator.ExecuteAsync(provider, Request()));

        Assert.False(Directory.Exists(provider.WorkspaceRoot));
    }

    [Fact]
    public async Task UncertainPrepareCleanupRetainsWorkspaceAndAdmission()
    {
        using var temp = new TempDirectory();
        var admissions = CreateAdmissionController();
        var coordinator = new SandboxExecutionCoordinator(CreateWorkspaceProvider(temp.Root), admissions);
        var provider = new FakeProvider(
            SandboxTerminalStatus.Failed,
            prepareFailure: new SandboxPrepareTeardownException(
                new IOException("create failed"), new IOException("remove unproven"), "provider-id"));

        var error = await Assert.ThrowsAsync<SandboxPrepareTeardownException>(() =>
            coordinator.ExecuteAsync(provider, Request()));

        Assert.Contains("retained", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(provider.WorkspaceRoot));
        var blocked = admissions.AcquireAsync(
            TenantContext.FromVerifiedCredential("tenant-a"), AdmissionPolicy()).AsTask();
        Assert.False(blocked.IsCompleted);
        // The coordinator's durable admission id, rather than the provider's test placeholder, is
        // the authority retained by the controller.
        var retainedId = GetOnlyAssignmentAdmission(admissions);
        Assert.True(await admissions.ReleaseReconciledAsync(retainedId));
        var released = await blocked;
        await released.ReleaseAsync();
    }

    [Fact]
    public async Task UnprovenRuntimeTeardownRetainsWorkspaceForReconciliation()
    {
        using var temp = new TempDirectory();
        var admissions = CreateAdmissionController();
        var coordinator = new SandboxExecutionCoordinator(CreateWorkspaceProvider(temp.Root), admissions);
        var provider = new FakeProvider(
            SandboxTerminalStatus.Ambiguous,
            destroyFailure: new IOException("runtime detach unconfirmed"));

        var error = await Assert.ThrowsAsync<SandboxTeardownException>(() =>
            coordinator.ExecuteAsync(provider, Request()));

        Assert.Contains("retained", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(error.AdmissionId));
        Assert.True(Directory.Exists(provider.WorkspaceRoot));

        var blocked = admissions.AcquireAsync(
            TenantContext.FromVerifiedCredential("tenant-a"), AdmissionPolicy()).AsTask();
        Assert.False(blocked.IsCompleted);
        Assert.True(await admissions.ReleaseReconciledAsync(error.AdmissionId));
        var reconciled = await blocked;
        await reconciled.ReleaseAsync();
    }

    [Fact]
    public async Task ExecutionFailureIsRethrownOnlyAfterCleanup()
    {
        using var temp = new TempDirectory();
        var coordinator = CreateCoordinator(CreateWorkspaceProvider(temp.Root));
        var provider = new FakeProvider(
            SandboxTerminalStatus.Failed,
            waitFailure: new InvalidOperationException("provider protocol failed"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(provider, Request()));

        Assert.Equal("provider protocol failed", error.Message);
        Assert.True(provider.Attempt!.Destroyed);
        Assert.False(Directory.Exists(provider.WorkspaceRoot));
    }

    private static SandboxWorkloadRequest Request() => new()
    {
        Assignment = new SandboxAssignmentIdentity(
            TenantContext.FromVerifiedCredential("tenant-a"), "run-1", "attempt-1"),
        ArtifactId = "artifact-1",
        ArtifactHash = "sha256:" + new string('a', 64),
        PolicyVersion = "policy-v1",
        BindingVersion = "binding-v1",
        RequiredIsolationTier = SandboxIsolationTier.Hardened,
        AdmissionPolicy = AdmissionPolicy(),
        Limits = new SandboxResourceLimits
        {
            MaxDuration = TimeSpan.FromMinutes(5),
            MaxMemoryBytes = 512 * 1024 * 1024,
            MaxScratchBytes = 1024 * 1024 * 1024,
            MaxProcesses = 32
        }
    };

    private static FileSystemSandboxWorkspaceProvider CreateWorkspaceProvider(string root) =>
        new(new FileSystemSandboxWorkspaceOptions { RootPath = Path.Combine(root, "assignments") });

    private static SandboxExecutionCoordinator CreateCoordinator(ISandboxWorkspaceProvider workspaces) =>
        new(workspaces, CreateAdmissionController());

    private static FairShareSandboxAdmissionController CreateAdmissionController() =>
        new(new SandboxAdmissionControllerOptions
        {
            PoolCapacities = new Dictionary<string, int> { ["shared-hardened"] = 1 }
        });

    private static ResolvedSandboxAdmissionPolicy AdmissionPolicy() => new()
    {
        PoolId = "shared-hardened",
        TenantWeight = 1,
        MaxConcurrentAttempts = 1,
        MaxQueuedAttempts = 8
    };

    private static string GetOnlyAssignmentAdmission(FairShareSandboxAdmissionController admissions)
    {
        var field = typeof(FairShareSandboxAdmissionController).GetField(
            "_activeLeases", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var value = (System.Collections.IDictionary)field.GetValue(admissions)!;
        return Assert.Single(value.Keys.Cast<string>());
    }

    private sealed class FakeProvider(
        SandboxTerminalStatus status,
        SandboxIsolationTier evidenceTier = SandboxIsolationTier.Hardened,
        bool waitForCancellation = false,
        Action? onPrepared = null,
        Exception? prepareFailure = null,
        Exception? waitFailure = null,
        Exception? destroyFailure = null) : ISandboxExecutionProvider
    {
        public FakeAttempt? Attempt { get; private set; }
        public string WorkspaceRoot { get; private set; } = string.Empty;

        public Task<ISandboxAttempt> PrepareAsync(
            SandboxWorkloadRequest request,
            SandboxWorkspaceAssignment workspace,
            CancellationToken cancellationToken)
        {
            WorkspaceRoot = workspace.RootPath;
            if (prepareFailure is not null)
                throw prepareFailure;

            Attempt = new FakeAttempt(status, evidenceTier, waitForCancellation, waitFailure, destroyFailure);
            onPrepared?.Invoke();
            return Task.FromResult<ISandboxAttempt>(Attempt);
        }
    }

    private sealed class FakeAttempt(
        SandboxTerminalStatus status,
        SandboxIsolationTier evidenceTier,
        bool waitForCancellation,
        Exception? waitFailure,
        Exception? destroyFailure) : ISandboxAttempt
    {
        public SandboxProviderEvidence Evidence { get; } = new(
            "fake", "1.0", "fake-runtime", evidenceTier, "sha256:image", "host-policy-v1");
        public bool RunCalled { get; private set; }
        public bool Destroyed { get; private set; }

        public async Task<SandboxExecutionOutcome> RunAsync(CancellationToken cancellationToken)
        {
            RunCalled = true;
            if (waitFailure is not null)
                throw waitFailure;
            if (waitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new SandboxExecutionOutcome(status);
        }

        public Task DestroyAsync(CancellationToken cancellationToken)
        {
            if (destroyFailure is not null)
                throw destroyFailure;
            Destroyed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), $"etlsql-sandbox-coordinator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
