using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// The sandbox lifecycle contract on a registered gVisor or Kata runtime with a digest-pinned
/// image — **Hardened** evidence.
/// </summary>
/// <remarks>
/// This is the lane that may be cited as hostile-tenant boundary evidence. It runs the identical
/// assertions as the Standard lane, so a difference in result is a difference in the runtime rather
/// than in what was checked. It skips, with a precise diagnostic, on any host where no hardened
/// runtime is registered — an ordinary shared-kernel runtime is never substituted.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DockerHardenedSandboxLifecycleTests : DockerSandboxLifecycleTestsBase
{
    protected override SandboxIsolationTier Tier => SandboxIsolationTier.Hardened;
    protected override string ExpectedRuntime => DockerSandboxEnvironment.HardenedRuntime;
    protected override string ExpectedImageIdentity => DockerSandboxEnvironment.HardenedDigest;

    protected override DockerSandboxExecutionOptions Options() => new()
    {
        Mode = DockerSandboxMode.Hardened,
        Image = DockerSandboxEnvironment.HardenedImage,
        ImageDigest = DockerSandboxEnvironment.HardenedDigest,
        Runtime = DockerSandboxEnvironment.HardenedRuntime,
        HostPolicyVersion = "docker-hardened",
        SessionRoot = SessionRoot,
        MachineKeyRoot = ProvisionMachineKeyRoot(),
        Entrypoint = "/app/etl-sql"
    };

    [DockerHardenedSandboxFact]
    public Task HardenedRuntimeRunsTenantCodeUnderItsOwnAssignmentMountsOnly() =>
        VerifyRuntimeRunsTenantCodeUnderItsOwnAssignmentMountsOnly();

    [DockerHardenedSandboxFact]
    public Task HardenedSuccessiveAssignmentsNeverReuseAStorageIdentifierOrSeePriorResidue() =>
        VerifySuccessiveAssignmentsNeverReuseAStorageIdentifierOrSeePriorResidue();

    [DockerHardenedSandboxFact]
    public Task HardenedDifferentTenantsNeverShareAWorkspaceSessionOrKeyPath() =>
        VerifyDifferentTenantsNeverShareAWorkspaceSessionOrKeyPath();

    [DockerHardenedSandboxFact]
    public Task HardenedCancellingTenantCodeRemovesTheRuntimeAndItsWritableState() =>
        VerifyCancellingTenantCodeRemovesTheRuntimeAndItsWritableState();

    [DockerHardenedSandboxFact]
    public Task HardenedForciblyTerminatedRuntimeIsCleanedUpAndReconciledAsDetached() =>
        VerifyForciblyTerminatedRuntimeIsCleanedUpAndReconciledAsDetached();

    [DockerHardenedSandboxFact]
    public Task HardenedUnprovenRuntimeDetachmentRetainsWritableStateInsteadOfDeletingIt() =>
        VerifyUnprovenRuntimeDetachmentRetainsWritableStateInsteadOfDeletingIt();

    [DockerHardenedSandboxFact]
    public Task HardenedCheckpointedStateResumesInADifferentSandbox() =>
        VerifyCheckpointedStateResumesInADifferentSandbox();

    [DockerHardenedSandboxFact]
    public Task HardenedGrantedCapabilityIsMountedReadOnlyOnALiveRuntime() =>
        VerifyGrantedCapabilityIsMountedReadOnlyOnALiveRuntime();
}
