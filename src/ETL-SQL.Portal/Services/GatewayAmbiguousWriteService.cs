using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public interface IGatewayAmbiguousWriteRecorder
{
    Task RecordAsync(GatewayOperation operation, CancellationToken cancellationToken);
}

public sealed record GatewayAmbiguousWriteEventDto(
    long Id,
    string EventType,
    string Actor,
    string? Note,
    string? EvidenceReference,
    string? Resolution,
    DateTime CreatedAtUtc);

public sealed record GatewayAmbiguousWriteCaseDto(
    long Id,
    string OperationId,
    string TenantId,
    string GatewayId,
    string ResourceId,
    string CorrelationId,
    DateTime ExecutedAtUtc,
    string State,
    string Priority,
    string? Owner,
    string? Resolution,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    long Version,
    IReadOnlyList<GatewayAmbiguousWriteEventDto> Events);

/// <summary>
/// Durable, deduplicated ambiguous-write inbox. Mutations append events; no method deletes a case
/// or event, and resolution requires externally verified evidence.
/// </summary>
public sealed class GatewayAmbiguousWriteService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config) : IGatewayAmbiguousWriteRecorder
{
    public static readonly IReadOnlySet<string> AllowedResolutions = new HashSet<string>(
        ["confirmed committed", "confirmed not applied", "compensated", "superseded"],
        StringComparer.OrdinalIgnoreCase);

    public async Task RecordAsync(GatewayOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Effect != GatewayOperationEffect.Mutating)
            return;

