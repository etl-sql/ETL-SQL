using System.Security.Cryptography;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace ETL_SQL.Portal.BrowserTests;

public sealed class PolicyAuthorityBrowserFactory : PortalBrowserFactory
{
    protected override void CustomizePortalConfig(PortalConfig config) => config.TenantId = "policy-browser";

    protected override void CustomizeServices(IServiceCollection services)
    {
        services.RemoveAll<IPolicyEnvelopeSigner>();
        services.AddSingleton<IPolicyEnvelopeSigner>(_ => new RsaPolicyEnvelopeSigner(RSA.Create(2048)));
    }
}

public sealed class PolicyAuthorityBrowserFixture() : PortalBrowserFixture(new PolicyAuthorityBrowserFactory());

[CollectionDefinition(Name)]
public sealed class PolicyAuthorityBrowserCollection : ICollectionFixture<PolicyAuthorityBrowserFixture>
{
    public const string Name = "Policy authority browser journeys";
}

[Trait("Category", "Browser")]
[Collection(PolicyAuthorityBrowserCollection.Name)]
public sealed class PolicyAuthorityPageTests(PolicyAuthorityBrowserFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<string> OpenScopeAsync(IPage page)
    {
        await fixture.SignInAsync(page);
        await page.GotoAsync("/admin.html");
        await page.Locator("[data-tab='policy']").ClickAsync();
        await Expect(page.Locator("#pa-status")).ToHaveTextAsync("Configured");
        var environment = $"browser-{Guid.NewGuid():N}";
        await page.Locator("#pa-tenant").FillAsync("policy-browser");
        await page.Locator("#pa-env").FillAsync(environment);
        await page.Locator("#pa-refreshBtn").ClickAsync();
        await Expect(page.Locator("#panel-policy")).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(page.Locator("#pa-versionWrap")).ToContainTextAsync("No policy versions");
        return environment;
    }

    [Fact]
    public async Task PolicyAuthority_RealSignedPublishStageCanaryHaltPromoteAndRollback()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        var environment = await OpenScopeAsync(page);
        await page.Locator("#pa-validateBtn").ClickAsync();
        await Expect(page.Locator("#pa-validateResult")).ToHaveTextAsync("Valid");
        ILocator Row(string version) => page.Locator($"#pa-versionWrap tbody tr[data-version='{version}']");
        await page.Locator("#pa-version").FillAsync("1.0.0");
        await page.Locator("#pa-publishBtn").ClickAsync();
        await Expect(Row("1.0.0").Locator("td").Nth(1)).ToHaveTextAsync("Active");
        await page.Locator("#pa-staged").SelectOptionAsync("true");
        await page.Locator("#pa-version").FillAsync("1.1.0");
        await page.Locator("#pa-publishBtn").ClickAsync();
        await Expect(Row("1.1.0").Locator("td").Nth(1)).ToHaveTextAsync("Staged");
        await Row("1.1.0").GetByRole(AriaRole.Button, new() { Name = "Activate", Exact = true }).ClickAsync();
        await Expect(Row("1.1.0").Locator("td").Nth(1)).ToHaveTextAsync("Active");
        await page.Locator("#pa-canaryVersion").FillAsync("1.2.0-canary");
        await page.Locator("#pa-publishCanaryBtn").ClickAsync();
        await Expect(Row("1.2.0-canary").Locator("td").Nth(1)).ToHaveTextAsync("Canary");
        await Row("1.2.0-canary").GetByRole(AriaRole.Button, new() { Name = "Halt", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Halt canary", Exact = true }).ClickAsync();
        await Expect(Row("1.2.0-canary").Locator("td").Nth(1)).ToHaveTextAsync("Rolled back");
        await Expect(page.Locator("#pa-actionResult")).ToContainTextAsync("Canary halted");
        await page.Locator("#pa-canaryVersion").FillAsync("1.3.0-canary");
        await page.Locator("#pa-cohortType").SelectOptionAsync("group");
        await page.Locator("#pa-canaryGroup").FillAsync("ring0");
        await page.Locator("#pa-publishCanaryBtn").ClickAsync();
        await Expect(Row("1.3.0-canary").Locator("td").Nth(1)).ToHaveTextAsync("Canary");
        await Expect(Row("1.3.0-canary").Locator("td").Nth(2)).ToContainTextAsync("group: ring0");
        await Row("1.3.0-canary").GetByRole(AriaRole.Button, new() { Name = "Promote", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Promote canary", Exact = true }).ClickAsync();
        await Expect(Row("1.3.0-canary").Locator("td").Nth(1)).ToHaveTextAsync("Active");
        await Row("1.0.0").GetByRole(AriaRole.Button, new() { Name = "Rollback to", Exact = true }).ClickAsync();
        await page.GetByLabel("New policy version", new() { Exact = true }).FillAsync("2.0.0-rollback");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create rollback", Exact = true }).ClickAsync();
        await Expect(Row("2.0.0-rollback").Locator("td").Nth(1)).ToHaveTextAsync("Active");
        await Expect(Row("1.3.0-canary").Locator("td").Nth(1)).ToHaveTextAsync("Rolled back");
        var json = await page.EvaluateAsync<string>("""
            async environment => {
              const active = await fetch('/api/admin/policy-authority/active?tenant=policy-browser&environment=' + encodeURIComponent(environment)).then(response => response.json());
              const status = await fetch('/api/admin/policy-authority/status').then(response => response.json());
              return JSON.stringify({ active, status });
            }
            """, environment);
        using var document = JsonDocument.Parse(json);
        var version = document.RootElement.GetProperty("active").GetProperty("version");
        Assert.Equal("2.0.0-rollback", version.GetProperty("policyVersion").GetString());
        var envelope = JsonSerializer.Deserialize<SignedOrganizationPolicyEnvelope>(
            document.RootElement.GetProperty("active").GetProperty("signedEnvelopeJson").GetString()!, Json)!;
        var enrollment = new EnterpriseEnrollmentDocument
        {
            Tenant = "policy-browser",
            PolicyEndpoint = "https://policy.example.test/etl-sql",
            PolicySigningPublicKey = document.RootElement.GetProperty("status").GetProperty("signingPublicKeyPem").GetString()!
        };
        Assert.NotNull(EnterprisePolicySignature.VerifyAndParse(envelope, enrollment, DateTimeOffset.UtcNow));
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var actions = await db.AuditLogs.Where(log => log.ResourceId == $"policy-browser/{environment}")
            .Select(log => log.Action).ToListAsync();
        foreach (var action in new[] { "PUBLISH_ORG_POLICY", "ACTIVATE_ORG_POLICY", "PUBLISH_CANARY_POLICY",
            "HALT_CANARY_POLICY", "PROMOTE_CANARY_POLICY", "ROLLBACK_ORG_POLICY" }) Assert.Contains(action, actions);
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task PolicyAuthority_RealMachineRegistrationRevocationAuditAndTenantRefusal()
    {
        await using var session = await fixture.NewSessionAsync();
        var page = session.Page;
        await OpenScopeAsync(page);
        var machineId = Guid.NewGuid().ToString("N");
        var enrollmentId = Guid.NewGuid().ToString("N");
        await page.Locator("#pa-machineId").FillAsync(machineId);
        await page.Locator("#pa-enrollmentId").FillAsync(enrollmentId);
        await page.Locator("#pa-thumbprint").FillAsync(new string('a', 40));
        await page.Locator("#pa-registerMachineBtn").ClickAsync();
        var row = page.Locator($"#pa-machineWrap tr[data-machine='{machineId}']");
        await Expect(row).ToContainTextAsync("Required");
        await Expect(page.Locator("#pa-machineId")).ToHaveValueAsync("");
        await row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true }).ClickAsync();
        await page.GetByLabel("Revocation reason", new() { Exact = true }).FillAsync("Browser lifecycle verification");
        await page.GetByRole(AriaRole.Button, new() { Name = "Revoke machine", Exact = true }).ClickAsync();
        await Expect(row).ToContainTextAsync("Revoked");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Revoke", Exact = true })).ToHaveCountAsync(0);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var actions = await db.AuditLogs.Where(log => log.ResourceId == machineId).Select(log => log.Action).ToListAsync();
        Assert.Contains("REGISTER_POLICY_MACHINE", actions); Assert.Contains("REVOKE_POLICY_MACHINE", actions);
        var deniedStatus = await page.EvaluateAsync<int>("async () => (await fetch('/api/admin/policy-authority/versions?tenant=other-tenant&environment=prod')).status");
        Assert.Equal(403, deniedStatus);
        await page.Locator("#pa-tenant").FillAsync("other-tenant");
        await Expect(page.Locator("#pa-machineWrap tbody tr")).ToHaveCountAsync(0);
        await page.Locator("#pa-loadMachinesBtn").ClickAsync();
        await Expect(page.Locator("#pa-machineWrap")).ToContainTextAsync("unavailable or incomplete");
        await Expect(page.Locator("#pa-registerMachineBtn")).ToBeDisabledAsync();
        Assert.Empty(session.PageErrors);
    }
}
