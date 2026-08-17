using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// A short-lived capability token used to grant custom tool runners temporary, restricted access
/// to specific network, file, Gateway, and secret resources, bound strictly to the current operation.
/// </summary>
public sealed record CapabilityToken
{
    public string TokenId { get; init; } = Guid.NewGuid().ToString("N");
    public string? TenantId { get; init; }
    public string? Environment { get; init; }
    public string? ToolDigest { get; init; }
    public string? OperationId { get; init; }
    public string? Actor { get; init; }
    public string? RunAttempt { get; init; }
    public string? PolicyVersion { get; init; }
    public string? Nonce { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }

    // Capabilities
    public bool AllowNetworkAccess { get; init; }
    public bool AllowGatewayResources { get; init; }
    public IReadOnlyList<string> AllowedPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowedNamedSecrets { get; init; } = Array.Empty<string>();

    // Limits
    public long? MaxMemoryBytes { get; init; }
    public long? MaxCpuTimeMs { get; init; }
}

/// <summary>
/// Issues and validates short-lived capability tokens for tool runners.
/// </summary>
public interface ICapabilityTokenIssuer
{
    /// <summary>
    /// Issues a secure token string from a capability definition.
    /// </summary>
    string IssueToken(CapabilityToken capability);

    /// <summary>
    /// Validates a raw token string and extracts the capability definition.
    /// </summary>
    bool TryValidateToken(string rawToken, out CapabilityToken? capability);
}
