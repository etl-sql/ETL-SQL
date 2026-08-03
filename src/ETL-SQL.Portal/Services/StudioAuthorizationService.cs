using System.Security.Claims;

namespace ETL_SQL.Portal.Services;

public static class StudioCapabilities
{
    public const string StudioAccess = nameof(StudioAccess);
    public const string ScriptRead = nameof(ScriptRead);
    public const string ScriptPreview = nameof(ScriptPreview);
    public const string ScriptRun = nameof(ScriptRun);
    public const string ScriptSave = nameof(ScriptSave);
    public const string ReportPublish = nameof(ReportPublish);
    public const string ScriptIngress = nameof(ScriptIngress);
    public const string SourceCommit = nameof(SourceCommit);
    public const string SourcePush = nameof(SourcePush);

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        StudioAccess, ScriptRead, ScriptPreview, ScriptRun, ScriptSave, ReportPublish,
        ScriptIngress, SourceCommit, SourcePush
    };
}

public sealed class StudioAuthorizationService(PortalConfig config)
{
    public const string CapabilityClaim = "studio_capability";

    public StudioDeploymentMode Mode => config.Studio?.Mode ?? StudioDeploymentMode.Disabled;

    public bool HasCapability(ClaimsPrincipal principal, string capability)
    {
        if (!StudioCapabilities.All.Contains(capability) || principal.Identity?.IsAuthenticated != true)
            return false;

        if (principal.Claims.Any(claim =>
            string.Equals(claim.Type, CapabilityClaim, StringComparison.OrdinalIgnoreCase)
            && string.Equals(claim.Value, capability, StringComparison.OrdinalIgnoreCase)))
            return true;

        var mappings = config.Studio?.RoleCapabilities;
        if (mappings is null) return false;
        foreach (var (role, capabilities) in mappings)
        {
            if (!principal.IsInRole(role)) continue;
            if (capabilities.Any(value => string.Equals(value, capability, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public IReadOnlyList<string> EffectiveCapabilities(ClaimsPrincipal principal) =>
        StudioCapabilities.All.Where(capability => HasCapability(principal, capability))
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Resolves capabilities from role names alone, for answering "what can <em>that</em> user do"
    /// without their session. Capability claims are per-token, so a claim-granted capability cannot
    /// be seen from here — the diagnostic reports the configured role mapping, which is what an
    /// administrator can actually change.
    /// </summary>
    public IReadOnlyList<string> EffectiveCapabilitiesForRoles(IEnumerable<string> roles)
    {
        var mappings = config.Studio?.RoleCapabilities;
        if (mappings is null) return [];

        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            if (!mappings.TryGetValue(role, out var capabilities)) continue;
            foreach (var capability in capabilities)
            {
                if (StudioCapabilities.All.Contains(capability))
                    granted.Add(capability);
            }
        }

        return [.. granted.OrderBy(capability => capability, StringComparer.Ordinal)];
    }

    /// <summary>
    /// <c>HttpContext.Items</c> key holding the capability that authorized the current request.
    /// <see cref="ETL_SQL.Portal.Filters.RequireStudioCapabilityAttribute"/> stamps it and
    /// <see cref="AuditService"/> records it, so an audited Studio mutation says which capability
    /// let it through rather than leaving a reviewer to infer it from the route.
    /// </summary>
    public const string AuthorizedCapabilityItem = "studio.authorized-capability";
}
