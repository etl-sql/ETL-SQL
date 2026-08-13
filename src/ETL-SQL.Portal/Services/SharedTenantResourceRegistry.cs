using System.Text.RegularExpressions;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record SharedTenantResourceDto(
    long Id,
    string Kind,
    string LogicalId,
    string ScopedId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    long Version);

/// <summary>
/// Durable shared namespace registry for the control-plane identifiers that later Gateway,
/// scheduler, storage, queue, and index providers consume. Every query includes the tenant derived
/// from verified request context; neither a numeric id nor a scoped-id string can select a tenant.
/// </summary>
public sealed partial class SharedTenantResourceRegistry(
    PortalDbContext db,
    PortalConfig config,
    RequestTenantContextAccessor tenantAccessor)
{
    public static readonly IReadOnlySet<string> SupportedKinds = new HashSet<string>(
        ["alias", "gateway", "resource", "run", "object", "storage", "queue", "index"],
        StringComparer.Ordinal);

    private TenantContext Context
    {
        get
        {
            if (!config.SharedTenancy.Enabled)
                throw new InvalidOperationException("The shared tenant resource registry requires Shared tenancy mode.");
            var context = tenantAccessor.RequireCurrent();
            if (context.Origin != TenantContextOrigin.VerifiedCredential)
                throw new UnauthorizedAccessException(
                    "Shared registry tenant authority must come from a verified credential.");
            return context;
        }
    }

    public async Task<IReadOnlyList<SharedTenantResourceDto>> ListAsync(
        string kind,
        CancellationToken ct = default)
    {
        kind = NormalizeKind(kind);
        var tenant = Context.Tenant.Value;
        return await db.SharedTenantResources.AsNoTracking()
            .Where(value => value.TenantId == tenant && value.Kind == kind)
            .OrderBy(value => value.LogicalId)
            .Select(value => ToDto(value))
            .ToListAsync(ct);
    }

    public async Task<SharedTenantResourceDto?> FindAsync(
        string kind,
        long id,
        CancellationToken ct = default)
    {
        kind = NormalizeKind(kind);
        if (id <= 0) return null;
        var tenant = Context.Tenant.Value;
        var value = await db.SharedTenantResources.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenant && candidate.Kind == kind && candidate.Id == id,
            ct);
        return value is null ? null : ToDto(value);
    }

    public async Task<SharedTenantResourceDto?> FindScopedAsync(
        string kind,
        string callerSuppliedScopedId,
        CancellationToken ct = default)
    {
        kind = NormalizeKind(kind);
        var context = Context;
        var scopedId = context.RequireOwned(callerSuppliedScopedId, $"{kind} scoped id");
        var tenant = context.Tenant.Value;
        var value = await db.SharedTenantResources.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenant && candidate.Kind == kind
                         && candidate.ScopedId == scopedId,
            ct);
        return value is null ? null : ToDto(value);
    }

    public async Task<SharedTenantResourceDto> RegisterAsync(
        string kind,
        string logicalId,
        CancellationToken ct = default)
    {
        kind = NormalizeKind(kind);
        logicalId = NormalizeLogicalId(logicalId);
        var context = Context;
        var tenant = context.Tenant.Value;
        var scopedId = context.ScopeKey($"{kind}/{logicalId}");
        var existing = await db.SharedTenantResources.SingleOrDefaultAsync(
            value => value.TenantId == tenant && value.Kind == kind && value.LogicalId == logicalId,
            ct);
        if (existing is not null)
            return ToDto(existing);

        var now = DateTime.UtcNow;
        var entity = new SharedTenantResource
        {
            TenantId = tenant,
            Kind = kind,
            LogicalId = logicalId,
            ScopedId = scopedId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.SharedTenantResources.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(entity).State = EntityState.Detached;
            existing = await db.SharedTenantResources.AsNoTracking().SingleOrDefaultAsync(
                value => value.TenantId == tenant && value.Kind == kind && value.LogicalId == logicalId,
                ct);
            if (existing is null) throw;
            return ToDto(existing);
        }
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(string kind, long id, CancellationToken ct = default)
    {
        kind = NormalizeKind(kind);
        if (id <= 0) return false;
        var tenant = Context.Tenant.Value;
        var deleted = await db.SharedTenantResources
            .Where(value => value.TenantId == tenant && value.Kind == kind && value.Id == id)
            .ExecuteDeleteAsync(ct);
        return deleted == 1;
    }

    private static SharedTenantResourceDto ToDto(SharedTenantResource value) => new(
        value.Id, value.Kind, value.LogicalId, value.ScopedId,
        value.CreatedAtUtc, value.UpdatedAtUtc, value.Version);

    private static string NormalizeKind(string value)
    {
        value = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SupportedKinds.Contains(value))
            throw new ArgumentException(
                $"Shared resource kind must be one of: {string.Join(", ", SupportedKinds)}.",
                nameof(value));
        return value;
    }

    private static string NormalizeLogicalId(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (!LogicalIdPattern().IsMatch(value))
            throw new ArgumentException(
                "Logical ids must be 1-128 characters using letters, numbers, dot, underscore, or hyphen.",
                nameof(value));
        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalIdPattern();
}
