using System.Text.Json;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public sealed record PortalSharedConnectionSummary(
    string Alias,
    string ConnectorType,
    bool Disabled,
    string? EnvironmentScope,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime? LastVerifiedAtUtc,
    long Version);

public sealed record PortalSharedConnectionDetail(
    PortalSharedConnectionSummary Summary,
    string? Target,
    IReadOnlyDictionary<string, string> Options);

public sealed record SharedConnectionAclEntry(int GroupId, string GroupName, string Permission);

/// <summary>An entry as exported/imported: definition metadata with SECRET: references, never secret values.</summary>
public sealed record PortalSharedConnectionExport(
    string Alias,
    string ConnectorType,
    string? Target,
    Dictionary<string, string> Options,
    string? EnvironmentScope,
    bool Disabled);

/// <summary>
/// Portal-managed shared connection catalog (SHARED:alias). Credential fields hold SECRET:
/// references, never values — enforced on every write, including import.
/// </summary>
public sealed class PortalConnectionCatalogService(PortalDbContext db)
{
    public async Task<bool> StoreAsync(
        PortalSharedConnectionExport entry,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        Validate(entry);

        var now = DateTime.UtcNow;
        var existing = await db.PortalSharedConnections
            .SingleOrDefaultAsync(c => c.Alias == entry.Alias, cancellationToken);
        if (existing == null)
        {
            db.PortalSharedConnections.Add(new PortalSharedConnection
            {
                Alias = entry.Alias,
                ConnectorType = entry.ConnectorType.Trim(),
                Target = entry.Target,
                OptionsJson = JsonSerializer.Serialize(entry.Options),
                Disabled = entry.Disabled,
                EnvironmentScope = entry.EnvironmentScope,
                OwnerUserId = userId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                Version = 1
            });
            return false;
        }

        existing.ConnectorType = entry.ConnectorType.Trim();
        existing.Target = entry.Target;
        existing.OptionsJson = JsonSerializer.Serialize(entry.Options);
        existing.Disabled = entry.Disabled;
        existing.EnvironmentScope = entry.EnvironmentScope;
        existing.UpdatedAtUtc = now;
        existing.UpdatedByUserId = userId;
        existing.Version++;
        return true;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);

