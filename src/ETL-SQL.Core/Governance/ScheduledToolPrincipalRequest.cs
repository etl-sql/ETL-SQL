namespace ETL_SQL.Core.Governance;

/// <summary>Tenant context comes from the requesting service credential.</summary>
public sealed record ScheduledToolPrincipalRequest(string OwnerKey)
{
    public const string Path = "/api/auth/scheduled-tool-principal";
    public const string Scope = "orchestrator.tool-identity.read";
    public const string ConfigurationSection = "Orchestrator:ScheduledToolIdentity";

    public bool TryGetOwner(out ToolPrincipalKind kind, out string id)
    {
        kind = default;
        id = string.Empty;
        var separator = OwnerKey?.IndexOf(':') ?? -1;
        if (separator < 1) return false;

        var subjectType = OwnerKey![..separator];
        if (subjectType.Equals("user", StringComparison.OrdinalIgnoreCase))
            kind = ToolPrincipalKind.User;
        else if (subjectType.Equals("service", StringComparison.OrdinalIgnoreCase))
            kind = ToolPrincipalKind.Service;
        else
            return false;

        id = OwnerKey[(separator + 1)..];
        return ToolUseGrant.IsStableId(id);
    }
}

public sealed record SavedJobToolApprovalRequest(bool? Approved);

public sealed record ScheduledToolPrincipalResponse(string TenantId, string OwnerKey, string[] GroupIds)
{
    public ToolExecutionPrincipal? ForOwner(string tenantId, ScheduledToolPrincipalRequest request) =>
        string.Equals(TenantId, tenantId, StringComparison.Ordinal)
        && string.Equals(OwnerKey, request.OwnerKey, StringComparison.Ordinal)
        && request.TryGetOwner(out var kind, out var id)
        && GroupIds is not null && GroupIds.All(ToolUseGrant.IsStableId)
            ? new ToolExecutionPrincipal(tenantId, kind, id, GroupIds, true)
            : null;
}
