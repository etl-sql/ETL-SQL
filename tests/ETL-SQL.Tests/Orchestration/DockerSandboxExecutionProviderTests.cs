using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class DockerSandboxExecutionProviderTests : IDisposable
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"etlsql-docker-sandbox-{Guid.NewGuid():N}");

    [Fact]
    public async Task PrepareBuildsStoppedDigestPinnedHardenedContainerAndRunDestroysIt()
    {
        var commands = new RecordingCommands(
            ImageEvidence(), Ok("29.6.2"), RuntimeEvidence(), Ok("container-id"), Ok("created"),
            Ok("{\"success\":true,\"rowsProcessed\":7,\"peakMemoryBytes\":4096," +
               "\"cpuTimeSeconds\":1.25,\"sessionId\":\"session-1\"}"),
            Ok("container-id"), Fail("No such object: container"));
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync("PRINT 'safe';");
        var workspace = await Workspace().AssignAsync(Identity());
        var provider = new DockerSandboxExecutionProvider(Options(), commands, artifacts);

        var attempt = await provider.PrepareAsync(
            Request(artifact) with { AdmissionId = "admission-1" }, workspace, default);
        var outcome = await attempt.RunAsync(default);
        await attempt.DestroyAsync(default);

        Assert.Equal(SandboxTerminalStatus.Succeeded, outcome.Status);
        Assert.Equal(7, outcome.Result!.RowsProcessed);
        Assert.Equal(4096, outcome.Result.PeakMemoryBytes);
        Assert.Equal(1.25, outcome.Result.CpuTimeSeconds);
        var create = commands.Calls.Single(call => call.Arguments.FirstOrDefault() == "create").Arguments;
        AssertContainsPair(create, "--runtime", "runsc");
        AssertContainsPair(create, "--network", "none");
        AssertContainsPair(create, "--cap-drop", "ALL");
        AssertContainsPair(create, "--security-opt", "no-new-privileges");
        AssertContainsPair(create, "--memory", "536870912");
        AssertContainsPair(create, "--memory-swap", "536870912");
        Assert.Contains("--read-only", create);
        Assert.DoesNotContain("--privileged", create);
        Assert.Contains($"{DockerSandboxExecutionProvider.AdmissionLabel}=admission-1", create);
        Assert.Contains("Session__Root=/var/lib/etl-sql/sessions", create);
        Assert.Contains("ETLSQL_MACHINE_KEY_FILE=/run/secrets/etlsql-machine-key", create);
        Assert.Contains(create, argument => argument.Contains("tenant-a.key", StringComparison.Ordinal));
        Assert.Contains("/workspace/input/job.etlsql", create);
        Assert.True(File.GetAttributes(Path.Combine(workspace.InputPath, "job.etlsql"))
            .HasFlag(FileAttributes.ReadOnly));

        File.SetAttributes(Path.Combine(workspace.InputPath, "job.etlsql"), FileAttributes.Normal);
        await workspace.DestroyAsync();
    }

    [Fact]
    public async Task PrepareStandardModeBuildsStandardContainerWithLocalImageId()
    {
        var commands = new RecordingCommands(
            Ok(Digest), Ok("29.6.2"), RuntimeEvidence(), Ok("container-id"), Ok("created"),
            Ok("{\"success\":true,\"rowsProcessed\":7,\"peakMemoryBytes\":4096," +
               "\"cpuTimeSeconds\":1.25,\"sessionId\":\"session-1\"}"),
            Ok("container-id"), Fail("No such object: container"));
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync("PRINT 'safe';");
        var workspace = await Workspace().AssignAsync(Identity());
        var options = new DockerSandboxExecutionOptions
        {
            Mode = DockerSandboxMode.Standard,
            Image = "local-image",
            LocalImageId = Digest,
            Runtime = "runc",
            HostPolicyVersion = "host-policy-v1",
            SessionRoot = Path.Combine(_root, "sessions"),
            MachineKeyRoot = ProvisionMachineKeyRoot(),
        };
        var provider = new DockerSandboxExecutionProvider(options, commands, artifacts);

        var attempt = await provider.PrepareAsync(
            Request(artifact, requiredIsolation: SandboxIsolationTier.Standard) with { AdmissionId = "admission-1" }, workspace, default);
        var outcome = await attempt.RunAsync(default);
        await attempt.DestroyAsync(default);

        Assert.Equal(SandboxTerminalStatus.Succeeded, outcome.Status);
        var create = commands.Calls.Single(call => call.Arguments.FirstOrDefault() == "create").Arguments;
        AssertContainsPair(create, "--runtime", "runc");
        Assert.Contains("local-image", create);
    }

    [Fact]
    public async Task DedicatedWorkerRefusesForeignTenantBeforeRuntimeInvocation()
    {
        var commands = new RecordingCommands();
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync("PRINT 'safe';");
        var workspace = await Workspace().AssignAsync(new SandboxAssignmentIdentity(
            TenantContext.FromVerifiedCredential("tenant-b"), "run-1", "attempt-1"));
        var provider = new DockerSandboxExecutionProvider(Options(), commands, artifacts);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.PrepareAsync(
            Request(artifact, "tenant-b") with { AdmissionId = "admission-2" }, workspace, default));

        Assert.Empty(commands.Calls);
        await workspace.DestroyAsync();
    }

    [Fact]
    public async Task ProviderRefusesValuesThatWouldBecomeProcessArguments()
    {
        var commands = new RecordingCommands();
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync("PRINT 'safe';");
        var workspace = await Workspace().AssignAsync(Identity());
        var provider = new DockerSandboxExecutionProvider(Options(), commands, artifacts);
        var request = Request(artifact) with
        {
            AdmissionId = "admission-values",
            VariableOverrides = new Dictionary<string, string> { ["password"] = "secret" }
        };

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.PrepareAsync(request, workspace, default));

        Assert.Empty(commands.Calls);
        await workspace.DestroyAsync();
    }

    [Fact]
    public async Task PrepareFailureAttemptsContainerRemovalAndNeverRunsTenantCode()
    {
        var commands = new RecordingCommands(
            ImageEvidence(), Ok("29.6.2"), RuntimeEvidence(), Fail("runtime rejected create"),
            Ok("removed"), Fail("No such object: container"));
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync("PRINT 'safe';");
        var workspace = await Workspace().AssignAsync(Identity());
        var provider = new DockerSandboxExecutionProvider(Options(), commands, artifacts);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.PrepareAsync(
            Request(artifact) with { AdmissionId = "admission-3" }, workspace, default));

        Assert.Contains(commands.Calls, call => call.Arguments.SequenceEqual(
            ["rm", "--force", "--volumes", $"etlsql-{workspace.AssignmentId}"]));
        Assert.DoesNotContain(commands.Calls, call => call.Arguments.Contains("start"));
        File.SetAttributes(Path.Combine(workspace.InputPath, "job.etlsql"), FileAttributes.Normal);
        await workspace.DestroyAsync();
    }

    [Fact]
    public async Task ImmutableArtifactTamperIsDetectedBeforeStaging()
    {
        var store = ArtifactStore();
        var artifact = await store.PutScriptAsync("PRINT 'original';");
        File.SetAttributes(artifact.Path, FileAttributes.Normal);
        await File.WriteAllTextAsync(artifact.Path, "PRINT 'tampered';");
        var destination = Path.Combine(_root, "stage", "job.etlsql");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.StageAsync(artifact.ArtifactId, artifact.Hash, destination));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ReconcilerReleasesOnlyAbsentOrProvenRemovedRuntime()
    {
        var admission = Admission("admission-4");
        var absent = new DockerSandboxRuntimeReconciler(Options(), new RecordingCommands(Ok("")));
        Assert.Equal(SandboxRuntimeReconciliationState.Detached,
            await absent.ProbeAsync(admission, default));

        var running = new DockerSandboxRuntimeReconciler(
            Options(), new RecordingCommands(Ok("abc|running")));
        Assert.Equal(SandboxRuntimeReconciliationState.Running,
            await running.ProbeAsync(admission, default));

        var stoppedCommands = new RecordingCommands(
            Ok("abc|exited"), Ok("abc"), Fail("No such object: abc"));
        var stopped = new DockerSandboxRuntimeReconciler(Options(), stoppedCommands);
        Assert.Equal(SandboxRuntimeReconciliationState.Detached,
            await stopped.ProbeAsync(admission, default));
    }

    [Theory]
    [InlineData("runsc")]
    [InlineData("kata")]
    public void StandardContainerRuntimeCannotClaimHardenedEvidence(string runtime)
    {
        Assert.Throws<ArgumentException>(() => new DockerSandboxExecutionProvider(
            Options().WithMode(DockerSandboxMode.Standard).WithImageDigest(Digest).WithRuntime(runtime), new RecordingCommands(), ArtifactStore()));
    }

    [Theory]
    [InlineData("runc")]
    [InlineData("crun")]
    [InlineData("windows")]
    [InlineData("runsc-malicious")]
    public void OrdinaryContainerRuntimeCannotClaimHardenedEvidence(string runtime)
    {
        Assert.Throws<ArgumentException>(() => new DockerSandboxExecutionProvider(
            Options().WithRuntime(runtime), new RecordingCommands(), ArtifactStore()));
    }

    private FileSystemImmutableSandboxArtifactStore ArtifactStore() => new(
        new ImmutableSandboxArtifactStoreOptions { RootPath = Path.Combine(_root, "artifacts") });

    private FileSystemSandboxWorkspaceProvider Workspace() => new(
        new FileSystemSandboxWorkspaceOptions { RootPath = Path.Combine(_root, "workspaces") });

    private static SandboxAssignmentIdentity Identity() => new(
        TenantContext.FromVerifiedCredential("tenant-a"), "run-1", "attempt-1");

    private DockerSandboxExecutionOptions Options() => new()
    {
        Image = "registry.example/etl-sql@" + Digest,
        ImageDigest = Digest,
        Runtime = "runsc",
        HostPolicyVersion = "host-policy-v1",
        SessionRoot = Path.Combine(_root, "sessions"),
        MachineKeyRoot = ProvisionMachineKeyRoot(),
        DedicatedTenantId = "tenant-a",
        DedicatedPoolId = "dedicated-tenant-a"
    };

    private string ProvisionMachineKeyRoot()
    {
        var root = Path.Combine(_root, "keys");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "tenant-a.key");
        if (!File.Exists(path)) File.WriteAllText(path, new string('k', 64));
        return root;
    }

    private static SandboxWorkloadRequest Request(
        ImmutableSandboxArtifact artifact,
        string tenant = "tenant-a",
        SandboxIsolationTier requiredIsolation = SandboxIsolationTier.Dedicated) => new()
        {
            Assignment = new SandboxAssignmentIdentity(
            TenantContext.FromVerifiedCredential(tenant), "run-1", "attempt-1"),
            ArtifactId = artifact.ArtifactId,
            ArtifactHash = artifact.Hash,
            PolicyVersion = "policy-v1",
            BindingVersion = "binding-v1",
            RequiredIsolationTier = requiredIsolation,
            Limits = new SandboxResourceLimits
            {
                MaxDuration = TimeSpan.FromMinutes(5),
                MaxMemoryBytes = 512 * 1024 * 1024,
                MaxScratchBytes = 1024 * 1024,
                MaxProcesses = 32
            },
            AdmissionPolicy = new ResolvedSandboxAdmissionPolicy
            {
                PoolId = tenant == "tenant-a" ? "dedicated-tenant-a" : "dedicated-tenant-b",
                TenantWeight = 1,
                MaxConcurrentAttempts = 1,
                MaxQueuedAttempts = 2
            },
            SessionId = "session-1"
        };

    private static SandboxAdmissionLedgerEntry Admission(string id) => new(
        1, id, "tenant-a", "dedicated-tenant-a", 1, 1, 2,
        SandboxAdmissionState.Retained, "node", DateTimeOffset.UtcNow, 2,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "retained");

    private static SandboxCommandResult Ok(string output) => new(0, output, "");
    private static SandboxCommandResult Fail(string error) => new(1, "", error);
    private static SandboxCommandResult ImageEvidence() =>
        Ok("[\"registry.example/etl-sql@" + Digest + "\"]");
    private static SandboxCommandResult RuntimeEvidence() =>
        Ok("{\"runsc\":{\"path\":\"runsc\"},\"runc\":{\"path\":\"runc\"}}");

    private static void AssertContainsPair(IReadOnlyList<string> values, string first, string second)
    {
        var index = Array.IndexOf(values.ToArray(), first);
        Assert.True(index >= 0 && index + 1 < values.Count);
        Assert.Equal(second, values[index + 1]);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingCommands(params SandboxCommandResult[] results) : ISandboxCommandRunner
    {
        private readonly Queue<SandboxCommandResult> _results = new(results);
        public List<(string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<SandboxCommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((executable, arguments.ToArray()));
            if (_results.Count == 0) throw new InvalidOperationException("Unexpected command.");
            return Task.FromResult(_results.Dequeue());
        }
    }
}

