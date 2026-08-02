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
}
