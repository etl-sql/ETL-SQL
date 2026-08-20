using System.Text.Json;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// The real-provider sandbox lifecycle contract, run against a live OCI runtime and a real ETL-SQL
/// workload. Subclasses bind it to one isolation tier so the tier that produced a given result is
/// unmistakable from the test name; the assertions themselves are identical, because the contract is.
/// </summary>
public abstract class DockerSandboxLifecycleTestsBase : IAsyncLifetime
{
    protected const string HarmlessScript = """
        CREATE CONNECTION SandboxOut AS FLATFILE(PATH = '/workspace/output/result.csv', DELIMITER = ',', HEADER = ON);
        CREATE TABLE #Probe (Id INT, Note VARCHAR(50));
        INSERT INTO #Probe VALUES (1, 'sandbox-ok');
        INSERT INTO SandboxOut SELECT * FROM #Probe;
        PRINT 'safe';
        """;

    // An idle wait, not a busy loop: the workload must still be running when the test cancels or
    // kills it, without the outcome depending on how fast this host happens to be.
    protected const string LongRunningScript = """
        WAITFOR DELAY '00:05:00';
        PRINT 'this script is not expected to finish';
        """;

    // Reaches a top-level checkpoint label — which is what persists evaluator state — and then waits
    // to be killed, so the attempt contributes a durable checkpoint and nothing else.
    protected const string CheckpointThenWaitScript = """
        DECLARE @Stage VARCHAR(50);
        SET @Stage = 'checkpointed-by-the-first-sandbox';
        Checkpoint1:
        WAITFOR DELAY '00:05:00';
        PRINT 'this script is not expected to finish';
        """;

