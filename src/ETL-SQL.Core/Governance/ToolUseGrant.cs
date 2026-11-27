using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Governance;

public enum ToolPrincipalKind
{
    User,
    Group,
    Service
}

public sealed record ToolUseGrant(string TenantId, ToolPrincipalKind Kind, string PrincipalId)
{
    internal bool IsValid() => Multitenancy.TenantId.TryParse(TenantId, out var tenant)
        && TenantId == tenant.Value && Enum.IsDefined(Kind) && IsStableId(PrincipalId);

    internal bool SameAs(ToolUseGrant other) =>
        Kind == other.Kind
        && string.Equals(TenantId, other.TenantId, StringComparison.Ordinal)
        && string.Equals(PrincipalId, other.PrincipalId, StringComparison.OrdinalIgnoreCase);

    internal static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 192
        && value == value.Trim() && !value.Any(char.IsControl);
}

/// <summary>Stable grant identities supplied by a verified host handoff.</summary>
public sealed record ToolExecutionPrincipal(
    string TenantId, ToolPrincipalKind Kind, string PrincipalId,
    IReadOnlyList<string> GroupIds, bool WithinExecutionScope, bool ApprovedSavedScriptOnly = false)
{
    internal bool CanUse(ToolUseGrant grant) =>
        WithinExecutionScope && (Kind is ToolPrincipalKind.User or ToolPrincipalKind.Service)
        && ToolUseGrant.IsStableId(PrincipalId)
        && string.Equals(grant.TenantId, TenantId, StringComparison.Ordinal)
        && (grant.Kind == ToolPrincipalKind.Group
            ? GroupIds?.Contains(grant.PrincipalId, StringComparer.OrdinalIgnoreCase) == true
            : grant.Kind == Kind && string.Equals(grant.PrincipalId, PrincipalId, StringComparison.OrdinalIgnoreCase));
}
