using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Policy Authority could already validate, publish, activate, canary and roll back — every verb,
/// and no consequence. These cover the question asked immediately before pressing activate: what
/// happens when I do?
/// </summary>
[Trait("Category", "Portal")]
public sealed class PolicyImpactTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class SigningPortalFactory : PortalWebFactory
    {
        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IPolicyEnvelopeSigner>();
            services.AddSingleton<IPolicyEnvelopeSigner>(new RsaPolicyEnvelopeSigner(RSA.Create(2048)));
        }
    }

    /// <summary>Signing host that also requires remote audit delivery with no collector configured.</summary>
    private sealed class RequiresRemoteAuditFactory : PortalWebFactory
    {
        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IPolicyEnvelopeSigner>();
            services.AddSingleton<IPolicyEnvelopeSigner>(new RsaPolicyEnvelopeSigner(RSA.Create(2048)));
        }

        protected override void CustomizePortalConfig(PortalConfig config) =>
            config.Audit.RequireRemoteDelivery = true;
    }

    private static string DocJson(bool requireRemoteAudit = false) =>
        OrganizationPolicySchema.Serialize(new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection
            {
                ApprovedRoots = [Path.GetTempPath().TrimEnd('\\', '/')]
            },
            MutationGuardrails = new MutationGuardrailPolicySection
            {
                RequireRemoteAuditForMutations = requireRemoteAudit
            }
        });

    [Fact]
    public async Task ReportsApprovalState_AndFlagsAReviewerWhoIsTheAuthor()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        await PublishAsync(client, adminToken, "1.0.0", DocJson(), reviewer: "bob");
        var reviewed = await ImpactAsync(client, adminToken);
        var approval = reviewed["approval"]!.AsObject();
        Assert.True(approval["reviewed"]!.GetValue<bool>());
        Assert.True(approval["separationOfDuties"]!.GetValue<bool>());

        // A filled-in reviewer field is not the same as a second pair of eyes: the author is 'admin'.
        await PublishAsync(client, adminToken, "1.1.0", DocJson(), reviewer: "admin");
        var selfReviewed = await ImpactAsync(client, adminToken, "1.1.0");
        var selfApproval = selfReviewed["approval"]!.AsObject();
        Assert.True(selfApproval["reviewed"]!.GetValue<bool>());
        Assert.False(selfApproval["separationOfDuties"]!.GetValue<bool>());
        Assert.Contains("second pair of eyes",
            selfApproval["explanation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DistinguishesRegisteredMachinesFromReachableOnes()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        await PublishAsync(client, adminToken, "1.0.0", DocJson(), reviewer: "bob");

        await SeedMachineAsync(factory, "fresh-1", lastSeen: DateTimeOffset.UtcNow.AddMinutes(-5));
        await SeedMachineAsync(factory, "stale-1",
            lastSeen: DateTimeOffset.UtcNow.AddHours(-(PolicyImpactService.StaleAfterHours + 12)));
        await SeedMachineAsync(factory, "never-1", lastSeen: null);
        await SeedMachineAsync(factory, "revoked-1", lastSeen: DateTimeOffset.UtcNow, revoked: true);

        var fleet = (await ImpactAsync(client, adminToken))["fleet"]!.AsObject();

        Assert.Equal(4, fleet["registeredMachines"]!.GetValue<int>());
        Assert.Equal(1, fleet["revoked"]!.GetValue<int>());
        // Registered is not the same as reachable, and the difference is what misleads a rollout.
        Assert.Equal(2, fleet["stale"]!.GetValue<int>());
        Assert.Equal(1, fleet["neverSeen"]!.GetValue<int>());
        Assert.Contains(fleet["findings"]!.AsArray().Select(f => f!.GetValue<string>()),
            finding => finding.Contains("will not pick this up", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LinksEachMachineToTheVersionItActuallyReceives()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        await PublishAsync(client, adminToken, "1.0.0", DocJson(), reviewer: "bob");

        await SeedMachineAsync(factory, "canary-1", DateTimeOffset.UtcNow, canaryGroup: "ring0");
        await SeedMachineAsync(factory, "rest-1", DateTimeOffset.UtcNow);
        await SeedMachineAsync(factory, "gone-1", DateTimeOffset.UtcNow, revoked: true);

        Assert.Equal(HttpStatusCode.OK,
            (await AuthPost(client, adminToken, "/api/admin/policy-authority/publish-canary", new
            {
                tenant = "acme",
                environment = "prod",
                policyVersion = "1.1.0-canary",
                policyJson = DocJson(),
                reviewer = "bob",
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
                canaryGroup = "ring0"
            })).StatusCode);

        var machines = (await ImpactAsync(client, adminToken))["machines"]!.AsArray()
            .ToDictionary(m => m!["machineId"]!.GetValue<string>(), m => m!.AsObject());

        Assert.Equal("1.1.0-canary", machines["canary-1"]["effectiveVersion"]!.GetValue<string>());
        Assert.Equal("1.0.0", machines["rest-1"]["effectiveVersion"]!.GetValue<string>());
        Assert.Equal("none (revoked)", machines["gone-1"]["effectiveVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task WarnsWhenActivatingWouldStartRefusingMutations()
    {
        using var factory = new RequiresRemoteAuditFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        // The policy demands remote audit delivery; this host has no collector configured.
        await PublishAsync(client, adminToken, "1.0.0", DocJson(requireRemoteAudit: true), reviewer: "bob");

        var collector = (await ImpactAsync(client, adminToken))["collector"]!.AsObject();
        Assert.True(collector["policyRequiresRemoteDelivery"]!.GetValue<bool>());
        Assert.False(collector["collectorConfigured"]!.GetValue<bool>());
        Assert.True(collector["wouldBlockMutations"]!.GetValue<bool>());
        Assert.Contains("503", collector["explanation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APolicyWithNoAuditRequirement_CannotBlockMutations()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        await PublishAsync(client, adminToken, "1.0.0", DocJson(requireRemoteAudit: false), reviewer: "bob");

        var collector = (await ImpactAsync(client, adminToken))["collector"]!.AsObject();
        Assert.False(collector["policyRequiresRemoteDelivery"]!.GetValue<bool>());
        Assert.False(collector["wouldBlockMutations"]!.GetValue<bool>());
    }

    [Fact]
    public async Task UnknownTenantOrEnvironment_Is404_AndTheRouteIsAdministratorOnly()
    {
        using var factory = new SigningPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        Assert.Equal(HttpStatusCode.NotFound,
            (await AuthGet(client, adminToken,
                "/api/admin/policy-authority/impact?tenant=nobody&environment=nowhere")).StatusCode);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await CreateViewerAsync(client, adminToken, $"pol_deny_{suffix}");
        var viewerToken = await LoginAsync(client, $"pol_deny_{suffix}", "Ready@Test2!");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AuthGet(client, viewerToken,
                "/api/admin/policy-authority/impact?tenant=acme&environment=prod")).StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SeedMachineAsync(
        PortalWebFactory factory, string machineId, DateTimeOffset? lastSeen,
        string? canaryGroup = null, bool revoked = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.Set<PolicyMachineEntity>().Add(new PolicyMachineEntity
        {
            MachineId = machineId,
            EnrollmentId = $"enr-{machineId}",
            Tenant = "acme",
            Environment = "prod",
            LastSeenAtUtc = lastSeen,
            CanaryGroup = canaryGroup,
            Revoked = revoked
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonObject> ImpactAsync(
        HttpClient client, string adminToken, string? version = null)
    {
        var url = "/api/admin/policy-authority/impact?tenant=acme&environment=prod"
            + (version is null ? "" : $"&version={version}");
        var response = await AuthGet(client, adminToken, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!;
    }

    private static async Task PublishAsync(
        HttpClient client, string adminToken, string version, string policyJson, string reviewer)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/policy-authority/publish", new
        {
            tenant = "acme",
            environment = "prod",
            policyVersion = version,
            policyJson,
            reviewer,
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            staged = false
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task CreateViewerAsync(HttpClient client, string adminToken, string username)
    {
        var create = await AuthPost(client, adminToken, "/api/admin/users", new
        {
            username,
            email = $"{username}@test.local",
            password = "Initial@Test1!",
            role = "Viewer"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var initial = await LoginAsync(client, username, "Initial@Test1!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Initial@Test1!", newPassword = "Ready@Test2!" })).StatusCode);
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        Assert.Equal(HttpStatusCode.NoContent,
            (await AuthPost(client, initial, "/api/auth/change-password",
                new { currentPassword = "Admin@12345!", newPassword = "Admin@Tests99!" })).StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Json))!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        SendAsync(client, HttpMethod.Get, token, url, null);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Post, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        await IfMatchVersioning.StampAsync(client, request, token);
        return await client.SendAsync(request);
    }
}
