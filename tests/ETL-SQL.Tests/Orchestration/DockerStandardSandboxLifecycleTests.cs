using ETL_SQL.Orchestrator.Execution;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// The sandbox lifecycle contract on an ordinary shared-kernel runtime — **Standard** evidence.
/// </summary>
/// <remarks>
/// This lane proves the shape of the contract against a real runtime and a real ETL-SQL workload on
/// an ordinary development host. It is explicitly not a hostile-tenant boundary result: `runc` does
/// not separate mutually untrusted customers, and the Standard provider mode refuses to claim a
/// Hardened or Dedicated tier. Cite <see cref="DockerHardenedSandboxLifecycleTests"/> for that.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class DockerStandardSandboxLifecycleTests : DockerSandboxLifecycleTestsBase
{
    protected override SandboxIsolationTier Tier => SandboxIsolationTier.Standard;
    protected override string ExpectedRuntime => DockerSandboxEnvironment.StandardRuntime;
    protected override string ExpectedImageIdentity => DockerSandboxEnvironment.ImageId;

    protected override DockerSandboxExecutionOptions Options() => new()
    {
        Mode = DockerSandboxMode.Standard,
        Image = DockerSandboxEnvironment.Image,
        LocalImageId = DockerSandboxEnvironment.ImageId,
        Runtime = DockerSandboxEnvironment.StandardRuntime,
        HostPolicyVersion = "docker-runc-standard",
        SessionRoot = SessionRoot,
        MachineKeyRoot = ProvisionMachineKeyRoot(),
        Entrypoint = "/app/etl-sql"
    };

    [DockerStandardSandboxFact]
    public Task StandardRuntimeRunsTenantCodeUnderItsOwnAssignmentMountsOnly() =>
        VerifyRuntimeRunsTenantCodeUnderItsOwnAssignmentMountsOnly();

    [DockerStandardSandboxFact]
    public Task StandardSuccessiveAssignmentsNeverReuseAStorageIdentifierOrSeePriorResidue() =>
        VerifySuccessiveAssignmentsNeverReuseAStorageIdentifierOrSeePriorResidue();

    [DockerStandardSandboxFact]
    public Task StandardDifferentTenantsNeverShareAWorkspaceSessionOrKeyPath() =>
        VerifyDifferentTenantsNeverShareAWorkspaceSessionOrKeyPath();

    [DockerStandardSandboxFact]
    public Task StandardCancellingTenantCodeRemovesTheRuntimeAndItsWritableState() =>
        VerifyCancellingTenantCodeRemovesTheRuntimeAndItsWritableState();

    [DockerStandardSandboxFact]
    public Task StandardForciblyTerminatedRuntimeIsCleanedUpAndReconciledAsDetached() =>
        VerifyForciblyTerminatedRuntimeIsCleanedUpAndReconciledAsDetached();

    [DockerStandardSandboxFact]
    public Task StandardUnprovenRuntimeDetachmentRetainsWritableStateInsteadOfDeletingIt() =>
        VerifyUnprovenRuntimeDetachmentRetainsWritableStateInsteadOfDeletingIt();

    [DockerStandardSandboxFact]
    public Task StandardCheckpointedStateResumesInADifferentSandbox() =>
        VerifyCheckpointedStateResumesInADifferentSandbox();

    /// <summary>
    /// The reserved-placement contract on an ordinary runtime. It proves the refusal logic against
    /// real containers; the citable Dedicated-tier result comes from
    /// <see cref="DockerDedicatedSandboxLifecycleTests"/> on a hardened runtime.
    /// </summary>
    [DockerStandardSandboxFact]
    public Task StandardHostFixedToOneTenantRunsOnlyItsOwnTenantAndPool() =>
        VerifyReservedPlacementRunsOnlyTheHostsOwnTenantAndPool(Options() with
        {
            DedicatedTenantId = "tenant-a",
            DedicatedPoolId = ReservedPoolId
        });
}
