using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// What the shell is told to offer, asserted on the server where the rule now lives.
///
/// <para>Two of these destinations cannot be decided from a token claim. Docs depends on whether
/// the Documentation module is enabled, and Studio on a capability that is deny-by-default with no
/// administrator bypass. Every page used to guess both, so a Docs link was offered on deployments
/// where <c>/docs.html</c> answers 404, and a Studio link was offered to every authenticated user
/// because the pages revealed it whenever the capability *probe* succeeded — and that probe was
/// deliberately opened to everyone.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class NavigationVisibilityTests
{
    private static async Task<Dictionary<string, bool>> DestinationsAsync(
        HostedPortalFactory factory, string token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync("/api/portal/navigation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonArray>();
        return body!.ToDictionary(
            node => node!["id"]!.GetValue<string>(),
            node => node!["visible"]!.GetValue<bool>());
    }

    /// <summary>
    /// The defect this endpoint exists for. Studio capabilities are deny-by-default and there is no
    /// administrator bypass, so a role holding none must not be offered the entry point — however
    /// senior the role is.
    /// </summary>
    [Fact]
    public async Task Studio_IsNotOffered_ToARoleHoldingNoStudioCapability()
    {
        using var factory = new HostedPortalFactory(portalConfig: config =>
            config.Studio.RoleCapabilities.Clear());
        var token = await AdminTokenAsync(factory);

        var destinations = await DestinationsAsync(factory, token);

        Assert.False(destinations["studioNav"],
            "Studio was offered to a caller holding no StudioAccess capability. Every gated Studio "
            + "route refuses them, so the entry point leads only to a 403.");
    }

    /// <summary>
    /// The positive half. Without it, an implementation that hid Studio unconditionally would pass
    /// the negative assertion and take the feature away from everyone who is entitled to it.
    /// </summary>
    [Fact]
    public async Task Studio_IsOffered_OnceTheCapabilityIsGranted()
    {
        using var factory = new HostedPortalFactory(portalConfig: config =>
        {
            config.Studio.RoleCapabilities.Clear();
            config.Studio.RoleCapabilities["Admin"] = ["StudioAccess"];
        });
        var token = await AdminTokenAsync(factory);

        var destinations = await DestinationsAsync(factory, token);

        Assert.True(destinations["studioNav"]);
    }

    /// <summary>
    /// Holding <em>some</em> Studio capability is not the same as holding the one that opens it.
    /// <c>StudioAccess</c> is specifically the discovery/open capability, so a caller granted only
    /// the others still has nowhere to go.
    /// </summary>
    [Fact]
    public async Task Studio_IsNotOffered_ForACapabilityThatIsNotStudioAccess()
    {
        using var factory = new HostedPortalFactory(portalConfig: config =>
        {
            config.Studio.RoleCapabilities.Clear();
            config.Studio.RoleCapabilities["Admin"] = ["ScriptRead", "ScriptSave"];
        });
        var token = await AdminTokenAsync(factory);

        var destinations = await DestinationsAsync(factory, token);

        Assert.False(destinations["studioNav"]);
    }

    /// <summary>Turning the Designer module off removes Studio regardless of capability.</summary>
    [Fact]
    public async Task Studio_IsNotOffered_WhenTheDesignerModuleIsDisabled()
    {
        using var factory = new HostedPortalFactory(portalConfig: config =>
            config.Modules.Designer = false);
        var token = await AdminTokenAsync(factory);

        var destinations = await DestinationsAsync(factory, token);

        Assert.False(destinations["studioNav"]);
    }

    /// <summary>
    /// <c>/docs.html</c> is served with a 404 when the module is off, so an always-visible Docs
    /// link is a navigation entry that leads to a bare error page.
    /// </summary>
    [Fact]
    public async Task Docs_IsNotOffered_WhenTheDocumentationModuleIsDisabled()
    {
        using var factory = new HostedPortalFactory(portalConfig: config =>
            config.Modules.Documentation = false);
        var token = await AdminTokenAsync(factory);

        var destinations = await DestinationsAsync(factory, token);

        Assert.False(destinations["docsNav"]);
    }

    [Fact]
    public async Task Docs_IsOffered_WhenTheDocumentationModuleIsEnabled()
    {
        using var factory = new HostedPortalFactory();
        var token = await AdminTokenAsync(factory);

        var destinations = await DestinationsAsync(factory, token);

        Assert.True(destinations["docsNav"]);
    }

    [Fact]
    public async Task Admin_IsOfferedToAnAdministrator()
    {
        using var factory = new HostedPortalFactory();
        var token = await AdminTokenAsync(factory);

        var destinations = await DestinationsAsync(factory, token);

        Assert.True(destinations["adminNav"]);
        Assert.True(destinations["orchestratorNav"]);
    }

    /// <summary>Anonymous callers get no answer at all rather than a permissive default.</summary>
    [Fact]
    public async Task Navigation_RequiresAuthentication()
    {
        using var factory = new HostedPortalFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/portal/navigation");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Signs in and clears the forced first-run change, which otherwise 403s every call.</summary>
    private static async Task<string> AdminTokenAsync(HostedPortalFactory factory)
    {
        const string initial = "Admin@12345!";
        const string changed = "Admin@Navigation99!";

        using var client = factory.CreateClient();
        var first = await LoginAsync(client, initial);

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = initial, newPassword = changed })
        };
        change.Headers.Authorization = new("Bearer", first);
        var changed_ = await client.SendAsync(change);
        Assert.Equal(HttpStatusCode.NoContent, changed_.StatusCode);

        return await LoginAsync(client, changed);
    }

    private static async Task<string> LoginAsync(HttpClient client, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonObject>();
        return body!["token"]!.GetValue<string>();
    }
}
