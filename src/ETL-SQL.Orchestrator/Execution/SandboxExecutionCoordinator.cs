using System.Runtime.ExceptionServices;

namespace ETL_SQL.Orchestrator.Execution;

public enum SandboxIsolationTier
{
    Local = 0,
    Standard = 1,
    Hardened = 2,
    Dedicated = 3
}

public enum SandboxTerminalStatus
{
    Succeeded,
    Failed,
    Cancelled,
    Ambiguous
}

/// <summary>Portable limits that every execution provider must enforce for one attempt.</summary>
public sealed record SandboxResourceLimits
{
    public required TimeSpan MaxDuration { get; init; }
    public required long MaxMemoryBytes { get; init; }
    public required long MaxScratchBytes { get; init; }
    public required int MaxProcesses { get; init; }
    /// <summary>
    /// CPU time the attempt may consume per wall-clock second, in cores. It is required rather than
    /// optional: an unbounded workload starves every co-tenant on the host, and a limit a provider is
    /// free to ignore is worse than none because the fleet believes the ceiling exists.
    /// </summary>
    public required double MaxCpuCores { get; init; }
    /// <summary>
    /// Optional block-I/O ceiling in operations per second, applied to reads and writes alike. Null
    /// means this profile makes no I/O promise; a positive value a host cannot actually enforce is a
    /// startup failure rather than a quietly dropped control.
    /// </summary>
    public int? MaxIops { get; init; }
    /// <summary>
    /// Concurrent connector connections one attempt may hold. The engine already enforces this per
    /// script; carrying it in the profile makes it a server-owned per-tenant ceiling rather than a
    /// constant baked into whichever worker image the host happens to run.
    /// </summary>
    public required int MaxConnectorConcurrency { get; init; }

    internal void Validate()
    {
        if (MaxDuration <= TimeSpan.Zero || MaxMemoryBytes <= 0 || MaxScratchBytes <= 0 || MaxProcesses <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(SandboxResourceLimits),
                "Sandbox duration, memory, scratch, and process limits must all be positive.");
        if (MaxCpuCores <= 0 || double.IsNaN(MaxCpuCores) || double.IsInfinity(MaxCpuCores))
            throw new ArgumentOutOfRangeException(
                nameof(MaxCpuCores), "The sandbox CPU limit must be a positive number of cores.");
        if (MaxIops is <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaxIops), "A declared sandbox IOPS limit must be positive.");
        if (MaxConnectorConcurrency <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaxConnectorConcurrency),
                "The sandbox connector concurrency limit must be positive.");
    }
}

/// <summary>
/// Provider-neutral execution request. It contains server-resolved identities and capability handles,
/// never raw provider credentials or a caller-selected physical runtime.
/// </summary>
public sealed record SandboxWorkloadRequest
{
    public required SandboxAssignmentIdentity Assignment { get; init; }
    public required string ArtifactId { get; init; }
    public required string ArtifactHash { get; init; }
    public required string PolicyVersion { get; init; }
    public required string BindingVersion { get; init; }
    public required SandboxIsolationTier RequiredIsolationTier { get; init; }
    public required SandboxResourceLimits Limits { get; init; }
    public required ResolvedSandboxAdmissionPolicy AdmissionPolicy { get; init; }
    public IReadOnlyList<string> CapabilityHandles { get; init; } = [];
    public string? CheckpointHandle { get; init; }
    public string? SessionId { get; init; }
    public bool ResumeFromCheckpoint { get; init; }
    public IReadOnlyDictionary<string, string> VariableOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    /// <summary>Set only by the admission coordinator after durable activation.</summary>
    public string? AdmissionId { get; internal init; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Assignment);
        ArgumentException.ThrowIfNullOrWhiteSpace(ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(PolicyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(BindingVersion);
        ArgumentNullException.ThrowIfNull(Limits);
        Limits.Validate();
        ArgumentNullException.ThrowIfNull(AdmissionPolicy);
        AdmissionPolicy.Validate();

        if (!ArtifactHash.StartsWith("sha256:", StringComparison.Ordinal) ||
            ArtifactHash.Length != 71 ||
            !ArtifactHash.AsSpan(7).ToString().All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "The immutable artifact hash must be a canonical sha256: value.",
                nameof(ArtifactHash));
        }

        if (CapabilityHandles.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Capability handles cannot contain blank values.", nameof(CapabilityHandles));
        if (ResumeFromCheckpoint && string.IsNullOrWhiteSpace(SessionId))
            throw new ArgumentException("Checkpoint resume requires a server-owned session id.", nameof(SessionId));
        if (VariableOverrides.Any(pair => string.IsNullOrWhiteSpace(pair.Key)))
            throw new ArgumentException("Variable override names cannot be blank.", nameof(VariableOverrides));
    }
}

/// <summary>Runtime evidence captured while the sandbox is prepared but before tenant code executes.</summary>
public sealed record SandboxProviderEvidence(
    string Provider,
    string ProviderVersion,
    string Runtime,
    SandboxIsolationTier IsolationTier,
    string ImageDigest,
    string HostPolicyVersion);

public sealed record SandboxExecutionOutcome(
    SandboxTerminalStatus Status,
    int? ExitCode = null,
    string? SanitizedDiagnostic = null,
    ScriptExecutionResult? Result = null);

/// <summary>
/// One started sandbox. A successful <see cref="DestroyAsync"/> call is the provider's guarantee that
/// the sandbox is stopped, its mounts are detached, and it can no longer access the assignment root.
/// </summary>
public interface ISandboxAttempt
{
    SandboxProviderEvidence Evidence { get; }
    Task<SandboxExecutionOutcome> RunAsync(CancellationToken cancellationToken);
    Task DestroyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Environment binding for a hardened runtime. Prepare must be transactional and leave tenant code
/// stopped: if it throws, no sandbox may retain the supplied workspace; if it succeeds, the
/// coordinator validates evidence before calling <see cref="ISandboxAttempt.RunAsync"/>.
/// </summary>
public interface ISandboxExecutionProvider
{
    Task<ISandboxAttempt> PrepareAsync(
        SandboxWorkloadRequest request,
        SandboxWorkspaceAssignment workspace,
        CancellationToken cancellationToken);
}

public sealed class SandboxTeardownException : Exception
{
    public SandboxTeardownException(
        Exception teardownFailure,
        string admissionId,
        Exception? executionFailure = null)
        : base(
            "Sandbox destruction did not prove that the runtime released its assignment; " +
            "writable state was retained for reconciliation.",
            teardownFailure)
    {
        AdmissionId = admissionId;
        ExecutionFailure = executionFailure;
    }

