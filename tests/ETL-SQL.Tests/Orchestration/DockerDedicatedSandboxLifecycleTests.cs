using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// The sandbox lifecycle contract on a host reserved for a single tenant — **Dedicated** evidence.
/// </summary>
/// <remarks>
/// A Dedicated host is a Hardened host that additionally refuses every tenant and capacity pool but
/// its own, so this lane requires the same registered gVisor/Kata runtime and digest-pinned image and
/// skips with the same precise diagnostic without them. It runs the shared lifecycle assertions plus
/// reserved placement, which is the part that cannot be shown on a shared host: the reserved host
/// runs its own tenant's work and creates no runtime at all for anyone else's.
///
/// <para>The cross-tenant separation test from the shared lanes is deliberately absent — it places a
/// second tenant's work, which this host must refuse. That refusal is asserted here instead.</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DockerDedicatedSandboxLifecycleTests : DockerSandboxLifecycleTestsBase
{
    private const string ReservedTenant = "tenant-a";

    protected override SandboxIsolationTier Tier => SandboxIsolationTier.Dedicated;
    protected override string ExpectedRuntime => DockerSandboxEnvironment.HardenedRuntime;
    protected override string ExpectedImageIdentity => DockerSandboxEnvironment.HardenedDigest;

    protected override DockerSandboxExecutionOptions Options() => new()
    {
        Mode = DockerSandboxMode.Hardened,
        Image = DockerSandboxEnvironment.HardenedImage,
        ImageDigest = DockerSandboxEnvironment.HardenedDigest,
        Runtime = DockerSandboxEnvironment.HardenedRuntime,
        HostPolicyVersion = "docker-dedicated",
        SessionRoot = SessionRoot,
        MachineKeyRoot = ProvisionMachineKeyRoot(),
        Entrypoint = "/app/etl-sql",
        // The whole point of the tier: this worker belongs to exactly one tenant and one pool.
        DedicatedTenantId = ReservedTenant,
        DedicatedPoolId = ReservedPoolId
    };

    [DockerHardenedSandboxFact]
    public Task DedicatedHostRunsOnlyItsOwnTenantAndPool() =>
        VerifyReservedPlacementRunsOnlyTheHostsOwnTenantAndPool();

    [DockerHardenedSandboxFact]
    public Task DedicatedRuntimeRunsTenantCodeUnderItsOwnAssignmentMountsOnly() =>
        VerifyRuntimeRunsTenantCodeUnderItsOwnAssignmentMountsOnly();

    [DockerHardenedSandboxFact]
    public Task DedicatedSuccessiveAssignmentsNeverReuseAStorageIdentifierOrSeePriorResidue() =>
        VerifySuccessiveAssignmentsNeverReuseAStorageIdentifierOrSeePriorResidue();

    [DockerHardenedSandboxFact]
    public Task DedicatedCancellingTenantCodeRemovesTheRuntimeAndItsWritableState() =>
        VerifyCancellingTenantCodeRemovesTheRuntimeAndItsWritableState();

    [DockerHardenedSandboxFact]
    public Task DedicatedForciblyTerminatedRuntimeIsCleanedUpAndReconciledAsDetached() =>
        VerifyForciblyTerminatedRuntimeIsCleanedUpAndReconciledAsDetached();

    [DockerHardenedSandboxFact]
    public Task DedicatedCheckpointedStateResumesInADifferentSandbox() =>
        VerifyCheckpointedStateResumesInADifferentSandbox();

    [DockerHardenedSandboxFact]
    public Task DedicatedGrantedCapabilityIsMountedReadOnlyOnALiveRuntime() =>
        VerifyGrantedCapabilityIsMountedReadOnlyOnALiveRuntime();
}