    /// <summary>Resolution path for script execution; the last-used touch is best-effort.</summary>
    public async Task<SharedConnectionDefinition> ResolveDefinitionAsync(
        string alias,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.PortalSharedConnections
            .Include(c => c.Acls).ThenInclude(a => a.Group)
            .SingleOrDefaultAsync(c => c.Alias == alias, cancellationToken)
            ?? throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the Portal connection catalog.");

        if (entity.Disabled)
            throw new InvalidOperationException($"Shared connection '{alias}' is disabled.");

        await EnforceUseAclAsync(entity, identity, cancellationToken);

        var definition = new SharedConnectionDefinition(
            entity.Alias, entity.ConnectorType, entity.Target, DeserializeOptions(entity), Disabled: false);

        try
        {
            var now = DateTime.UtcNow;
            entity.LastUsedAtUtc = now;

            var consumer = identity?.EffectiveUser ?? "(none)";
            var usage = await db.SharedConnectionUsages
                .SingleOrDefaultAsync(
                    u => u.SharedConnectionId == entity.Id && u.ConsumerUser == consumer, cancellationToken);
            if (usage == null)
            {
                db.SharedConnectionUsages.Add(new SharedConnectionUsage
                {
                    SharedConnectionId = entity.Id,
                    ConsumerUser = consumer,
                    LastUsedAtUtc = now,
                    UseCount = 1
                });
            }
            else
            {
                usage.LastUsedAtUtc = now;
                usage.UseCount++;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Usage telemetry must never fail resolution (e.g. audit fail-closed or a concurrent update).
            db.ChangeTracker.Clear();
        }

        return definition;
    }

    /// <summary>
    /// Entries without grants are usable by any caller (restriction is opt-in per entry). Entries
    /// with grants require an admin, the entry's owner, or membership in a granted group; callers
    /// without an injected identity are denied. Group membership is resolved authoritatively from
    /// the user id when present, falling back to identity group names for federated identities.
    /// </summary>
    private async Task EnforceUseAclAsync(
        PortalSharedConnection entity,
        ExecutionIdentity? identity,
        CancellationToken cancellationToken)
    {
        if (entity.Acls.Count == 0)
            return;

        if (identity != null)
        {
            if (identity.IsAdmin)
                return;
            if (entity.OwnerUserId != null && identity.EffectiveUserId == entity.OwnerUserId)
                return;

            if (identity.EffectiveUserId is int userId)
            {
                var groupIds = await db.UserGroups
                    .AsNoTracking()
                    .Where(ug => ug.UserId == userId)
                    .Select(ug => ug.GroupId)
                    .ToListAsync(cancellationToken);
                if (entity.Acls.Any(a => groupIds.Contains(a.GroupId)))
                    return;
            }
            else if (entity.Acls.Any(a => identity.HasGroup(a.Group.Name)))
            {
                return;
            }
        }

        throw new UnauthorizedAccessException(
            $"Identity '{identity?.EffectiveUser ?? "(none)"}' is not authorized to use shared connection '{entity.Alias}'. " +
            "Ask an administrator for a use grant on this connection.");
    }

    public async Task<IReadOnlyList<SharedConnectionAclEntry>> ListAclsAsync(string alias, CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        return await db.SharedConnectionAcls
            .AsNoTracking()
            .Where(a => a.SharedConnectionId == entity.Id)
            .OrderBy(a => a.Group.Name)
            .Select(a => new SharedConnectionAclEntry(a.GroupId, a.Group.Name, a.Permission.ToString()))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Grants use to a group (idempotent). Returns false when the grant already existed.</summary>
    public async Task<bool> GrantUseAsync(string alias, int groupId, CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        var exists = await db.SharedConnectionAcls
            .AnyAsync(a => a.SharedConnectionId == entity.Id && a.GroupId == groupId, cancellationToken);
        if (exists)
            return false;

        if (!await db.Groups.AnyAsync(g => g.Id == groupId, cancellationToken))
            throw new KeyNotFoundException($"Group {groupId} does not exist.");

        db.SharedConnectionAcls.Add(new SharedConnectionAcl
        {
            SharedConnectionId = entity.Id,
            GroupId = groupId,
            Permission = SharedConnectionPermission.Use
        });
        return true;
    }

    public async Task<bool> RevokeUseAsync(string alias, int groupId, CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        var acl = await db.SharedConnectionAcls
            .SingleOrDefaultAsync(a => a.SharedConnectionId == entity.Id && a.GroupId == groupId, cancellationToken);
        if (acl == null)
            return false;

        db.SharedConnectionAcls.Remove(acl);
        return true;
    }

    public async Task<IReadOnlyList<PortalSharedConnectionSummary>> ListAsync(CancellationToken cancellationToken = default)
        => (await db.PortalSharedConnections
                .AsNoTracking()
                .OrderBy(c => c.Alias)
                .ToListAsync(cancellationToken))
            .Select(ToSummary)
            .ToList();

    public async Task<PortalSharedConnectionDetail?> GetDetailAsync(string alias, CancellationToken cancellationToken = default)
    {
        var entity = await db.PortalSharedConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Alias == alias, cancellationToken);
        if (entity == null)
            return null;

        return new PortalSharedConnectionDetail(ToSummary(entity), MaskTarget(entity.Target), MaskOptions(DeserializeOptions(entity)));
    }

    public async Task<IReadOnlyList<PortalSharedConnectionExport>> ExportAsync(CancellationToken cancellationToken = default)
        => (await db.PortalSharedConnections
                .AsNoTracking()
                .OrderBy(c => c.Alias)
                .ToListAsync(cancellationToken))
            .Select(entity => new PortalSharedConnectionExport(
                entity.Alias,
                entity.ConnectorType,
                entity.Target,
                new Dictionary<string, string>(DeserializeOptions(entity), StringComparer.OrdinalIgnoreCase),
                entity.EnvironmentScope,
                entity.Disabled))
            .ToList();

    public async Task<SecretLifecycleStatus> GetStatusAsync(string alias, CancellationToken cancellationToken = default)
    {
        var disabled = await db.PortalSharedConnections
            .AsNoTracking()
            .Where(c => c.Alias == alias)
            .Select(c => (bool?)c.Disabled)
            .SingleOrDefaultAsync(cancellationToken);

        return disabled switch
        {
            null => SecretLifecycleStatus.NotFound,
            true => SecretLifecycleStatus.Disabled,
            false => SecretLifecycleStatus.Active
        };
    }

    public async Task DisableAsync(string alias, int? userId = null, CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        if (!entity.Disabled)
        {
            entity.Disabled = true;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.UpdatedByUserId = userId;
            entity.Version++;
        }
    }

    public async Task EnableAsync(string alias, int? userId = null, CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        if (entity.Disabled)
        {
            entity.Disabled = false;
            entity.UpdatedAtUtc = DateTime.UtcNow;
            entity.UpdatedByUserId = userId;
            entity.Version++;
        }
    }

    public async Task DeleteAsync(string alias, CancellationToken cancellationToken = default)
        => db.PortalSharedConnections.Remove(await Require(alias, cancellationToken));

    /// <summary>Resolves every SECRET: reference in the entry to prove it is usable; values are discarded.</summary>
    public async Task<int> VerifySecretReferencesAsync(
        string alias,
        ISecretProvider secrets,
        CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        var references = DeserializeOptions(entity).Values
            .Concat(string.IsNullOrEmpty(entity.Target) ? [] : entity.Target.Split(';'))
            .Select(value => value.Trim().Trim('\'', '"'))
            .Select(value => value.Contains('=') ? value.Split('=', 2)[1].Trim().Trim('\'', '"') : value)
            .Where(value => value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase))
            .Select(value => value["SECRET:".Length..].Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in references)
            _ = await secrets.ResolveAsync(name, cancellationToken);

        entity.LastVerifiedAtUtc = DateTime.UtcNow;
        return references.Count;
    }

