using ETL_SQL.Portal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETL_SQL.Portal.Controllers;

/// <summary>What this caller should be offered in the Portal's top-level navigation.</summary>
/// <param name="Id">The element id the shell reveals, e.g. <c>studioNav</c>.</param>
/// <param name="Visible">Whether to offer it.</param>
/// <param name="Reason">Why not, when it is hidden. Diagnostic only — never rendered.</param>
public sealed record NavDestinationDto(string Id, bool Visible, string? Reason);

/// <summary>
/// The navigation answer, computed once on the server.
///
/// <para>Every page used to re-derive this from JWT claims, in five different spellings, and two
/// of the destinations cannot be derived from claims at all. <b>Docs</b> depends on whether the
/// Documentation module is enabled — a server fact with no claim — so every page offered a Docs
/// link that 404s on a deployment with the module off. <b>Studio</b> depends on a Studio
/// capability, and pages revealed it whenever the capability *probe* succeeded; since that probe
/// was deliberately opened to every authenticated user, the entry was shown to everyone, including
/// the roles that hold no Studio capability at all.</para>
///
/// <para>Both are the same failure: a navigation that offers what it cannot deliver reads as the
/// product being broken rather than as a permission the user lacks. Neither is fixable by being
/// more careful in six copies of the rule, which is why the rule now has one home — here, where
/// module state and capability state both already live.</para>
///
/// <para>This reveals nothing a caller cannot already establish by pressing the link. It reports
/// only which entry points to offer <em>this</em> caller, never the deployment's module
/// configuration in general.</para>
/// </summary>
[ApiController]
[Route("api/portal/navigation")]
[Authorize]
public sealed class NavigationController(
    PortalModuleRegistry modules,
    StudioAuthorizationService studio) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<NavDestinationDto>> Get()
    {
        var destinations = new List<NavDestinationDto>
        {
            Role("adminNav", "Admin"),
            Role("orchestratorNav", "Admin", "OrchestratorManager", "OrchestratorViewer"),
            Module("docsNav", "Documentation"),
            Studio()
        };

        return Ok(destinations);
    }

    private NavDestinationDto Role(string id, params string[] roles) =>
        roles.Any(User.IsInRole)
            ? new(id, true, null)
            : new(id, false, $"requires one of: {string.Join(", ", roles)}");

    private NavDestinationDto Module(string id, string module) =>
        modules.IsEnabled(module)
            ? new(id, true, null)
            : new(id, false, $"the {module} module is disabled");

    /// <summary>
    /// Studio needs three things at once, and checking any one of them alone is what produced the
    /// defect: the Designer module enabled, a deployment mode that is not <c>Disabled</c>, and the
    /// <c>StudioAccess</c> capability. Capabilities are deny-by-default with no administrator
    /// bypass, so "is an Admin" is not a sufficient test either.
    /// </summary>
    private NavDestinationDto Studio()
    {
        if (!modules.IsEnabled("Designer"))
            return new("studioNav", false, "the Designer module is disabled");
        if (studio.Mode == StudioDeploymentMode.Disabled)
            return new("studioNav", false, "Studio is disabled for this deployment");
        if (!studio.HasCapability(User, StudioCapabilities.StudioAccess))
            return new("studioNav", false, $"requires the {StudioCapabilities.StudioAccess} capability");

        return new("studioNav", true, null);
    }
}
