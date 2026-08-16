using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Orchestrator.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Legacy mode and the solo boundary.
///
/// <para>Legacy mode — no caller assertion, the API key as the only identity — is a supported Solo
/// configuration and a dangerous default anywhere else, because it is usually <i>inferred</i> from the
/// bind address rather than chosen. Two things follow, and both are tested here: the service has to
/// say which mode it is in, and it has to refuse the per-object grant model while it is in legacy
/// mode, since a grant there names a principal that does not exist and restricts a caller who already
/// passes every check.</para>
/// </summary>
[Trait("Category", "Portal")]
public sealed class OrchestratorLegacyModeTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string ApiKey = "test-orch-key-12345";
    private const string PrincipalKey = "9b1c77d2f4e84a1e8c0d6b3a5f27e410";

    public OrchestratorLegacyModeTests() => OrchestratorAuthorizationMode.ResetProxyObservationForTests();

    public void Dispose() => OrchestratorAuthorizationMode.ResetProxyObservationForTests();

    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in pairs) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // ── The mode, resolved ───────────────────────────────────────────────────────

    [Fact]
    public void ALoopbackServiceWithNothingConfiguredIsLegacyAndSaysItIsSoloOnly()
    {
        var mode = OrchestratorAuthorizationMode.Resolve(Config(("Orchestrator:ApiKey", "a-key")));

        Assert.Equal(OrchestratorAuthorizationModeKind.Legacy, mode.Kind);
        Assert.Equal("legacy", mode.Name);
        Assert.False(mode.ExplicitlyConfigured);
        // Nothing here contradicts Solo, so this is a statement of fact rather than a warning — but it
        // still has to be a statement, because the alternative is an operator inferring the mode from
        // the absence of a message.
        Assert.False(mode.RequiresOperatorAttention);
        Assert.Contains("LEGACY", mode.Describe(), StringComparison.Ordinal);
        Assert.Contains("Solo", mode.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void ANonLoopbackBindDefaultsToFederated()
    {
        var mode = OrchestratorAuthorizationMode.Resolve(Config(("urls", "http://0.0.0.0:5001")));

        Assert.Equal(OrchestratorAuthorizationModeKind.Federated, mode.Kind);
        Assert.False(mode.RequiresOperatorAttention);
        Assert.Empty(mode.SharedDeploymentSignals);
    }

    [Fact]
    public void LegacyModeTurnedOnOverANetworkBindIsCalledOut()
    {
        var mode = OrchestratorAuthorizationMode.Resolve(Config(
            ("Orchestrator:RequireFederatedIdentity", "false"),
            ("urls", "http://0.0.0.0:5001")));

        Assert.True(mode.IsLegacy);
        Assert.True(mode.ExplicitlyConfigured);
        Assert.True(mode.RequiresOperatorAttention);
        Assert.Contains("non-loopback", string.Join(" ", mode.SharedDeploymentSignals), StringComparison.Ordinal);
        // The message has to carry the way out, not just the diagnosis: turning federation on strands
        // everything this host already had until it is adopted.
        Assert.Contains("RequireFederatedIdentity=true", mode.Describe(), StringComparison.Ordinal);
        Assert.Contains("adopt", mode.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void APairedSigningSecretContradictsLegacyMode()
    {
        // The secret exists for one purpose and legacy mode ignores it: either a Portal was paired and
        // the mode was then turned off, or it was never turned on.
        var mode = OrchestratorAuthorizationMode.Resolve(Config(
            ("Orchestrator:RequireFederatedIdentity", "false"),
            ("Orchestrator:IdentitySigningSecret", "a-dedicated-test-identity-secret-at-least-32-bytes")));

        Assert.True(mode.RequiresOperatorAttention);
        Assert.Contains("identity-signing secret", string.Join(" ", mode.SharedDeploymentSignals), StringComparison.Ordinal);
    }

    [Fact]
    public void AConfiguredTenantContradictsLegacyMode()
    {
        var mode = OrchestratorAuthorizationMode.Resolve(Config(
            ("Orchestrator:RequireFederatedIdentity", "false"),
            ("Orchestrator:TenantId", "tenant-a")));

        Assert.True(mode.RequiresOperatorAttention);
        Assert.Contains("tenant", string.Join(" ", mode.SharedDeploymentSignals), StringComparison.Ordinal);
    }

    [Fact]
    public void AProxiedRequestIsTheSignalTheBindAddressCannotSee()
    {
        // The case in the roadmap: a shared Orchestrator behind a reverse proxy binds loopback, so the
        // configuration looks exactly like a laptop. The requests do not.
        var loopbackLegacy = Config(("Orchestrator:ApiKey", "a-key"));
        Assert.False(OrchestratorAuthorizationMode.Resolve(loopbackLegacy).RequiresOperatorAttention);

        var proxied = new DefaultHttpContext();
        proxied.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.True(OrchestratorAuthorizationMode.NoteProxiedRequest(proxied));
        // Latched, so the discovery is logged once rather than on every request that follows.
        Assert.False(OrchestratorAuthorizationMode.NoteProxiedRequest(proxied));

        var mode = OrchestratorAuthorizationMode.Resolve(loopbackLegacy);
        Assert.True(mode.RequiresOperatorAttention);
        Assert.Contains("reverse proxy", string.Join(" ", mode.SharedDeploymentSignals), StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectRequestIsNotMistakenForAProxiedOne()
    {
        Assert.False(OrchestratorAuthorizationMode.NoteProxiedRequest(new DefaultHttpContext()));
        Assert.False(OrchestratorAuthorizationMode.ProxiedRequestObserved);
    }

    // ── The mode, visible on the wire ────────────────────────────────────────────

    [Fact]
    public async Task HealthReportsLegacyModeToAnUnauthenticatedProbe()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: false);
        using var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<JsonObject>("/health", Json);

        Assert.Equal("Healthy", health!["status"]!.GetValue<string>());
        Assert.Equal("legacy", health["authorizationMode"]!.GetValue<string>());
        Assert.False(health["requiresCallerIdentity"]!.GetValue<bool>());
        // A loopback test host is a plausible Solo box, so the probe reports the mode without alarm.
        Assert.False(health["legacyModeOnSharedDeployment"]!.GetValue<bool>());
    }

    [Fact]
    public async Task HealthReportsFederatedModeWhenCallersAreIdentified()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: true);
        using var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<JsonObject>("/health", Json);

        Assert.Equal("federated", health!["authorizationMode"]!.GetValue<string>());
        Assert.True(health["requiresCallerIdentity"]!.GetValue<bool>());
        Assert.False(health["legacyModeOnSharedDeployment"]!.GetValue<bool>());
    }

    /// <summary>
    /// The health endpoint names the mode and stops there. The evidence behind the warning names
    /// listen addresses, and an unauthenticated probe has no business reading those.
    /// </summary>
    [Fact]
    public async Task HealthDoesNotPublishTheEvidenceBehindTheWarning()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: false);
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/health");

        Assert.DoesNotContain("IdentitySigningSecret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("urls", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── The solo boundary ────────────────────────────────────────────────────────

    [Fact]
    public async Task LegacyModeRefusesToWriteAGrantAndWritesNothing()
    {
        // Without this the API key — which already passes every object decision — could write grants
        // naming principals that exist nowhere: a second identity model inside the Orchestrator, which
        // is exactly what routing identity through the Portal exists to prevent.
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: false);
        using var client = factory.CreateClient();
        await CreateJobAsync(client, "legacy_grant_refused");

        using var attempt = await SendAsync(
            client, HttpMethod.Put, $"/api/authorization/JOB/legacy_grant_refused/USER/{PrincipalKey}",
            new { permission = "MANAGE" });

        Assert.Equal(HttpStatusCode.Conflict, attempt.StatusCode);
        var error = (await attempt.Content.ReadFromJsonAsync<JsonObject>(Json))!["error"]!.GetValue<string>();
        Assert.Contains("legacy mode", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequireFederatedIdentity=true", error, StringComparison.Ordinal);

        var store = (IOrchestratorAuthorizationStore)factory.Services.GetService(typeof(IOrchestratorAuthorizationStore))!;
        var jobs = (IJobHistoryStore)factory.Services.GetService(typeof(IJobHistoryStore))!;
        var job = await jobs.GetJobAsync((string?)null, "legacy_grant_refused");
        Assert.Empty(await store.GetObjectGrantsAsync(job!.Id.ToString(), default));
    }

    [Theory]
    [InlineData("GET", "/api/authorization/JOB/legacy_surface")]
    [InlineData("DELETE", "/api/authorization/JOB/legacy_surface/USER/" + PrincipalKey)]
    [InlineData("GET", "/api/authorization/unowned")]
    public async Task TheWholeGrantSurfaceIsRefusedInLegacyMode(string method, string path)
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: false);
        using var client = factory.CreateClient();
        await CreateJobAsync(client, "legacy_surface");

        using var response = await SendAsync(client, new HttpMethod(method), path);

        // Not 403: the caller is not being told they lack authority, they are being told the model does
        // not exist here. "No grants" and "grants do not apply" are the same empty list otherwise.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task LegacyModeRefusesOwnershipReassignmentAndAdoption()
    {
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: false);
        using var client = factory.CreateClient();
        await CreateJobAsync(client, "legacy_owner_refused");

        using (var setOwner = await SendAsync(
            client, HttpMethod.Put, "/api/authorization/JOB/legacy_owner_refused/owner",
            new { principalKind = "USER", principalId = PrincipalKey }))
            Assert.Equal(HttpStatusCode.Conflict, setOwner.StatusCode);

        using (var adopt = await SendAsync(
            client, HttpMethod.Post, "/api/authorization/adopt",
            new { principalKind = "USER", principalId = PrincipalKey }))
            Assert.Equal(HttpStatusCode.Conflict, adopt.StatusCode);
    }

    [Fact]
    public async Task LegacyModeStillRunsTheJobsItOwns()
    {
        // The refusal is scoped to the grant model. A Solo box is a supported deployment, not a
        // degraded one, and everything else has to keep working.
        using var factory = new OrchestratorWebFactory(requireFederatedIdentity: false);
        using var client = factory.CreateClient();
        await CreateJobAsync(client, "legacy_still_works");

        using var listed = await SendAsync(client, HttpMethod.Get, "/api/scheduled-jobs");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
    }

    private static async Task CreateJobAsync(HttpClient client, string name)
    {
        using var create = await SendAsync(client, HttpMethod.Post, "/api/scheduled-jobs", new
        {
            name,
            scriptText = "SELECT 1 AS Value;",
            interval = 100,
            unit = "DAY"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Orchestrator-Key", ApiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