    private async Task<PortalSharedConnection> Require(string alias, CancellationToken cancellationToken)
        => await db.PortalSharedConnections.SingleOrDefaultAsync(c => c.Alias == alias, cancellationToken)
            ?? throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the Portal connection catalog.");

    private static void Validate(PortalSharedConnectionExport entry)
    {
        SecretNameValidator.Validate(entry.Alias);
        if (string.IsNullOrWhiteSpace(entry.ConnectorType))
            throw new ArgumentException("A connector type is required.", nameof(entry));

        var rawCredential = SharedConnectionValidator.FindRawCredential(entry.Options, entry.Target);
        if (rawCredential != null)
            throw new ArgumentException(
                $"Field '{rawCredential}' holds a raw credential value. The catalog stores references only: " +
                "store the value in the secret store and reference it as SECRET:name.", nameof(entry));
    }

    private static Dictionary<string, string> DeserializeOptions(PortalSharedConnection entity)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(entity.OptionsJson) is { } options
            ? new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static PortalSharedConnectionSummary ToSummary(PortalSharedConnection entity) => new(
        entity.Alias,
        entity.ConnectorType,
        entity.Disabled,
        entity.EnvironmentScope,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.LastUsedAtUtc,
        entity.LastVerifiedAtUtc,
        entity.Version);

    // Write-side validation guarantees credential fields hold references, but masking is
    // belt-and-braces for entries that arrived by import or older versions.
    private static IReadOnlyDictionary<string, string> MaskOptions(Dictionary<string, string> options)
        => options.ToDictionary(
            pair => pair.Key,
            pair => SecretResolvableFields.IsResolvable(pair.Key) && !IsReference(pair.Value)
                ? SecretRedactor.Mask
                : pair.Value,
            StringComparer.OrdinalIgnoreCase);

    private static string? MaskTarget(string? target)
    {
        if (string.IsNullOrEmpty(target))
            return target;

        var segments = target.Split(';');
        for (var i = 0; i < segments.Length; i++)
        {
            var parts = segments[i].Split('=', 2);
            if (parts.Length == 2 && SecretResolvableFields.IsResolvable(parts[0].Trim()) && !IsReference(parts[1]))
                segments[i] = $"{parts[0]}={SecretRedactor.Mask}";
        }

        return string.Join(';', segments);
    }

    private static bool IsReference(string value)
    {
        var trimmed = value.Trim().Trim('\'', '"');
        return trimmed.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase);
    }
}
