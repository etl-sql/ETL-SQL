using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class ServiceAccountIntegrationTests
{
    [Fact]
    public async Task ScheduledOwnerLookup_IsScopedAndReadsCurrentMembership()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var adminId = await GetAdminIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var owner = new PortalUser { UserName = "scheduled-owner", NormalizedUserName = "SCHEDULED-OWNER" };
        var group = new Group { Name = "scheduled-finance" };
        var foreign = new PortalUser
        {
            UserName = "foreign-owner",
            NormalizedUserName = "FOREIGN-OWNER",
            TenantId = "tenant-other"
        };
        var membership = new UserGroup { User = owner, Group = group };
        db.AddRange(owner, foreign, group, membership);
        await db.SaveChangesAsync();
        var ownerKey = $"user:{owner.PrincipalKey}";

        var denied = await SendAsync(client, HttpMethod.Post, ScheduledToolPrincipalRequest.Path,
            adminToken, new ScheduledToolPrincipalRequest(ownerKey));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var reader = await CreateAsync(client, adminToken, adminId, "ordinary-reader");
        var readerToken = await ExchangeAsync(client, reader.ClientId, reader.Secret);
        var wrongScope = await SendAsync(client, HttpMethod.Post, ScheduledToolPrincipalRequest.Path,
            readerToken, new ScheduledToolPrincipalRequest(ownerKey));
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);

        var directory = await CreateAsync(client, adminToken, adminId, "scheduled-owner-lookup",
            ScheduledToolPrincipalRequest.Scope);
        var directoryToken = await ExchangeAsync(client, directory.ClientId, directory.Secret);

        async Task<ToolExecutionPrincipal?> ResolveAsync(string tenant, string key)
        {
            var inquiry = new ScheduledToolPrincipalRequest(key);
            using var response = await SendAsync(client, HttpMethod.Post, ScheduledToolPrincipalRequest.Path,
                directoryToken, inquiry);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var principal = await response.Content.ReadFromJsonAsync<ScheduledToolPrincipalResponse>();
            return principal?.ForOwner(tenant, inquiry);
        }

        var live = await ResolveAsync("portal-host", ownerKey);
        Assert.NotNull(live);
        Assert.Equal(group.PrincipalKey, Assert.Single(live.GroupIds));
        Assert.Null(await ResolveAsync("portal-host", $"user:{foreign.PrincipalKey}"));
        Assert.Null(await ResolveAsync("tenant-other", ownerKey));

        var executor = await CreateAsync(client, adminToken, adminId, "scheduled-executor",
            ServiceAccountScopes.OrchestratorExecute);
        var service = await ResolveAsync("portal-host", $"service:{executor.Id}");
        Assert.NotNull(service);
        Assert.Equal(ToolPrincipalKind.Service, service.Kind);
        Assert.Empty(service.GroupIds);
        Assert.Null(await ResolveAsync("portal-host", $"service:{reader.Id}"));
        var revokedAccount = await db.ServiceAccounts.SingleAsync(account => account.Id == executor.Id);
        revokedAccount.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Null(await ResolveAsync("portal-host", $"service:{executor.Id}"));

        db.UserGroups.Remove(membership);
        await db.SaveChangesAsync();
        var removed = await ResolveAsync("portal-host", ownerKey);
        Assert.NotNull(removed);
        Assert.Empty(removed.GroupIds);

        owner.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await ResolveAsync("portal-host", ownerKey));

        var issuance = await SendAsync(client, HttpMethod.Post, "/api/auth/orchestrator-assertion", directoryToken);
        Assert.Equal(HttpStatusCode.Forbidden, issuance.StatusCode);
    }

    [Fact]
    public async Task CiWorkloadExchange_IsSecretlessShortLivedResourceBoundAndAudited()
    {
        using var factory = new WorkloadPortalFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var provisioned = await CreateAsync(client, adminToken, ownerId, "ci-reader");
        var config = factory.Services.GetRequiredService<PortalConfig>();
        config.Identity.WorkloadIdentity.Bindings[0] =
            config.Identity.WorkloadIdentity.Bindings[0] with { ServiceAccountClientId = provisioned.ClientId };

        var assertion = factory.CreateAssertion();
        var exchange = await client.PostAsJsonAsync("/api/auth/workload-token", new
        {
            subjectToken = assertion,
            audience = "etl-sql-ci",
            resource = "/api/folders",
            operation = "portal.read"
        });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var body = (await exchange.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.InRange(body["expiresIn"]!.GetValue<int>(), 60, 900);
        var token = body["accessToken"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Get, "/api/reports", token)).StatusCode);

        var replay = await client.PostAsJsonAsync("/api/auth/workload-token", new
        {
            subjectToken = assertion,
            audience = "etl-sql-ci",
            resource = "/api/folders",
            operation = "portal.read"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        config.Identity.WorkloadIdentity.Bindings[0] =
            config.Identity.WorkloadIdentity.Bindings[0] with { TenantId = "tenant-other" };
        var crossTenant = await client.PostAsJsonAsync("/api/auth/workload-token", new
        {
            subjectToken = factory.CreateAssertion(),
            audience = "etl-sql-ci",
            resource = "/api/folders",
            operation = "portal.read"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, crossTenant.StatusCode);

        config.Identity.WorkloadIdentity.Bindings[0] =
            config.Identity.WorkloadIdentity.Bindings[0] with
            {
                TenantId = "portal-host",
                RequireApproval = true
            };
        var selfApproval = await SendAsync(client, HttpMethod.Post,
            "/api/admin/workload-identity/approvals", adminToken, new
            {
                bindingId = "ci-main",
                resource = "/api/folders",
                operation = "portal.read"
            });
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
        var bypass = await client.PostAsJsonAsync("/api/auth/workload-token", new
        {
            subjectToken = factory.CreateAssertion(),
            audience = "etl-sql-ci",
            resource = "/api/folders",
            operation = "portal.read"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, bypass.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(value =>
            value.Action == "WORKLOAD_IDENTITY_TOKEN_ISSUED"
            && value.ActorType == "ExternalWorkload"
            && value.ActorId == "ci-main"
            && value.Detail!.Contains("Resource=/api/folders")));
        Assert.True(await db.AuditLogs.AnyAsync(value =>
            value.Action == "WORKLOAD_IDENTITY_EXCHANGE_DENIED"
            && value.Detail!.Contains("workload_replay_rejected")));
    }
    [Fact]
    public async Task IdentityAutomation_CanDelegateOnlyItsCurrentAuthorityForItsOwner()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var provisionerResponse = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts",
            adminToken, new
            {
                name = "identity-provisioner",
                ownerUserId = ownerId,
                scopes = new[] { "admin.identity" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, provisionerResponse.StatusCode);
        var provisionerBody = (await provisionerResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        var provisioner = provisionerBody["account"]!.AsObject();
        var serviceToken = await ExchangeAsync(client,
            provisioner["clientId"]!.GetValue<string>(),
            provisionerBody["clientSecret"]!.GetValue<string>());

        var delegated = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts",
            serviceToken, new
            {
                name = "delegated-identity",
                ownerUserId = ownerId,
                scopes = new[] { "admin.identity" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, delegated.StatusCode);

        var broadened = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts",
            serviceToken, new
            {
                name = "scope-escalation",
                ownerUserId = ownerId,
                scopes = new[] { "admin.identity", "portal.read" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Forbidden, broadened.StatusCode);

        var strongerResponse = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts",
            adminToken, new
            {
                name = "stronger-sibling",
                ownerUserId = ownerId,
                scopes = new[] { "admin.identity", "portal.read" },
                roles = new[] { "Admin" },
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        var stronger = (await strongerResponse.Content.ReadFromJsonAsync<JsonObject>())!["account"]!.AsObject();
        var stealSecret = await SendAsync(client, HttpMethod.Post,
            $"/api/admin/service-accounts/{stronger["id"]}/rotate-secret", serviceToken, version: 1);
        Assert.Equal(HttpStatusCode.Forbidden, stealSecret.StatusCode);
    }

    [Fact]
    public async Task ProvisionAndExchange_ExposeSecretOnce_EnforceScopes_AndAttributeAudit()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);

        var created = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name = "report-reader",
                description = "integration test",
                ownerUserId = ownerId,
                scopes = new[] { "portal.read" },
                roles = Array.Empty<string>(),
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var secret = body["clientSecret"]!.GetValue<string>();
        var account = body["account"]!.AsObject();
        var accountId = account["id"]!.GetValue<string>();
        var clientId = account["clientId"]!.GetValue<string>();
        Assert.StartsWith("sas_", secret, StringComparison.Ordinal);

        var listed = await SendAsync(client, HttpMethod.Get, "/api/admin/service-accounts", adminToken);
        var listText = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, listText, StringComparison.Ordinal);
        Assert.DoesNotContain("secretHash", listText, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var stored = await db.ServiceAccounts.SingleAsync(value => value.Id == accountId);
            Assert.NotEqual(secret, stored.SecretHash);
            Assert.DoesNotContain(secret, stored.SecretHash, StringComparison.Ordinal);
        }

        var serviceToken = await ExchangeAsync(client, clientId, secret);
        var folders = await SendAsync(client, HttpMethod.Get, "/api/folders", serviceToken);
        Assert.Equal(HttpStatusCode.OK, folders.StatusCode);
        var adminDenied = await SendAsync(client, HttpMethod.Get, "/api/admin/service-accounts", serviceToken);
        Assert.Equal(HttpStatusCode.Forbidden, adminDenied.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var audit = await db.AuditLogs.SingleAsync(value =>
                value.Action == "SERVICE_ACCOUNT_TOKEN_ISSUED" && value.ActorId == accountId);
            Assert.Equal("ServiceAccount", audit.ActorType);
            Assert.Equal("portal.read", audit.EffectiveScopes);
            Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
            Assert.DoesNotContain(secret, audit.Detail ?? "", StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RotateExpireAndRevoke_InvalidateCredentialsAndIssuedTokens()
    {
        using var factory = new PortalWebFactory();
        using var client = factory.CreateClient();
        var adminToken = await GetAdminTokenAsync(client);
        var ownerId = await GetAdminIdAsync(factory);
        var provisioned = await CreateAsync(client, adminToken, ownerId, "rotating-reader");
        var originalToken = await ExchangeAsync(client, provisioned.ClientId, provisioned.Secret);

        var rotated = await SendAsync(client, HttpMethod.Post,
            $"/api/admin/service-accounts/{provisioned.Id}/rotate-secret", adminToken, version: 1);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newSecret = (await rotated.Content.ReadFromJsonAsync<JsonObject>())!["clientSecret"]!.GetValue<string>();
        Assert.NotEqual(provisioned.Secret, newSecret);
        var staleRotation = await SendAsync(client, HttpMethod.Post,
            $"/api/admin/service-accounts/{provisioned.Id}/rotate-secret", adminToken, version: 1);
        Assert.Equal(HttpStatusCode.Conflict, staleRotation.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/service-token",
                new { clientId = provisioned.ClientId, clientSecret = provisioned.Secret })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", originalToken)).StatusCode);
        var rotatedToken = await ExchangeAsync(client, provisioned.ClientId, newSecret);

        var disabled = await SendAsync(client, HttpMethod.Put,
            $"/api/admin/service-accounts/{provisioned.Id}", adminToken,
            new { isEnabled = false, expiresAt = DateTime.UtcNow.AddHours(1), scopes = new[] { "portal.read" } },
            version: 2);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/service-token",
                new { clientId = provisioned.ClientId, clientSecret = newSecret })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", rotatedToken)).StatusCode);

        var enabled = await SendAsync(client, HttpMethod.Put,
            $"/api/admin/service-accounts/{provisioned.Id}", adminToken,
            new { isEnabled = true, expiresAt = DateTime.UtcNow.AddHours(1), scopes = new[] { "portal.read" } },
            version: 3);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        rotatedToken = await ExchangeAsync(client, provisioned.ClientId, newSecret);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var row = await db.ServiceAccounts.SingleAsync(value => value.Id == provisioned.Id);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/service-token",
                new { clientId = provisioned.ClientId, clientSecret = newSecret })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", rotatedToken)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var row = await db.ServiceAccounts.SingleAsync(value => value.Id == provisioned.Id);
            row.ExpiresAt = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }
        var currentToken = await ExchangeAsync(client, provisioned.ClientId, newSecret);
        var revoked = await SendAsync(client, HttpMethod.Post,
            $"/api/admin/service-accounts/{provisioned.Id}/revoke", adminToken, version: 4);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await SendAsync(client, HttpMethod.Get, "/api/folders", currentToken)).StatusCode);
    }

    private static async Task<(string Id, string ClientId, string Secret)> CreateAsync(
        HttpClient client, string adminToken, int ownerId, string name, string requestedScope = ServiceAccountScopes.PortalRead)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/service-accounts", adminToken,
            new
            {
                name,
                ownerUserId = ownerId,
                scopes = new[] { requestedScope },
                roles = Array.Empty<string>(),
                expiresAt = DateTime.UtcNow.AddHours(1)
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var account = body["account"]!.AsObject();
        return (account["id"]!.GetValue<string>(), account["clientId"]!.GetValue<string>(),
            body["clientSecret"]!.GetValue<string>());
    }

    private static async Task<string> ExchangeAsync(HttpClient client, string clientId, string secret)
    {
        var response = await client.PostAsJsonAsync("/api/auth/service-token",
            new { clientId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]!.GetValue<string>();
    }

    private static async Task<int> GetAdminIdAsync(PortalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        return await db.Users.Where(value => value.UserName == "admin").Select(value => value.Id).SingleAsync();
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@12345!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        var change = await SendAsync(client, HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = "Admin@12345!", newPassword = "Admin@Service99!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        var relogin = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@Service99!" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        return (await relogin.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, object? body = null, long? version = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (version.HasValue) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version.Value}\"");
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private sealed class WorkloadPortalFactory : PortalWebFactory
    {
        private readonly RSA rsa = RSA.Create(2048);

        protected override void CustomizePortalConfig(PortalConfig config)
        {
            config.Identity.WorkloadIdentity = new WorkloadIdentityConfig
            {
                Enabled = true,
                ClockSkewSeconds = 30,
                MaximumAssertionLifetimeSeconds = 600,
                Bindings = [new WorkloadIdentityBindingConfig
                {
                    Id = "ci-main",
                    Provider = "private_key_jwt",
                    ServiceAccountClientId = "pending",
                    TenantId = "portal-host",
                    Issuer = "https://ci.example.test",
                    Subject = "repo:etl-sql/ETL-SQL:ref:refs/heads/main",
                    Audience = "etl-sql-ci",
                    Resource = "/api/folders",
                    Operations = ["portal.read"]
                }]
            };
        }

        protected override void CustomizeServices(IServiceCollection services)
        {
            services.RemoveAll<IWorkloadIdentitySigningKeyProvider>();
            services.AddSingleton<IWorkloadIdentitySigningKeyProvider>(new StaticWorkloadKeys(rsa));
        }

        public string CreateAssertion()
        {
            var now = DateTime.UtcNow;
            return new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
            {
                Issuer = "https://ci.example.test",
                Audience = "etl-sql-ci",
                Subject = new ClaimsIdentity([
                    new Claim(JwtRegisteredClaimNames.Sub, "repo:etl-sql/ETL-SQL:ref:refs/heads/main"),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
                ]),
                IssuedAt = now,
                NotBefore = now.AddSeconds(-1),
                Expires = now.AddMinutes(5),
                SigningCredentials = new(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) rsa.Dispose();
        }
    }

    private sealed class StaticWorkloadKeys(RSA rsa) : IWorkloadIdentitySigningKeyProvider
    {
        public Task<IEnumerable<SecurityKey>> GetAsync(WorkloadIdentityBindingConfig binding, CancellationToken ct) =>
            Task.FromResult<IEnumerable<SecurityKey>>([new RsaSecurityKey(rsa)]);
    }
}