internal static class DockerSandboxOptionsTestExtensions
{
    public static DockerSandboxExecutionOptions WithRuntime(
        this DockerSandboxExecutionOptions source,
        string runtime) => new()
        {
            Mode = source.Mode,
            DockerExecutable = source.DockerExecutable,
            Image = source.Image,
            ImageDigest = source.ImageDigest,
            LocalImageId = source.LocalImageId,
            Runtime = runtime,
            HostPolicyVersion = source.HostPolicyVersion,
            SessionRoot = source.SessionRoot,
            MachineKeyRoot = source.MachineKeyRoot,
            Entrypoint = source.Entrypoint,
            User = source.User,
            DedicatedTenantId = source.DedicatedTenantId,
            DedicatedPoolId = source.DedicatedPoolId
        };

    public static DockerSandboxExecutionOptions WithMode(
        this DockerSandboxExecutionOptions source,
        DockerSandboxMode mode) => new()
        {
            Mode = mode,
            DockerExecutable = source.DockerExecutable,
            Image = source.Image,
            ImageDigest = source.ImageDigest,
            LocalImageId = source.LocalImageId,
            Runtime = source.Runtime,
            HostPolicyVersion = source.HostPolicyVersion,
            SessionRoot = source.SessionRoot,
            MachineKeyRoot = source.MachineKeyRoot,
            Entrypoint = source.Entrypoint,
            User = source.User,
            DedicatedTenantId = source.DedicatedTenantId,
            DedicatedPoolId = source.DedicatedPoolId
        };

    public static DockerSandboxExecutionOptions WithImageDigest(
        this DockerSandboxExecutionOptions source,
        string? digest) => new()
        {
            Mode = source.Mode,
            DockerExecutable = source.DockerExecutable,
            Image = source.Image,
            ImageDigest = digest,
            LocalImageId = source.LocalImageId,
            Runtime = source.Runtime,
            HostPolicyVersion = source.HostPolicyVersion,
            SessionRoot = source.SessionRoot,
            MachineKeyRoot = source.MachineKeyRoot,
            Entrypoint = source.Entrypoint,
            User = source.User,
            DedicatedTenantId = source.DedicatedTenantId,
            DedicatedPoolId = source.DedicatedPoolId
        };
}