    // Carries the checkpoint label (resume requires it) but never assigns @Stage. Any value it can
    // write therefore came from the other sandbox's checkpoint, not from re-running the first half.
    protected const string ResumeFromCheckpointScript = """
        DECLARE @Stage VARCHAR(50);
        Checkpoint1:
        CREATE CONNECTION SandboxOut AS FLATFILE(PATH = '/workspace/output/resumed.csv', DELIMITER = ',', HEADER = ON);
        CREATE TABLE #Resumed (Stage VARCHAR(50));
        INSERT INTO #Resumed VALUES (@Stage);
        INSERT INTO SandboxOut SELECT * FROM #Resumed;
        PRINT 'safe';
        """;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"etlsql-sandbox-live-{Guid.NewGuid():N}");
    private readonly ProcessSandboxCommandRunner _docker = new();
    private readonly List<string> _containers = [];

    /// <summary>The tier the request demands and the provider evidence must satisfy.</summary>
    protected abstract SandboxIsolationTier Tier { get; }

    /// <summary>The runtime this lane asserts the workload actually ran on.</summary>
    protected abstract string ExpectedRuntime { get; }

    /// <summary>The image identity the provider is pinned to: a local ID or a registry digest.</summary>
    protected abstract string ExpectedImageIdentity { get; }

    protected abstract DockerSandboxExecutionOptions Options();

    protected async Task VerifyRuntimeRunsTenantCodeUnderItsOwnAssignmentMountsOnly()
    {
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(HarmlessScript);
        var workspace = await Workspaces().AssignAsync(Identity("tenant-a"));
        var provider = new DockerSandboxExecutionProvider(Options(), _docker, artifacts);

        var attempt = await provider.PrepareAsync(
            Request(artifact, "tenant-a") with { AdmissionId = "live-lifecycle-1" }, workspace, default);
        var container = TrackContainer(workspace.AssignmentId);

        // Evidence is what the environment can prove, not what the request asked for.
        Assert.Equal("docker-oci", attempt.Evidence.Provider);
        Assert.Equal(Tier, attempt.Evidence.IsolationTier);
        Assert.Equal(ExpectedRuntime, attempt.Evidence.Runtime);
        Assert.Equal(ExpectedImageIdentity, attempt.Evidence.ImageDigest);

        var inspected = await InspectAsync(container);
        var host = inspected.GetProperty("HostConfig");
        Assert.Equal(ExpectedRuntime, host.GetProperty("Runtime").GetString());
        Assert.True(host.GetProperty("ReadonlyRootfs").GetBoolean());
        Assert.False(host.GetProperty("Privileged").GetBoolean());
        Assert.Equal("none", host.GetProperty("NetworkMode").GetString());
        Assert.Contains("ALL", host.GetProperty("CapDrop").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("no-new-privileges", string.Join(
            ',', host.GetProperty("SecurityOpt").EnumerateArray().Select(value => value.GetString())));
        Assert.Equal("65532:65532", inspected.GetProperty("Config").GetProperty("User").GetString());

        // Containment ceilings are only real if the runtime actually received them. NanoCpus is
        // Docker's own encoding of --cpus, so this is the host's view rather than the request's.
        Assert.Equal(512L * 1024 * 1024, host.GetProperty("Memory").GetInt64());
        Assert.Equal(512L * 1024 * 1024, host.GetProperty("MemorySwap").GetInt64());
        Assert.Equal(2_000_000_000L, host.GetProperty("NanoCpus").GetInt64());
        Assert.Equal(64L, host.GetProperty("PidsLimit").GetInt64());

        // Nothing reusable is mounted: no named or anonymous volume, and every bind source belongs to
        // this assignment or to this tenant's server-owned session and key roots.
        var mounts = inspected.GetProperty("Mounts").EnumerateArray().ToArray();
        Assert.All(mounts, mount => Assert.Equal("bind", mount.GetProperty("Type").GetString()));
        Assert.All(mounts, mount => Assert.True(
            string.IsNullOrEmpty(mount.TryGetProperty("Name", out var name) ? name.GetString() : null),
            "A sandbox assignment must not receive a reusable named volume."));
        Assert.Contains(workspace.AssignmentId, MountSource(mounts, "/workspace/input"));
        Assert.Contains(workspace.AssignmentId, MountSource(mounts, "/workspace/output"));
        Assert.False(MountRw(mounts, "/workspace/input"), "Staged tenant input must be mounted read-only.");
        Assert.Contains("tenant-a", MountSource(mounts, "/var/lib/etl-sql/sessions"));
        Assert.Contains("tenant-a", MountSource(mounts, "/run/secrets/etlsql-machine-key"));
        // Scratch is a fresh tmpfs, so it cannot carry state out of the attempt at all.
        Assert.Contains("/workspace/scratch", host.GetProperty("Tmpfs").EnumerateObject().Select(entry => entry.Name));

        var outcome = await attempt.RunAsync(default);

        AssertSucceeded(outcome);
        Assert.Equal(0, outcome.ExitCode);
        Assert.True(
            File.Exists(Path.Combine(workspace.OutputPath, "result.csv")),
            "Tenant code must be able to write through its own output mount.");

        await attempt.DestroyAsync(default);
        Assert.False(await ContainerExistsAsync(container));

        await workspace.DestroyAsync();
        Assert.False(Directory.Exists(workspace.RootPath));
    }

    protected async Task VerifySuccessiveAssignmentsNeverReuseAStorageIdentifierOrSeePriorResidue()
    {
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(HarmlessScript);
        var workspaces = new CapturingWorkspaceProvider(Workspaces());
        var provider = new DockerSandboxExecutionProvider(Options(), _docker, artifacts);
        var coordinator = new SandboxExecutionCoordinator(workspaces, Admissions());

        // The same logical tenant, run, and attempt identifiers twice: only the server-derived
        // assignment identity may distinguish the two roots.
        var first = await coordinator.ExecuteAsync(provider, Request(artifact, "tenant-a"));
        var firstAssignment = workspaces.Assignments[0];
        AssertSucceeded(first);
        Assert.False(Directory.Exists(firstAssignment.RootPath));

        var second = await coordinator.ExecuteAsync(provider, Request(artifact, "tenant-a"));
        var secondAssignment = workspaces.Assignments[1];

        AssertSucceeded(second);
        Assert.NotEqual(firstAssignment.AssignmentId, secondAssignment.AssignmentId);
        Assert.NotEqual(firstAssignment.RootPath, secondAssignment.RootPath);
        Assert.False(Directory.Exists(secondAssignment.RootPath));
        Assert.Empty(await ListContainersAsync(
            $"label={DockerSandboxExecutionProvider.AssignmentLabel}={firstAssignment.AssignmentId}"));
        Assert.Empty(await ListContainersAsync(
            $"label={DockerSandboxExecutionProvider.AssignmentLabel}={secondAssignment.AssignmentId}"));
    }

    protected async Task VerifyDifferentTenantsNeverShareAWorkspaceSessionOrKeyPath()
    {
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(HarmlessScript);
        var provider = new DockerSandboxExecutionProvider(Options(), _docker, artifacts);
        var workspaces = Workspaces();

        var tenantA = await workspaces.AssignAsync(Identity("tenant-a"));
        var attemptA = await provider.PrepareAsync(
            Request(artifact, "tenant-a") with { AdmissionId = "live-tenant-a" }, tenantA, default);
        var containerA = TrackContainer(tenantA.AssignmentId);
        var tenantB = await workspaces.AssignAsync(Identity("tenant-b"));
        var attemptB = await provider.PrepareAsync(
            Request(artifact, "tenant-b") with { AdmissionId = "live-tenant-b" }, tenantB, default);
        var containerB = TrackContainer(tenantB.AssignmentId);

        var mountsA = (await InspectAsync(containerA)).GetProperty("Mounts").EnumerateArray().ToArray();
        var mountsB = (await InspectAsync(containerB)).GetProperty("Mounts").EnumerateArray().ToArray();

        foreach (var destination in new[]
                 {
                     "/workspace/input", "/workspace/output",
                     "/var/lib/etl-sql/sessions", "/run/secrets/etlsql-machine-key"
                 })
        {
            Assert.NotEqual(MountSource(mountsA, destination), MountSource(mountsB, destination));
        }

        // The per-tenant encryption data key is distinct material, not merely a distinct path.
        Assert.NotEqual(
            await File.ReadAllBytesAsync(Path.Combine(_root, "keys", "tenant-a.key")),
            await File.ReadAllBytesAsync(Path.Combine(_root, "keys", "tenant-b.key")));

        await attemptA.DestroyAsync(default);
        await attemptB.DestroyAsync(default);
        await tenantA.DestroyAsync();
        await tenantB.DestroyAsync();
    }

    protected async Task VerifyCancellingTenantCodeRemovesTheRuntimeAndItsWritableState()
    {
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(LongRunningScript);
        var workspaces = new CapturingWorkspaceProvider(Workspaces());
        var provider = new DockerSandboxExecutionProvider(Options(), _docker, artifacts);
        var coordinator = new SandboxExecutionCoordinator(workspaces, Admissions());
        using var cancellation = new CancellationTokenSource();

        var execution = coordinator.ExecuteAsync(provider, Request(artifact, "tenant-a"), cancellation.Token);
        var assignment = await WaitForAssignmentAsync(workspaces);
        var container = TrackContainer(assignment.AssignmentId);
        await WaitForRunningAsync(container);
        await cancellation.CancelAsync();
        var outcome = await execution;

        Assert.Equal(SandboxTerminalStatus.Cancelled, outcome.Status);
        // Cleanup is authority revocation: caller cancellation must not leave the runtime or its
        // writable state behind, even though the workload was killed rather than allowed to exit.
        Assert.False(await ContainerExistsAsync(container));
        Assert.False(Directory.Exists(assignment.RootPath));
    }

    protected async Task VerifyForciblyTerminatedRuntimeIsCleanedUpAndReconciledAsDetached()
    {
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(LongRunningScript);
        var workspace = await Workspaces().AssignAsync(Identity("tenant-a"));
        var provider = new DockerSandboxExecutionProvider(Options(), _docker, artifacts);
        var attempt = await provider.PrepareAsync(
            Request(artifact, "tenant-a") with { AdmissionId = "live-killed" }, workspace, default);
        var container = TrackContainer(workspace.AssignmentId);

        var execution = attempt.RunAsync(default);
        await WaitForRunningAsync(container);
        var kill = await _docker.RunAsync("docker", ["kill", container]);
        Assert.Equal(0, kill.ExitCode);
        var outcome = await execution;

        Assert.Equal(SandboxTerminalStatus.Failed, outcome.Status);
        // 137 is SIGKILL: the workload was terminated mid-run rather than reaching its own exit.
        Assert.Equal(137, outcome.ExitCode);
        await attempt.DestroyAsync(default);
        Assert.False(await ContainerExistsAsync(container));

        var reconciler = new DockerSandboxRuntimeReconciler(Options(), _docker);
        Assert.Equal(
            SandboxRuntimeReconciliationState.Detached,
            await reconciler.ProbeAsync(Admission("live-killed"), default));

        await workspace.DestroyAsync();
        Assert.False(Directory.Exists(workspace.RootPath));
    }

    protected async Task VerifyCheckpointedStateResumesInADifferentSandbox()
    {
        var artifacts = ArtifactStore();
        var provider = new DockerSandboxExecutionProvider(Options(), _docker, artifacts);
        var sessionId = $"resume-{Guid.NewGuid():N}";

        // The first sandbox reaches its checkpoint and is then killed outright. Its workspace is
        // destroyed with it, so only the tenant's server-owned session root survives the attempt.
        var firstArtifact = await artifacts.PutScriptAsync(CheckpointThenWaitScript);
        var firstWorkspace = await Workspaces().AssignAsync(Identity("tenant-a"));
        var firstAttempt = await provider.PrepareAsync(
            Request(firstArtifact, "tenant-a") with
            {
                AdmissionId = "live-checkpoint-1",
                SessionId = sessionId
            },
            firstWorkspace,
            default);
        var firstContainer = TrackContainer(firstWorkspace.AssignmentId);
        var firstRun = firstAttempt.RunAsync(default);
        await WaitForCheckpointAsync(sessionId, firstRun);
        Assert.Equal(0, (await _docker.RunAsync("docker", ["kill", firstContainer])).ExitCode);
        Assert.Equal(137, (await firstRun).ExitCode);
        await firstAttempt.DestroyAsync(default);
        Assert.False(await ContainerExistsAsync(firstContainer));
        await firstWorkspace.DestroyAsync();
        Assert.False(Directory.Exists(firstWorkspace.RootPath));

        // The second sandbox is a different assignment in a different container, sharing only the
        // tenant's session root. Its script never assigns @Stage.
        var secondArtifact = await artifacts.PutScriptAsync(ResumeFromCheckpointScript);
        var secondWorkspace = await Workspaces().AssignAsync(Identity("tenant-a"));
        Assert.NotEqual(firstWorkspace.AssignmentId, secondWorkspace.AssignmentId);
        var secondAttempt = await provider.PrepareAsync(
            Request(secondArtifact, "tenant-a") with
            {
                AdmissionId = "live-checkpoint-2",
                SessionId = sessionId,
                ResumeFromCheckpoint = true
            },
            secondWorkspace,
            default);
        var secondContainer = TrackContainer(secondWorkspace.AssignmentId);
        Assert.NotEqual(firstContainer, secondContainer);
        var outcome = await secondAttempt.RunAsync(default);

        AssertSucceeded(outcome);
        // The value can only have come from the checkpoint the killed sandbox wrote: this attempt
        // never set it, and the workspace that produced it no longer exists.
        Assert.Contains(
            "checkpointed-by-the-first-sandbox",
            await File.ReadAllTextAsync(Path.Combine(secondWorkspace.OutputPath, "resumed.csv")));

        await secondAttempt.DestroyAsync(default);
        Assert.False(await ContainerExistsAsync(secondContainer));
        await secondWorkspace.DestroyAsync();
    }

    /// <summary>
    /// Reserved placement on a host fixed to one tenant: its own tenant's work actually runs there,
    /// and foreign tenant or pool work is refused before any runtime exists. The refusal half is what
    /// distinguishes a reserved host from an ordinary one that merely happens to be running a single
    /// tenant's jobs today.
    /// </summary>
    protected async Task VerifyReservedPlacementRunsOnlyTheHostsOwnTenantAndPool(
        DockerSandboxExecutionOptions? hostOptions = null)
    {
        var options = hostOptions ?? Options();
        Assert.False(
            string.IsNullOrWhiteSpace(options.DedicatedTenantId),
            "This lane must be configured as a tenant-dedicated host.");
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(HarmlessScript);
        var provider = new DockerSandboxExecutionProvider(options, _docker, artifacts);
        var ownTenant = options.DedicatedTenantId!;

        // Positive: the reserved host runs its own tenant's work, on the runtime it claims.
        var workspace = await Workspaces().AssignAsync(Identity(ownTenant));
        var attempt = await provider.PrepareAsync(
            Request(artifact, ownTenant) with { AdmissionId = "live-reserved-1" }, workspace, default);
        var container = TrackContainer(workspace.AssignmentId);
        Assert.Equal(Tier, attempt.Evidence.IsolationTier);
        Assert.Equal(ExpectedRuntime, attempt.Evidence.Runtime);
        var inspected = await InspectAsync(container);
        Assert.Equal(
            ownTenant,
            inspected.GetProperty("Config").GetProperty("Labels")
                .GetProperty(DockerSandboxExecutionProvider.TenantLabel).GetString());
        AssertSucceeded(await attempt.RunAsync(default));
        await attempt.DestroyAsync(default);
        await workspace.DestroyAsync();

        // Negative: another tenant's work is refused, and no runtime is created for it at all.
        var foreignWorkspace = await Workspaces().AssignAsync(Identity("tenant-b"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.PrepareAsync(
            Request(artifact, "tenant-b") with { AdmissionId = "live-reserved-foreign" },
            foreignWorkspace,
            default));
        Assert.Empty(await ListContainersAsync(
            $"label={DockerSandboxExecutionProvider.AssignmentLabel}={foreignWorkspace.AssignmentId}"));
        await foreignWorkspace.DestroyAsync();

        // Negative: its own tenant placed in a different capacity pool is refused too, so a reserved
        // host cannot be borrowed by pointing other capacity at it.
        var foreignPoolWorkspace = await Workspaces().AssignAsync(Identity(ownTenant));
        var foreignPool = Request(artifact, ownTenant);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.PrepareAsync(
            foreignPool with
            {
                AdmissionId = "live-reserved-foreign-pool",
                AdmissionPolicy = foreignPool.AdmissionPolicy with { PoolId = "shared-elsewhere" }
            },
            foreignPoolWorkspace,
            default));
        Assert.Empty(await ListContainersAsync(
            $"label={DockerSandboxExecutionProvider.AssignmentLabel}={foreignPoolWorkspace.AssignmentId}"));
        await foreignPoolWorkspace.DestroyAsync();
    }

    /// <summary>
    /// A granted capability reaches a real container: mounted read-only at its own path, with the
    /// material nowhere in the command, and the workload still runs. Proving the mount on a live
    /// runtime is the part a command-construction test cannot show.
    /// </summary>
    protected async Task VerifyGrantedCapabilityIsMountedReadOnlyOnALiveRuntime()
    {
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(HarmlessScript);
        var workspace = await Workspaces().AssignAsync(Identity("tenant-a"));
        var provider = new DockerSandboxExecutionProvider(
            Options(), _docker, artifacts, new StaticCapabilityResolver("live-capability-material"));

        var attempt = await provider.PrepareAsync(
            Request(artifact, "tenant-a") with
            {
                AdmissionId = "live-capability-1",
                CapabilityHandles = ["warehouse-reader"]
            },
            workspace,
            default);
        var container = TrackContainer(workspace.AssignmentId);

        var mounts = (await InspectAsync(container)).GetProperty("Mounts").EnumerateArray().ToArray();
        Assert.Contains(workspace.AssignmentId, MountSource(mounts, "/run/secrets/capabilities"));
        Assert.False(
            MountRw(mounts, "/run/secrets/capabilities"),
            "A workload may read the capabilities it was granted and must not be able to add to them.");

        AssertSucceeded(await attempt.RunAsync(default));
        await attempt.DestroyAsync(default);
        await workspace.DestroyAsync();
        // The material lived in the assignment, so it is gone with it.
        Assert.False(Directory.Exists(Path.Combine(workspace.RootPath, "capabilities")));
    }

    private sealed class StaticCapabilityResolver(string material) : ISandboxCapabilityResolver
    {
        public Task<string> ResolveAsync(
            SandboxAssignmentIdentity assignment,
            string capabilityHandle,
            CancellationToken cancellationToken) => Task.FromResult(material);
    }

    protected async Task VerifyUnprovenRuntimeDetachmentRetainsWritableStateInsteadOfDeletingIt()
    {
        var artifacts = ArtifactStore();
        var artifact = await artifacts.PutScriptAsync(HarmlessScript);
        var workspaces = new CapturingWorkspaceProvider(Workspaces());
        var provider = new DockerSandboxExecutionProvider(
            Options(), new UnprovableRemovalCommandRunner(_docker), artifacts);
        var coordinator = new SandboxExecutionCoordinator(workspaces, Admissions());

        var teardown = await Assert.ThrowsAsync<SandboxTeardownException>(() =>
            coordinator.ExecuteAsync(provider, Request(artifact, "tenant-a")));
        var assignment = workspaces.Assignments.Single();
        TrackContainer(assignment.AssignmentId);

        Assert.NotNull(teardown.AdmissionId);
        // Deleting a root a possibly-live runtime may still have mounted is worse than retaining it.
        Assert.True(
            Directory.Exists(assignment.RootPath),
            "Writable state must be retained for fenced reconciliation when detachment is unproven.");
    }

    /// <summary>Reports what the sandbox actually said, so a failing run is diagnosable from the log.</summary>
    private static void AssertSucceeded(SandboxExecutionOutcome outcome) =>
        Assert.True(
            outcome.Status == SandboxTerminalStatus.Succeeded,
            $"Sandbox run did not succeed: status={outcome.Status}, exit={outcome.ExitCode}, " +
            $"diagnostic={outcome.SanitizedDiagnostic}, error={outcome.Result?.ErrorMessage}");

    private static string MountSource(IReadOnlyList<JsonElement> mounts, string destination) =>
        mounts.Single(mount => mount.GetProperty("Destination").GetString() == destination)
            .GetProperty("Source").GetString()!;

    private static bool MountRw(IReadOnlyList<JsonElement> mounts, string destination) =>
        mounts.Single(mount => mount.GetProperty("Destination").GetString() == destination)
            .GetProperty("RW").GetBoolean();

    private async Task<JsonElement> InspectAsync(string container)
    {
        var result = await _docker.RunAsync("docker", ["inspect", container]);
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        return document.RootElement[0].Clone();
    }

    private async Task<bool> ContainerExistsAsync(string container) =>
        !DockerSandboxExecutionProvider.IsContainerAbsent(
            await _docker.RunAsync("docker", ["inspect", container]));

    private async Task<string[]> ListContainersAsync(string filter)
    {
        var result = await _docker.RunAsync(
            "docker", ["ps", "--all", "--filter", filter, "--format", "{{.ID}}"]);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task WaitForRunningAsync(string container)
    {
        // flaky-wait-budget-ok: container inspect status polling deadline
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var state = await _docker.RunAsync(
                "docker", ["inspect", "--format", "{{.State.Status}}", container]);
            if (state.ExitCode == 0 && state.StandardOutput.Trim()
                    .Equals("running", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(100);
        }

        Assert.Fail($"The sandbox container '{container}' never reached the running state.");
    }

    /// <summary>
    /// Waits for the checkpoint to be durable on the tenant's session root rather than for a log
    /// line, so the kill below cannot race the write it is supposed to happen after.
    /// </summary>
    private async Task WaitForCheckpointAsync(string sessionId, Task<SandboxExecutionOutcome>? runTask = null)
    {
        // The file appearing is not the checkpoint being finished: killing the sandbox between the
        // store creating its database and finishing the write leaves state the next attempt cannot
        // read, which looks exactly like resume being broken. Wait until the variables table
        // is committed and queryable.
        var metadata = Path.Combine(SessionRoot, "tenant-a", sessionId, "metadata.db");
        // flaky-wait-budget-ok: checkpoint file settling deadline
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (runTask is { IsCompleted: true })
            {
                var earlyOutcome = await runTask;
                Assert.Fail($"The sandbox exited prematurely before checkpointing (exit code {earlyOutcome.ExitCode}): {earlyOutcome.SanitizedDiagnostic}\n{earlyOutcome.Result?.ErrorMessage}");
            }

            if (File.Exists(metadata))
            {
                try
                {
                    await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={metadata};Mode=ReadOnly");
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM variables WHERE name LIKE '%Stage%';";
                    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    if (count > 0)
                    {
                        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(conn);
                        return;
                    }
                }
                catch
                {
                    // Database may be locked or initializing schema; retry next poll
                }
            }

            await Task.Delay(150);
        }

        Assert.Fail($"The sandbox never persisted a settled checkpoint for session '{sessionId}'.");
    }

    private static async Task<SandboxWorkspaceAssignment> WaitForAssignmentAsync(
        CapturingWorkspaceProvider workspaces)
    {
        // flaky-wait-budget-ok: workspace allocation deadline
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (workspaces.Assignments.Count > 0) return workspaces.Assignments[0];
            await Task.Delay(50);
        }

        throw new TimeoutException("The coordinator never allocated a sandbox workspace assignment.");
    }

    private string TrackContainer(string assignmentId)
    {
        var container = $"etlsql-{assignmentId}";
        _containers.Add(container);
        return container;
    }

    private FileSystemImmutableSandboxArtifactStore ArtifactStore() => new(
        new ImmutableSandboxArtifactStoreOptions { RootPath = Path.Combine(_root, "artifacts") });

    private FileSystemSandboxWorkspaceProvider Workspaces() => new(
        new FileSystemSandboxWorkspaceOptions { RootPath = Path.Combine(_root, "workspaces") });

    /// <summary>The single capacity pool these lanes place work in.</summary>
    protected const string ReservedPoolId = "sandbox-pool";

    private static ISandboxAdmissionController Admissions() => new FairShareSandboxAdmissionController(
        new SandboxAdmissionControllerOptions
        {
            PoolCapacities = new Dictionary<string, int>(StringComparer.Ordinal) { [ReservedPoolId] = 4 }
        });

    private static SandboxAssignmentIdentity Identity(string tenant) => new(
        TenantContext.FromVerifiedCredential(tenant), "run-1", "attempt-1");

    protected string SessionRoot => Path.Combine(_root, "sessions");

    protected string ProvisionMachineKeyRoot()
    {
        var root = Path.Combine(_root, "keys");
        Directory.CreateDirectory(root);
        foreach (var tenant in new[] { "tenant-a", "tenant-b" })
        {
            var path = Path.Combine(root, $"{tenant}.key");
            if (!File.Exists(path))
                File.WriteAllBytes(path, System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        }

        return root;
    }

    private SandboxWorkloadRequest Request(ImmutableSandboxArtifact artifact, string tenant) => new()
    {
        Assignment = Identity(tenant),
        ArtifactId = artifact.ArtifactId,
        ArtifactHash = artifact.Hash,
        PolicyVersion = "policy-v1",
        BindingVersion = "binding-v1",
        RequiredIsolationTier = Tier,
        Limits = new SandboxResourceLimits
        {
            MaxDuration = TimeSpan.FromMinutes(5),
            MaxMemoryBytes = 512 * 1024 * 1024,
            MaxScratchBytes = 32 * 1024 * 1024,
            MaxProcesses = 64,
            MaxCpuCores = 2,
            MaxConnectorConcurrency = 8
        },
        AdmissionPolicy = new ResolvedSandboxAdmissionPolicy
        {
            PoolId = ReservedPoolId,
            TenantWeight = 1,
            MaxConcurrentAttempts = 2,
            MaxQueuedAttempts = 2
        }
    };

    private static ETL_SQL.Orchestrator.Storage.SandboxAdmissionLedgerEntry Admission(string id) => new(
        1, id, "tenant-a", "sandbox-pool", 1, 1, 2,
        ETL_SQL.Orchestrator.Storage.SandboxAdmissionState.Retained, "node", DateTimeOffset.UtcNow, 2,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "retained");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var container in _containers)
        {
            try
            {
                await _docker.RunAsync("docker", ["rm", "--force", "--volumes", container]);
            }
            catch
            {
                // A leftover container is reported by the assertions that care about it; teardown of
                // the temporary host root must still run.
            }
        }

        if (!Directory.Exists(_root)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // On Linux CI, volume-mounted files written by Docker containers (e.g. metadata.db)
            // may belong to container UIDs that the host runner cannot remove.
        }
    }

    private sealed class CapturingWorkspaceProvider(ISandboxWorkspaceProvider inner) : ISandboxWorkspaceProvider
    {
        public List<SandboxWorkspaceAssignment> Assignments { get; } = [];

        public async ValueTask<SandboxWorkspaceAssignment> AssignAsync(
            SandboxAssignmentIdentity identity,
            CancellationToken cancellationToken = default)
        {
            var assignment = await inner.AssignAsync(identity, cancellationToken);
            lock (Assignments) Assignments.Add(assignment);
            return assignment;
        }
    }

    /// <summary>Makes runtime removal report failure so the unproven-detachment path can be exercised.</summary>
    private sealed class UnprovableRemovalCommandRunner(ISandboxCommandRunner inner) : ISandboxCommandRunner
    {
        public Task<SandboxCommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            arguments.Count > 0 && arguments[0] == "rm"
                ? Task.FromResult(new SandboxCommandResult(
                    1, "", "simulated: the runtime did not prove it released the assignment"))
                : inner.RunAsync(executable, arguments, cancellationToken);
    }
}