        var tenantId = NormalizeTenant(operation.TenantId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        if (await db.GatewayAmbiguousWriteCases.AsNoTracking().AnyAsync(
            item => item.TenantId == tenantId && item.OperationId == operation.OperationId,
            cancellationToken))
            return;

        var now = DateTime.UtcNow;
        var item = new GatewayAmbiguousWriteCase
        {
            TenantId = tenantId,
            OperationId = operation.OperationId,
            GatewayId = operation.GatewayId,
            ResourceId = operation.ResourceId,
            CorrelationId = operation.CorrelationId,
            ExecutedAtUtc = operation.DispatchedAtUtc?.UtcDateTime ?? now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Events =
            [
                new GatewayAmbiguousWriteEvent
                {
                    TenantId = tenantId,
                    EventType = "Detected",
                    Actor = "Gateway",
                    Note = "A mutating Gateway operation returned an ambiguous outcome.",
                    CreatedAtUtc = now
                }
            ]
        };
        db.GatewayAmbiguousWriteCases.Add(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.GatewayAmbiguousWriteCases.AsNoTracking().AnyAsync(
                existing => existing.TenantId == tenantId && existing.OperationId == operation.OperationId,
                cancellationToken))
                throw;
        }
    }

    public async Task<IReadOnlyList<GatewayAmbiguousWriteCaseDto>> ListAsync(
        string tenantId,
        bool includeResolved,
        CancellationToken cancellationToken)
    {
        var normalizedTenant = NormalizeTenant(tenantId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var query = db.GatewayAmbiguousWriteCases.AsNoTracking()
            .Include(item => item.Events)
            .Where(item => item.TenantId == normalizedTenant);
        if (!includeResolved)
            query = query.Where(item => item.State != "Resolved");
        return (await query.OrderByDescending(item => item.ExecutedAtUtc)
            .ToListAsync(cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<GatewayAmbiguousWriteCaseDto?> GetAsync(
        string tenantId,
        long id,
        CancellationToken cancellationToken)
    {
        var normalizedTenant = NormalizeTenant(tenantId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var item = await db.GatewayAmbiguousWriteCases.AsNoTracking()
            .Include(candidate => candidate.Events)
            .SingleOrDefaultAsync(candidate => candidate.TenantId == normalizedTenant && candidate.Id == id,
                cancellationToken);
        return item is null ? null : ToDto(item);
    }

    public Task<GatewayAmbiguousWriteCaseDto> AcknowledgeAsync(
        string tenantId, long id, long expectedVersion, string actor, string? note,
        CancellationToken cancellationToken) =>
        MutateAsync(tenantId, id, expectedVersion, actor, "Acknowledged", note, null, null,
            item =>
            {
                EnsureUnresolved(item);
                if (item.State == "Open") item.State = "Acknowledged";
            }, cancellationToken);

    public Task<GatewayAmbiguousWriteCaseDto> AssignAsync(
        string tenantId, long id, long expectedVersion, string actor, string owner, string? note,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Trim().Length > 256)
            throw new ArgumentException("A valid owner is required.", nameof(owner));
        return MutateAsync(tenantId, id, expectedVersion, actor, "Assigned", note, null, null,
            item =>
            {
                EnsureUnresolved(item);
                item.Owner = owner.Trim();
                item.State = "Assigned";
            }, cancellationToken);
    }

    public Task<GatewayAmbiguousWriteCaseDto> AddEvidenceAsync(
        string tenantId, long id, long expectedVersion, string actor, string? note,
        string? evidenceReference, CancellationToken cancellationToken)
    {
        RequireEvidence(note, evidenceReference);
        return MutateAsync(tenantId, id, expectedVersion, actor, "EvidenceAdded", note,
            evidenceReference, null, _ => { }, cancellationToken);
    }

    public Task<GatewayAmbiguousWriteCaseDto> ResolveAsync(
        string tenantId, long id, long expectedVersion, string actor, string resolution,
        string? note, string? evidenceReference, CancellationToken cancellationToken)
    {
        var requestedResolution = resolution?.Trim();
        if (string.IsNullOrEmpty(requestedResolution) || !AllowedResolutions.Contains(requestedResolution))
            throw new ArgumentException("The externally verified resolution is not supported.", nameof(resolution));
        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new ArgumentException("An external evidence reference is required.", nameof(evidenceReference));
        var normalized = AllowedResolutions.Single(value =>
            string.Equals(value, requestedResolution, StringComparison.OrdinalIgnoreCase));
        return MutateAsync(tenantId, id, expectedVersion, actor, "Resolved", note,
            evidenceReference, normalized, item =>
            {
                EnsureUnresolved(item);
                item.State = "Resolved";
                item.Resolution = normalized;
            }, cancellationToken);
    }

    private async Task<GatewayAmbiguousWriteCaseDto> MutateAsync(
        string tenantId,
        long id,
        long expectedVersion,
        string actor,
        string eventType,
        string? note,
        string? evidenceReference,
        string? resolution,
        Action<GatewayAmbiguousWriteCase> apply,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new UnauthorizedAccessException("An authenticated operator is required.");
        if (note?.Length > 4000 || evidenceReference?.Length > 1000)
            throw new ArgumentException("Case evidence exceeds the allowed length.");

        var normalizedTenant = NormalizeTenant(tenantId);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var item = await db.GatewayAmbiguousWriteCases.Include(candidate => candidate.Events)
            .SingleOrDefaultAsync(candidate => candidate.TenantId == normalizedTenant && candidate.Id == id,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Ambiguous-write case {id} was not found.");
        if (item.Version != expectedVersion)
            throw new DbUpdateConcurrencyException("The ambiguous-write case changed; reload before updating it.");

        apply(item);
        var now = DateTime.UtcNow;
        item.UpdatedAtUtc = now;
        item.Version++;
        item.Events.Add(new GatewayAmbiguousWriteEvent
        {
            TenantId = normalizedTenant,
            EventType = eventType,
            Actor = actor,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            EvidenceReference = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim(),
            Resolution = resolution,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(item);
    }

    private static void RequireEvidence(string? note, string? evidenceReference)
    {
        if (string.IsNullOrWhiteSpace(note) && string.IsNullOrWhiteSpace(evidenceReference))
            throw new ArgumentException("Evidence or an operator note is required.");
    }

    private static void EnsureUnresolved(GatewayAmbiguousWriteCase item)
    {
        if (item.State == "Resolved")
            throw new ArgumentException("A resolved ambiguous-write case cannot be changed.");
    }

    private string NormalizeTenant(string tenantId) =>
        tenantId == "default" && config.SharedTenancy.Enabled != true && string.IsNullOrWhiteSpace(config.TenantId)
            ? "portal-host"
            : tenantId;

    private static GatewayAmbiguousWriteCaseDto ToDto(GatewayAmbiguousWriteCase item) => new(
        item.Id, item.OperationId, item.TenantId, item.GatewayId, item.ResourceId, item.CorrelationId,
        item.ExecutedAtUtc, item.State, item.Priority, item.Owner, item.Resolution,
        item.CreatedAtUtc, item.UpdatedAtUtc, item.Version,
        item.Events.OrderBy(entry => entry.CreatedAtUtc).ThenBy(entry => entry.Id)
            .Select(entry => new GatewayAmbiguousWriteEventDto(
                entry.Id, entry.EventType, entry.Actor, entry.Note, entry.EvidenceReference,
                entry.Resolution, entry.CreatedAtUtc)).ToList());
}
