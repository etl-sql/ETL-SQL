using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ETL_SQL.Portal.Services;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class WorkloadIdentityFederationTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly DateTime _now = DateTime.UtcNow;

    [Theory]
    [InlineData("github", "https://token.actions.githubusercontent.com")]
    [InlineData("gitlab", "https://gitlab.example.test")]
    [InlineData("azure_devops", "https://vstoken.dev.azure.com/example-org")]
    [InlineData("private_key_jwt", "https://scheduler.example.test")]
    public async Task ProvidersUseTheSameExactPolicyContract(string provider, string issuer)
    {
        var binding = Binding(provider, issuer);
        var service = Service(binding);

        var validated = await service.ValidateAsync(Token(binding), binding.Audience,
            binding.Resource, "reports.execute", "APR-42");

        Assert.Equal(binding.Id, validated.Binding.Id);
        Assert.Equal("token-1", validated.TokenId);
    }

    [Theory]
    [InlineData("repo:other/project:ref:refs/heads/main", "etl-sql-ci", "report:quarterly", "reports.execute", "APR-42")]
    [InlineData("repo:etl-sql/ETL-SQL:ref:refs/heads/main", "other-audience", "report:quarterly", "reports.execute", "APR-42")]
    [InlineData("repo:etl-sql/ETL-SQL:ref:refs/heads/main", "etl-sql-ci", "report:other", "reports.execute", "APR-42")]
    [InlineData("repo:etl-sql/ETL-SQL:ref:refs/heads/main", "etl-sql-ci", "report:quarterly", "orchestrator.admin", "APR-42")]
    [InlineData("repo:etl-sql/ETL-SQL:ref:refs/heads/main", "etl-sql-ci", "report:quarterly", "reports.execute", "APR-WRONG")]
    public async Task SubjectAudienceResourceOperationAndApprovalMismatchFailClosed(
        string subject, string audience, string resource, string operation, string approval)
    {
        var binding = Binding("private_key_jwt", "https://scheduler.example.test");
        var service = Service(binding);
        var assertionBinding = binding with { Subject = subject, Audience = audience };

        var error = await Assert.ThrowsAsync<WorkloadIdentityException>(() => service.ValidateAsync(
            Token(assertionBinding), binding.Audience, resource, operation, approval));

        Assert.Contains(error.Code, new[] { "workload_policy_denied", "workload_approval_required" });
    }

    [Fact]
    public async Task AssertionIsSingleUseAndLifetimeIsCapped()
    {
        var binding = Binding("private_key_jwt", "https://scheduler.example.test");
        var service = Service(binding);
        var token = Token(binding);
        await service.ValidateAsync(token, binding.Audience, binding.Resource, "reports.execute", "APR-42");

        var replay = await Assert.ThrowsAsync<WorkloadIdentityException>(() => service.ValidateAsync(
            token, binding.Audience, binding.Resource, "reports.execute", "APR-42"));
        Assert.Equal("workload_replay_rejected", replay.Code);

        var longLived = Token(binding, expires: _now.AddMinutes(11), tokenId: "long-lived");
        var lifetime = await Assert.ThrowsAsync<WorkloadIdentityException>(() => service.ValidateAsync(
            longLived, binding.Audience, binding.Resource, "reports.execute", "APR-42"));
        Assert.Equal("invalid_workload_lifetime", lifetime.Code);
    }

    [Fact]
    public async Task DisabledBindingAndRotatedKeyFailClosed()
    {
        var binding = Binding("private_key_jwt", "https://scheduler.example.test");
        var disabled = Service(binding with { Enabled = false });
        var revoked = await Assert.ThrowsAsync<WorkloadIdentityException>(() => disabled.ValidateAsync(
            Token(binding), binding.Audience, binding.Resource, "reports.execute", "APR-42"));
        Assert.Equal("workload_policy_denied", revoked.Code);

        using var replacement = RSA.Create(2048);
        var rotated = Service(binding, replacement);
        var oldKey = await Assert.ThrowsAsync<WorkloadIdentityException>(() => rotated.ValidateAsync(
            Token(binding, tokenId: "old-key"), binding.Audience, binding.Resource,
            "reports.execute", "APR-42"));
        Assert.Equal("invalid_workload_assertion", oldKey.Code);
    }

    private WorkloadIdentityFederationService Service(WorkloadIdentityBindingConfig binding, RSA? validationKey = null)
    {
        var config = new PortalConfig
        {
            Identity = new IdentityConfig
            {
                WorkloadIdentity = new WorkloadIdentityConfig
                {
                    Enabled = true,
                    MaximumAssertionLifetimeSeconds = 600,
                    ClockSkewSeconds = 0,
                    Bindings = [binding]
                }
            }
        };
        return new(config, new StaticKeys(validationKey ?? _rsa),
            new MemoryReplayStore(), new TestApprovals(), new FixedTimeProvider(_now));
    }

    private static WorkloadIdentityBindingConfig Binding(string provider, string issuer) => new()
    {
        Id = provider + "-binding",
        Provider = provider,
        ServiceAccountClientId = "sa_ci",
        TenantId = "tenant-a",
        Issuer = issuer,
        Subject = "repo:etl-sql/ETL-SQL:ref:refs/heads/main",
        Audience = "etl-sql-ci",
        Resource = "report:quarterly",
        Operations = ["reports.execute"],
        RequireApproval = true
    };

    private string Token(WorkloadIdentityBindingConfig binding, DateTime? expires = null, string tokenId = "token-1")
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = binding.Issuer,
            Audience = binding.Audience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, binding.Subject),
                new Claim(JwtRegisteredClaimNames.Jti, tokenId)
            ]),
            IssuedAt = _now,
            NotBefore = _now.AddSeconds(-1),
            Expires = expires ?? _now.AddMinutes(5),
            SigningCredentials = new(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    public void Dispose() => _rsa.Dispose();

    private sealed class StaticKeys(RSA rsa) : IWorkloadIdentitySigningKeyProvider
    {
        public Task<IEnumerable<SecurityKey>> GetAsync(WorkloadIdentityBindingConfig binding, CancellationToken ct) =>
            Task.FromResult<IEnumerable<SecurityKey>>([new RsaSecurityKey(rsa)]);
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class MemoryReplayStore : IWorkloadIdentityReplayStore
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public Task<bool> TryUseAsync(string tenantId, string bindingId, string tokenId,
            DateTime expiresUtc, CancellationToken ct) =>
            Task.FromResult(_used.Add($"{tenantId}:{bindingId}:{tokenId}"));
    }

    private sealed class TestApprovals : IWorkloadIdentityApprovalService
    {
        public string Issue(WorkloadIdentityBindingConfig binding, int approvedByUserId) => "APR-42";

        public Task ValidateAsync(WorkloadIdentityBindingConfig binding, string? token, CancellationToken ct)
        {
            if (binding.RequireApproval && token != "APR-42")
                throw new WorkloadIdentityException("workload_approval_required");
            return Task.CompletedTask;
        }
    }
}
