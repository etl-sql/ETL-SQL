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

    internal void Validate()
    {
        if (MaxDuration <= TimeSpan.Zero || MaxMemoryBytes <= 0 || MaxScratchBytes <= 0 || MaxProcesses <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(SandboxResourceLimits),
                "Sandbox duration, memory, scratch, and process limits must all be positive.");
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
    string? SanitizedDiagnostic = null);

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
        try
        {
            workspace = await workspaces.AssignAsync(request.Assignment, executionToken);
            attempt = await provider.PrepareAsync(request, workspace, executionToken);
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
            if (attempt is null || runtimeDestroyed)
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
