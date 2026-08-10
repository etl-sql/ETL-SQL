using System.Security.Claims;
using System.Text.Json;
using ETL_SQL.Core.Security;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class DedicatedPolicyAuthorityGuardTests
{
    [Fact]
    public void DedicatedHostAcceptsOnlyItsConfiguredTenant()
    {
        var guard = new DedicatedPolicyAuthorityGuard(new PortalConfig { TenantId = "tenant-alpha" });
        var tenantAdmin = Principal(new Claim(ClaimTypes.Role, "Admin"));

        Assert.Equal("tenant-alpha", guard.AuthorizeRead(null));
        Assert.Equal("tenant-alpha", guard.AuthorizeMutation(tenantAdmin, "tenant-alpha"));
        Assert.Throws<UnauthorizedAccessException>(() => guard.AuthorizeRead("tenant-beta"));
    }

    [Fact]
    public void PlatformScopeCannotMutateDedicatedTenantPolicy()
    {
        var guard = new DedicatedPolicyAuthorityGuard(new PortalConfig { TenantId = "tenant-alpha" });
        var platform = Principal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(DedicatedPolicyAuthorityGuard.AuthorityScopeClaim,
                DedicatedPolicyAuthorityGuard.PlatformScope));

        var error = Assert.Throws<UnauthorizedAccessException>(() =>
            guard.AuthorizeMutation(platform, "tenant-alpha"));

        Assert.Contains("Platform scope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionArtifactCarriesNeitherProviderBindingNorResolvedMaterial()
    {
        var secret = Convert.ToBase64String(Enumerable.Repeat((byte)29, 32).ToArray());
        var environmentName = "ETLSQL_TENANT_ALPHA_DATASET_KEY";
        var provider = new EnvironmentKeyMaterialProvider(
        [
            new EnvironmentKeyMaterialBinding(environmentName,
                new("environment", "tenant-alpha-dataset", "tenant-alpha",
                    KeyPurpose.Dataset, "v1"))
        ], _ => secret);
        using (var lease = await provider.ResolveAsync(new("tenant-alpha", KeyPurpose.Dataset)))
            Assert.Equal(32, lease.Bytes.Length);

        var executionArtifact = JsonSerializer.Serialize(new ExecutionJob(
            "job-1", ReportId: 7, UserId: 3, ActorType: "TenantServiceAccount"));

        Assert.DoesNotContain(secret, executionArtifact, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentName, executionArtifact, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-alpha-dataset", executionArtifact, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
}
