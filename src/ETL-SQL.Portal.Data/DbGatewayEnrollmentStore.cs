using ETL_SQL.Core.Governance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Portal.Data;

/// <summary>
/// Durable, provider-neutral Gateway enrollment store backed by Portal state. Each operation uses
/// its own scope because the store is shared by the long-lived broker while DbContext is scoped.
/// </summary>
public sealed class DbGatewayEnrollmentStore(
    IServiceScopeFactory scopeFactory,
    TimeProvider? timeProvider = null) : IGatewayEnrollmentStore
{
    private const string InvalidTokenMessage = "The enrollment token is not valid.";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<GatewayEnrollment> IssueAsync(
        string tenantId,
        string gatewayId,
        string oneTimeToken,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        GatewayEnrollmentToken.ValidateStrength(oneTimeToken);
        if (expiresUtc <= _time.GetUtcNow())
            throw new ArgumentOutOfRangeException(nameof(expiresUtc), "Enrollment expiry must be in the future.");

        var entity = new GatewayEnrollmentEntity
        {
            EnrollmentId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            GatewayId = gatewayId,
            TokenHash = GatewayEnrollmentToken.Hash(oneTimeToken),
            CreatedUtc = _time.GetUtcNow(),
            ExpiresUtc = expiresUtc,
            State = GatewayEnrollmentState.Pending.ToString()
        };

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        db.GatewayEnrollments.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToModel(entity);
    }

    public async Task<GatewayEnrollment> ConsumeAsync(
        string tenantId,
        string oneTimeToken,
        string workloadPublicKeyThumbprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadPublicKeyThumbprint);
        if (workloadPublicKeyThumbprint.Length != 64
            || workloadPublicKeyThumbprint.Any(character => !Uri.IsHexDigit(character)))
            throw new GatewayEnrollmentException(InvalidTokenMessage);
        if (string.IsNullOrWhiteSpace(oneTimeToken))
            throw new GatewayEnrollmentException(InvalidTokenMessage);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var candidates = await db.GatewayEnrollments
            .Where(value => value.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Compare hashes in constant time. Tenant is part of the query so cross-tenant and unknown
        // presentations remain deliberately indistinguishable.
        var match = candidates.FirstOrDefault(value =>
            GatewayEnrollmentToken.Matches(value.TokenHash, oneTimeToken));
        if (match is null
            || !Enum.TryParse<GatewayEnrollmentState>(match.State, out var state)
            || state != GatewayEnrollmentState.Pending
            || _time.GetUtcNow() >= match.ExpiresUtc)
        {
            throw new GatewayEnrollmentException(InvalidTokenMessage);
        }

        match.State = GatewayEnrollmentState.Consumed.ToString();
        match.ConsumedUtc = _time.GetUtcNow();
        match.WorkloadPublicKeyThumbprint = workloadPublicKeyThumbprint;
        match.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new GatewayEnrollmentException(InvalidTokenMessage);
        }

        return ToModel(match);
    }

    public async Task RevokeAsync(
        string tenantId,
        string gatewayId,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var matches = await db.GatewayEnrollments
            .Where(value => value.TenantId == tenantId && value.GatewayId == gatewayId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var match in matches)
        {
            match.State = GatewayEnrollmentState.Revoked.ToString();
            match.Version++;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GatewayEnrollment?> FindByGatewayAsync(
        string tenantId,
        string gatewayId,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entities = await db.GatewayEnrollments.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.GatewayId == gatewayId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var entity = entities
            .OrderByDescending(value => value.State == "Consumed")
            .ThenByDescending(value => value.CreatedUtc)
            .FirstOrDefault();
        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<GatewayEnrollment>> ListByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var entities = await db.GatewayEnrollments.AsNoTracking()
            .Where(value => value.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.OrderByDescending(value => value.CreatedUtc).Select(ToModel).ToList();
    }

    private static GatewayEnrollment ToModel(GatewayEnrollmentEntity value) => new(
        value.EnrollmentId,
        value.TenantId,
        value.GatewayId,
        value.TokenHash,
        value.CreatedUtc,
        value.ExpiresUtc,
        Enum.Parse<GatewayEnrollmentState>(value.State),
        value.ConsumedUtc,
        value.WorkloadPublicKeyThumbprint);
}