    public string AdmissionId { get; }
    public Exception? ExecutionFailure { get; }
}

public sealed class SandboxPrepareTeardownException : Exception
{
    public SandboxPrepareTeardownException(Exception preparationFailure, Exception teardownFailure, string admissionId)
        : base(
            "Sandbox preparation failed and runtime removal could not be proven; writable state was retained for reconciliation.",
            teardownFailure)
    {
        PreparationFailure = preparationFailure;
        AdmissionId = admissionId;
    }

    public Exception PreparationFailure { get; }
    public string AdmissionId { get; }
}

/// <summary>
/// Enforces the provider-neutral attempt lifecycle. Workspace deletion occurs only after the runtime
/// provider proves the sandbox is destroyed and detached; uncertain teardown retains state rather than
/// deleting storage that a live workload might still have mounted.
/// </summary>
public sealed class SandboxExecutionCoordinator(
    ISandboxWorkspaceProvider workspaces,
    ISandboxAdmissionController admissions)
{
    public async Task<SandboxExecutionOutcome> ExecuteAsync(
        ISandboxExecutionProvider provider,
        SandboxWorkloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var admission = await admissions.AcquireAsync(
            request.Assignment.Tenant,
            request.AdmissionPolicy,
            cancellationToken);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, admission.LeaseLost);
        var executionToken = executionCancellation.Token;
        SandboxWorkspaceAssignment? workspace = null;
        ISandboxAttempt? attempt = null;
        var runtimeDestroyed = false;
        var retainForReconciliation = false;
        try
        {
            workspace = await workspaces.AssignAsync(request.Assignment, executionToken);
            try
            {
                attempt = await provider.PrepareAsync(
                    request with { AdmissionId = admission.AdmissionId }, workspace, executionToken);
            }
            catch (SandboxPrepareTeardownException)
            {
                retainForReconciliation = true;
                throw;
            }
            if (attempt is null)
                throw new InvalidOperationException("The sandbox provider prepared no attempt.");

            Exception? executionFailure = null;
            SandboxExecutionOutcome? outcome = null;
            try
            {
                ValidateEvidence(attempt.Evidence, request.RequiredIsolationTier);
                outcome = await attempt.RunAsync(executionToken);
                if (outcome is null)
                    throw new InvalidOperationException("The sandbox provider returned no terminal outcome.");
            }
            catch (Exception ex)
            {
                executionFailure = ex;
            }

            try
            {
                // Cleanup is authority revocation, not user work. Caller cancellation must not skip it.
                await attempt.DestroyAsync(CancellationToken.None);
                runtimeDestroyed = true;
            }
            catch (Exception teardownFailure)
            {
                throw new SandboxTeardownException(
                    teardownFailure, admission.AdmissionId, executionFailure);
            }

            if (executionFailure is not null)
                ExceptionDispatchInfo.Capture(executionFailure).Throw();

            return outcome!;
        }
        finally
        {
            // A failed transactional Prepare owns no runtime. A prepared runtime must first prove it has
            // detached; otherwise retain the workspace for fenced reconciliation and residue evidence.
            if (!retainForReconciliation && (attempt is null || runtimeDestroyed))
            {
                try
                {
                    if (workspace is not null)
                        await workspace.DestroyAsync(CancellationToken.None);
                }
                finally
                {
                    await admission.ReleaseAsync();
                }
            }
        }
    }

    private static void ValidateEvidence(
        SandboxProviderEvidence evidence,
        SandboxIsolationTier requiredTier)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(evidence.Provider) ||
            string.IsNullOrWhiteSpace(evidence.ProviderVersion) ||
            string.IsNullOrWhiteSpace(evidence.Runtime) ||
            string.IsNullOrWhiteSpace(evidence.ImageDigest) ||
            string.IsNullOrWhiteSpace(evidence.HostPolicyVersion))
        {
            throw new InvalidOperationException("Sandbox provider evidence is incomplete.");
        }

        if (evidence.IsolationTier < requiredTier)
        {
            throw new UnauthorizedAccessException(
                $"Provider isolation tier '{evidence.IsolationTier}' does not satisfy required tier '{requiredTier}'.");
        }
    }
}
