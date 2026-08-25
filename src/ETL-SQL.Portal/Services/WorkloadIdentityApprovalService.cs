using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.Portal.Services;

public sealed class WorkloadIdentityApprovalService(
    PortalConfig config,
    IWorkloadIdentityReplayStore replay,
    TimeProvider timeProvider) : IWorkloadIdentityApprovalService
{
    public const string Issuer = "etl-sql-workload-approval";
    public const string Audience = "etl-sql-workload-token-exchange";
    public const int LifetimeSeconds = 300;

    public string Issue(WorkloadIdentityBindingConfig binding, int approvedByUserId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var claims = new[]
        {
            new Claim("tenant_id", binding.TenantId),
            new Claim("binding_id", binding.Id),
            new Claim("resource", binding.Resource),
            new Claim("operation", string.Join(' ', binding.Operations)),
            new Claim("approved_by", approvedByUserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var token = new JwtSecurityToken(Issuer, Audience, claims, now, now.AddSeconds(LifetimeSeconds),
            new SigningCredentials(JwtSigningKeyRing.Current(config.Jwt), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task ValidateAsync(
        WorkloadIdentityBindingConfig binding, string? token, CancellationToken ct)
    {
        if (!binding.RequireApproval) return;
        if (string.IsNullOrWhiteSpace(token))
            throw new WorkloadIdentityException("workload_approval_required");
        var result = await new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().ValidateTokenAsync(
            token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = JwtSigningKeyRing.ValidationKeys(config.Jwt),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(15)
            });
        if (!result.IsValid) throw new WorkloadIdentityException("invalid_workload_approval");
        var identity = result.ClaimsIdentity;
        if (!Exact(identity, "tenant_id", binding.TenantId)
            || !Exact(identity, "binding_id", binding.Id)
            || !Exact(identity, "resource", binding.Resource)
            || !Exact(identity, "operation", string.Join(' ', binding.Operations)))
            throw new WorkloadIdentityException("invalid_workload_approval");
        var jti = identity.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var exp = identity.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (string.IsNullOrWhiteSpace(jti) || !long.TryParse(exp, out var epoch)
            || !await replay.TryUseAsync(binding.TenantId, binding.Id + ":approval", jti,
                DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime, ct))
            throw new WorkloadIdentityException("workload_approval_replay_rejected");
    }

    private static bool Exact(ClaimsIdentity identity, string type, string expected) =>
        string.Equals(identity.FindFirst(type)?.Value, expected, StringComparison.Ordinal);
}
