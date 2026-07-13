using ETL_SQL.Core.Governance;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Data;

/// <summary>
/// Database-backed <see cref="IPolicyAuthorityStore"/> over the portal <see cref="PolicyVersions"/>
/// table. Rows are append-only; supersession flips rollout state without deleting or rewriting the
/// signed envelope, preserving an immutable published-version history.
/// </summary>
public sealed class DbPolicyAuthorityStore(PortalDbContext db) : IPolicyAuthorityStore
{
    public async Task<PublishedPolicyVersion?> GetActiveAsync(
        string tenant, string environment, CancellationToken ct = default)
    {
        // Order by the append-order Id (not IssuedAtUtc, which SQLite cannot ORDER BY as a
        // DateTimeOffset); issuance is monotonic, so append order matches issuance order.
        var row = await db.PolicyVersions
            .Where(x => x.Tenant == tenant && x.Environment == environment
                && x.RolloutState == nameof(PolicyRolloutState.Active))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async Task<PublishedPolicyVersion?> GetCanaryAsync(
        string tenant, string environment, CancellationToken ct = default)
    {
        var row = await db.PolicyVersions
            .Where(x => x.Tenant == tenant && x.Environment == environment
                && x.RolloutState == nameof(PolicyRolloutState.Canary))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<PublishedPolicyVersion>> ListAsync(
        string tenant, string environment, CancellationToken ct = default)
    {
        var rows = await db.PolicyVersions
            .Where(x => x.Tenant == tenant && x.Environment == environment)
            .OrderBy(x => x.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(ToDomain).ToArray();
    }

    public async Task AppendAsync(PublishedPolicyVersion version, CancellationToken ct = default)
    {
        db.PolicyVersions.Add(new PolicyVersionEntity
        {
            Tenant = version.Tenant,
            Environment = version.Environment,
            PolicyVersion = version.PolicyVersion,
            PolicyHash = version.PolicyHash,
            IssuedAtUtc = version.IssuedAtUtc,
            ExpiresAtUtc = version.ExpiresAtUtc,
            Author = version.Author,
            Reviewer = version.Reviewer,
            SupersededVersion = version.SupersededVersion,
            RolloutState = version.RolloutState.ToString(),
            SignedEnvelopeJson = version.SignedEnvelopeJson,
            PublishedAtUtc = version.PublishedAtUtc,
            CanaryGroup = version.Canary?.Group,
            CanaryPercentage = version.Canary?.Percentage
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SetRolloutStateAsync(
        string tenant, string environment, string policyVersion, PolicyRolloutState state,
        CancellationToken ct = default)
    {
        var row = await db.PolicyVersions
            .FirstOrDefaultAsync(x => x.Tenant == tenant && x.Environment == environment
                && x.PolicyVersion == policyVersion, ct)
            .ConfigureAwait(false);
        if (row is null) return;
        row.RolloutState = state.ToString();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static PublishedPolicyVersion ToDomain(PolicyVersionEntity x) => new(
        x.Tenant, x.Environment, x.PolicyVersion, x.PolicyHash, x.IssuedAtUtc, x.ExpiresAtUtc,
        x.Author, x.Reviewer, x.SupersededVersion,
        Enum.Parse<PolicyRolloutState>(x.RolloutState), x.SignedEnvelopeJson, x.PublishedAtUtc)
    {
        Canary = x.CanaryGroup is null && x.CanaryPercentage is null
            ? null
            : new CanaryCohort { Group = x.CanaryGroup, Percentage = x.CanaryPercentage }
    };
}
