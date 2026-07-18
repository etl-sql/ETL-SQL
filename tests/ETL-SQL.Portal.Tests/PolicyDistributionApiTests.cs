using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Machine-authenticated policy retrieval (TODO 3.1): enrolled machines fetch their signed
/// envelope with the enrollment headers the client runtime sends; responses are bound to the
/// registered tenant/environment; unknown, revoked, and reassigned identities are refused; client
/// certificates are enforced when registered; denials are audited.
/// </summary>
[Trait("Category", "Portal")]
public class PolicyDistributionApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string CertForwardHeader = "X-Test-Client-Cert";
    private const string EnvelopeUrl = "/api/policy-authority/envelope";

    private sealed class DistributionPortalFactory : PortalWebFactory
    {
        protected override void CustomizeConfiguration(Dictionary<string, string?> settings) =>
            settings["Portal:PolicyAuthority:ClientCertificateForwardingHeader"] = CertForwardHeader;

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IPolicyEnvelopeSigner>();
            services.AddSingleton<IPolicyEnvelopeSigner>(new RsaPolicyEnvelopeSigner(RSA.Create(2048)));
        }
    }

    [Fact]
    public async Task EnrolledMachine_RetrievesEnvelope_ThroughTheRealClientSource()
    {
        using var factory = new DistributionPortalFactory();
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
        var adminToken = await GetAdminTokenAsync(client);
        var publicKeyPem = await GetSigningPublicKeyAsync(client, adminToken);

        // The enrollment document is exactly what `etlsql enterprise enroll` writes on the machine.
        var enrollment = new EnterpriseEnrollmentDocument
        {
            Tenant = "acme",
            PolicyEndpoint = "https://localhost" + EnvelopeUrl,
            PolicySigningPublicKey = publicKeyPem
        };
        await RegisterMachineAsync(client, adminToken, enrollment.MachineId, enrollment.EnrollmentId,
            "acme", "prod");
        await PublishAsync(client, adminToken, "1.0.0");

        // Drive the actual client-side source so the transport contract can't drift.
        var source = new HttpsSignedEnterprisePolicySource(client, new Uri(enrollment.PolicyEndpoint));
        var envelope = await source.LoadAsync(enrollment);
        var document = EnterprisePolicySignature.VerifyAndParse(envelope, enrollment, DateTimeOffset.UtcNow);
        Assert.NotEmpty(document.Filesystem.ApprovedRoots);
        Assert.Equal("1.0.0", envelope.PolicyVersion);

        // Successful retrieval stamps the machine's last-seen time in the registry.
        var machines = await (await AuthGet(client, adminToken,
            "/api/admin/policy-authority/machines?tenant=acme&environment=prod"))
            .Content.ReadFromJsonAsync<JsonArray>(Json);
        var machine = machines!.Single(m => m!["machineId"]!.GetValue<string>() == enrollment.MachineId);
        Assert.NotNull(machine!["lastSeenAtUtc"]!.GetValue<DateTimeOffset?>());
    }

    [Fact]
    public async Task UnknownRevokedAndReassignedIdentities_AreRefusedAndAudited()
    {
        using var factory = new DistributionPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        await PublishAsync(client, adminToken, "1.0.0");

        // Missing headers → 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(EnvelopeUrl)).StatusCode);

        // Unknown machine → 403.
        var unknownId = Guid.NewGuid().ToString("N");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await MachineGet(client, "acme", Guid.NewGuid().ToString("N"), unknownId)).StatusCode);

        // Registered machine retrieves; after revocation the same identity is refused.
        var machineId = Guid.NewGuid().ToString("N");
        var enrollmentId = Guid.NewGuid().ToString("N");
        await RegisterMachineAsync(client, adminToken, machineId, enrollmentId, "acme", "prod");
        Assert.Equal(
            HttpStatusCode.OK,
            (await MachineGet(client, "acme", enrollmentId, machineId)).StatusCode);

        var revoke = await AuthPost(client, adminToken,
            $"/api/admin/policy-authority/machines/{machineId}/revoke", new { reason = "laptop stolen" });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await MachineGet(client, "acme", enrollmentId, machineId)).StatusCode);

        // A machine presenting another tenant or another enrollment (reassigned/copied identity)
        // is refused even though the machine ID itself is registered.
        var machine2 = Guid.NewGuid().ToString("N");
        var enrollment2 = Guid.NewGuid().ToString("N");
        await RegisterMachineAsync(client, adminToken, machine2, enrollment2, "acme", "prod");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await MachineGet(client, "globex", enrollment2, machine2)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await MachineGet(client, "acme", Guid.NewGuid().ToString("N"), machine2)).StatusCode);

        // Every refusal above left a durable audit record.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var denials = await db.AuditLogs.CountAsync(a => a.Action == "POLICY_ENVELOPE_DENIED");
        Assert.True(denials >= 5, $"Expected at least 5 audited denials, found {denials}.");
    }

    [Fact]
    public async Task ClientCertificate_IsRequiredAndThumbprintBound_WhenRegistered()
    {
        using var factory = new DistributionPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        await PublishAsync(client, adminToken, "1.0.0");

        using var expected = CreateSelfSignedCertificate("CN=ETL-SQL Machine");
        using var wrong = CreateSelfSignedCertificate("CN=ETL-SQL Impostor");
        var thumbprint = Convert.ToHexString(expected.GetCertHash(HashAlgorithmName.SHA256));

        var machineId = Guid.NewGuid().ToString("N");
        var enrollmentId = Guid.NewGuid().ToString("N");
        await RegisterMachineAsync(client, adminToken, machineId, enrollmentId, "acme", "prod", thumbprint);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await MachineGet(client, "acme", enrollmentId, machineId)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await MachineGet(client, "acme", enrollmentId, machineId, wrong)).StatusCode);

        var authorized = await MachineGet(client, "acme", enrollmentId, machineId, expected);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(
            await authorized.Content.ReadAsStringAsync(), Json);
        Assert.Equal("1.0.0", envelope!.PolicyVersion);
    }

    [Fact]
    public async Task RetrievalWithoutPublishedPolicy_Returns404_AndReRegistrationRequiresRevocation()
    {
        using var factory = new DistributionPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var machineId = Guid.NewGuid().ToString("N");
        var enrollmentId = Guid.NewGuid().ToString("N");
        await RegisterMachineAsync(client, adminToken, machineId, enrollmentId, "acme", "prod");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await MachineGet(client, "acme", enrollmentId, machineId)).StatusCode);

        // An active identity cannot be silently rebound to another tenant/environment.
        var rebind = await AuthPost(client, adminToken, "/api/admin/policy-authority/machines", new
        {
            machineId,
            enrollmentId = Guid.NewGuid().ToString("N"),
            tenant = "globex",
            environment = "prod"
        });
        Assert.Equal(HttpStatusCode.BadRequest, rebind.StatusCode);

        // After revocation the machine may be re-registered (fresh enrollment).
        await AuthPost(client, adminToken,
            $"/api/admin/policy-authority/machines/{machineId}/revoke", new { reason = "reimaged" });
        var newEnrollment = Guid.NewGuid().ToString("N");
        await RegisterMachineAsync(client, adminToken, machineId, newEnrollment, "globex", "prod");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await MachineGet(client, "acme", enrollmentId, machineId)).StatusCode);
    }

    [Fact]
    public async Task CanaryCohortMember_ReceivesCanary_WhileTheRestOfTheFleetStaysOnActive()
    {
        using var factory = new DistributionPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        // m1 is labelled into the canary group; m2 is not.
        var (m1, e1) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        var (m2, e2) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        await RegisterMachineAsync(client, adminToken, m1, e1, "acme", "prod", canaryGroup: "ring0");
        await RegisterMachineAsync(client, adminToken, m2, e2, "acme", "prod");

        await PublishAsync(client, adminToken, "1.0.0");
        await PublishCanaryViaServiceAsync(factory, "1.1.0-canary", CanaryCohort.ForGroup("ring0"));

        // The group member gets the canary; the unlabelled machine stays on the fleet-wide active.
        Assert.Equal("1.1.0-canary", await RetrievedVersionAsync(client, "acme", e1, m1));
        Assert.Equal("1.0.0", await RetrievedVersionAsync(client, "acme", e2, m2));
    }

    [Fact]
    public async Task PercentageCanaryAt100_ReachesTheMachine_ThenHaltRevertsIt()
    {
        using var factory = new DistributionPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);

        var (machineId, enrollmentId) = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        await RegisterMachineAsync(client, adminToken, machineId, enrollmentId, "acme", "prod");
        await PublishAsync(client, adminToken, "1.0.0");

        // A 100% canary includes every machine regardless of group label.
        await PublishCanaryViaServiceAsync(factory, "1.1.0-canary", CanaryCohort.ForPercentage(100));
        Assert.Equal("1.1.0-canary", await RetrievedVersionAsync(client, "acme", enrollmentId, machineId));

        // Halting re-issues the active document (later issuance) so the cohort machine reverts off the
        // canary on its next poll — it no longer receives 1.1.0-canary.
        await HaltCanaryViaServiceAsync(factory, "1.1.0-canary");
        var reverted = await RetrievedVersionAsync(client, "acme", enrollmentId, machineId);
        Assert.NotEqual("1.1.0-canary", reverted);
        Assert.StartsWith("1.0.0", reverted); // re-issued from the 1.0.0 active document
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static X509Certificate2 CreateSelfSignedCertificate(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static Task<HttpResponseMessage> MachineGet(
        HttpClient client, string tenant, string enrollmentId, string machineId,
        X509Certificate2? clientCertificate = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, EnvelopeUrl);
        request.Headers.Add(EnterprisePolicyTransport.TenantHeader, tenant);
        request.Headers.Add(EnterprisePolicyTransport.EnrollmentHeader, enrollmentId);
        request.Headers.Add(EnterprisePolicyTransport.MachineHeader, machineId);
        if (clientCertificate is not null)
            request.Headers.Add(CertForwardHeader,
                Convert.ToBase64String(clientCertificate.Export(X509ContentType.Cert)));
        return client.SendAsync(request);
    }

    private static async Task RegisterMachineAsync(
        HttpClient client, string adminToken, string machineId, string enrollmentId,
        string tenant, string environment, string? thumbprint = null, string? canaryGroup = null)
    {
        var response = await AuthPost(client, adminToken, "/api/admin/policy-authority/machines", new
        {
            machineId,
            enrollmentId,
            tenant,
            environment,
            clientCertificateThumbprint = thumbprint,
            canaryGroup
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> RetrievedVersionAsync(
        HttpClient client, string tenant, string enrollmentId, string machineId)
    {
        var response = await MachineGet(client, tenant, enrollmentId, machineId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(
            await response.Content.ReadAsStringAsync(), Json);
        return envelope!.PolicyVersion;
    }

    private static async Task PublishCanaryViaServiceAsync(
        PortalWebFactory factory, string version, CanaryCohort cohort)
    {
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PolicyAuthorityService>();
        var doc = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection { ApprovedRoots = [Path.GetTempPath().TrimEnd('\\', '/')] }
        };
        await svc.PublishCanaryAsync(doc, "acme", "prod", version, "admin", null,
            DateTimeOffset.UtcNow.AddDays(30), cohort);
    }

    private static async Task HaltCanaryViaServiceAsync(PortalWebFactory factory, string version)
    {
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PolicyAuthorityService>();
        await svc.HaltCanaryAsync("acme", "prod", version, "admin", null);
    }

    private static async Task PublishAsync(HttpClient client, string adminToken, string version)
    {
        var doc = new OrganizationPolicyDocument
        {
            Filesystem = new FilesystemPolicySection
            {
                ApprovedRoots = [Path.GetTempPath().TrimEnd('\\', '/')]
            }
        };
        var response = await AuthPost(client, adminToken, "/api/admin/policy-authority/publish", new
        {
            tenant = "acme",
            environment = "prod",
            policyVersion = version,
            policyJson = OrganizationPolicySchema.Serialize(doc),
            expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> GetSigningPublicKeyAsync(HttpClient client, string adminToken)
    {
        var status = await (await AuthGet(client, adminToken, "/api/admin/policy-authority/status"))
            .Content.ReadFromJsonAsync<JsonObject>(Json);
        Assert.True(status!["configured"]!.GetValue<bool>());
        return status["signingPublicKeyPem"]!.GetValue<string>();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var initial = await LoginAsync(client, "admin", "Admin@12345!");
        var change = await AuthPost(client, initial, "/api/auth/change-password", new
        {
            currentPassword = "Admin@12345!",
            newPassword = "Admin@Tests99!"
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        return await LoginAsync(client, "admin", "Admin@Tests99!");
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json);
        return body!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> AuthGet(HttpClient client, string token, string url) =>
        SendAsync(client, HttpMethod.Get, token, url, null);

    private static Task<HttpResponseMessage> AuthPost(HttpClient client, string token, string url, object body) =>
        SendAsync(client, HttpMethod.Post, token, url, body);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string token, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
