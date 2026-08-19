using ETL_SQL.Core.Governance;

namespace ETL_SQL.Orchestrator.Execution;

/// <summary>
/// Resolves a server-issued capability handle to the material a sandboxed workload may use.
/// </summary>
/// <remarks>
/// The handle is the only thing that ever crosses the request boundary. It names an entitlement the
/// server already decided the workload has; it is not the credential, and a workload cannot mint,
/// widen, or enumerate one. Resolution happens on the orchestrator side of the boundary so a tenant
/// never sees the material's origin, only the file it was given.
/// </remarks>
public interface ISandboxCapabilityResolver
{
    Task<string> ResolveAsync(
        SandboxAssignmentIdentity assignment,
        string capabilityHandle,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves capability handles through the governance secret provider, so capabilities are the same
/// material, with the same custody and rotation, as every other secret the product holds — rather
/// than a second credential store that exists only for sandboxes.
/// </summary>
public sealed class SecretBackedSandboxCapabilityResolver(ISecretProvider secrets)
    : ISandboxCapabilityResolver
{
    private readonly ISecretProvider _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));

    public async Task<string> ResolveAsync(
        SandboxAssignmentIdentity assignment,
        string capabilityHandle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ValidateHandle(capabilityHandle);

        // Capabilities are namespaced per tenant, so one tenant's handle can never resolve to
        // another's material even if both deployments use the same capability name.
        var name = $"sandbox/{assignment.Tenant.Tenant.Value}/{capabilityHandle}";
        SecretResolutionResult? result = null;
        try
        {
            result = await _secrets.ResolveAsync(name, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UnauthorizedAccessException(
                $"Capability '{capabilityHandle}' could not be resolved for this tenant.", ex);
        }

        if (string.IsNullOrEmpty(result?.Value))
        {
            throw new UnauthorizedAccessException(
                $"Capability '{capabilityHandle}' is not provisioned for this tenant; the sandbox was " +
                "not started rather than being run without a capability it was told it had.");
        }

        return result.Value;
    }

    /// <summary>
    /// A handle becomes a file name, so it must be one plain segment. Anything that could shape a
    /// path would let a caller choose where its material lands, or which file it overwrites.
    /// </summary>
    internal static void ValidateHandle(string capabilityHandle) =>
        CapabilityReference.ValidateHandle(capabilityHandle);
}
