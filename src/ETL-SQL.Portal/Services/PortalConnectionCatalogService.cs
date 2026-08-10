using System.Text.Json;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

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
    IReadOnlyDictionary<string, string> Options,
    IReadOnlyList<string> SensitiveFields);

public sealed record SharedConnectionAclEntry(int GroupId, string GroupName, string Permission);

/// <summary>An entry as exported/imported: definition metadata with SECRET: references, never secret values.</summary>
public sealed record PortalSharedConnectionExport(
    string Alias,
    string ConnectorType,
    string? Target,
    Dictionary<string, string> Options,
    string? EnvironmentScope,
    bool Disabled,
    List<string>? SensitiveFields = null);

/// <summary>
/// Portal-managed shared connection catalog (SHARED:alias). Credential fields hold SECRET:
/// references, never values — enforced on every write, including import.
/// </summary>
public sealed class PortalConnectionCatalogService(
    PortalDbContext db,
    TenantContext? tenantContext = null,
    PortalConfig? config = null)
{
    private readonly string _tenantScope = ResolveTenantScope(tenantContext, config);
    private string TenantScope => _tenantScope;

    private static string ResolveTenantScope(TenantContext? context, PortalConfig? config)
    {
        if (context is not null) return context.Tenant.Value;
        if (config?.SharedTenancy.Enabled == true)
            throw new UnauthorizedAccessException(
                "Shared connection-catalog access requires a server-verified tenant context.");
        return string.IsNullOrWhiteSpace(config?.TenantId) ? "portal-host" : config.TenantId;
    }

    public async Task<bool> StoreAsync(
        PortalSharedConnectionExport entry,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        Validate(entry);

        var now = DateTime.UtcNow;
        var existing = await db.PortalSharedConnections
            .SingleOrDefaultAsync(c => c.TenantId == TenantScope && c.Alias == entry.Alias, cancellationToken);
        if (existing == null)
        {
            db.PortalSharedConnections.Add(new PortalSharedConnection
            {
                TenantId = TenantScope,
                Alias = entry.Alias,
                ConnectorType = entry.ConnectorType.Trim(),
                Target = entry.Target,
                OptionsJson = JsonSerializer.Serialize(entry.Options),
                Disabled = entry.Disabled,
                EnvironmentScope = entry.EnvironmentScope,
                SensitiveFieldsCsv = ToCsv(entry.SensitiveFields),
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
        existing.SensitiveFieldsCsv = ToCsv(entry.SensitiveFields);
        existing.UpdatedAtUtc = now;
        existing.UpdatedByUserId = userId;
        existing.Version++;
        return true;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Lists the aliases this identity may actually use, applying the same rule as
    /// <see cref="EnforceUseAclAsync"/>: ungranted entries are open to all, granted entries need
    /// admin, ownership, or group membership. Used to populate the editor's schema explorer
    /// without disclosing the existence of connections the caller cannot use.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListUsableAliasesAsync(
        ExecutionIdentity? identity,
        CancellationToken cancellationToken = default)
    {
        var entries = await db.PortalSharedConnections
            .AsNoTracking()
            .Include(c => c.Acls).ThenInclude(a => a.Group)
            .Where(c => c.TenantId == TenantScope && !c.Disabled)
            .OrderBy(c => c.Alias)
            .ToListAsync(cancellationToken);

        var groupIds = identity?.EffectiveUserId is int userId
            ? await db.UserGroups.AsNoTracking()
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.GroupId)
                .ToListAsync(cancellationToken)
            : [];

        return entries.Where(entry => CanUse(entry, identity, groupIds)).Select(entry => entry.Alias).ToList();
    }

    private static bool CanUse(PortalSharedConnection entity, ExecutionIdentity? identity, IReadOnlyCollection<int> groupIds)
    {
        if (entity.Acls.Count == 0) return true;
        if (identity is null) return false;
        if (identity.IsAdmin) return true;
        if (entity.OwnerUserId != null && identity.EffectiveUserId == entity.OwnerUserId) return true;
        if (identity.EffectiveUserId is int)
            return entity.Acls.Any(a => groupIds.Contains(a.GroupId));
        return entity.Acls.Any(a => identity.HasGroup(a.Group.Name));
    }

    /// <summary>Resolution path for script execution; the last-used touch is best-effort.</summary>
    public async Task<SharedConnectionDefinition> ResolveDefinitionAsync(
        string alias,
        ExecutionIdentity? identity = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.PortalSharedConnections
            .Include(c => c.Acls).ThenInclude(a => a.Group)
            .SingleOrDefaultAsync(c => c.TenantId == TenantScope && c.Alias == alias, cancellationToken)
            ?? throw new KeyNotFoundException($"Shared connection '{alias}' was not found in the Portal connection catalog.");

        if (entity.Disabled)
            throw new InvalidOperationException($"Shared connection '{alias}' is disabled.");

        await EnforceUseAclAsync(entity, identity, cancellationToken);

        var definition = new SharedConnectionDefinition(
            entity.Alias, entity.ConnectorType, entity.Target, DeserializeOptions(entity), Disabled: false,
            FromCsv(entity.SensitiveFieldsCsv));

        try
        {
            var now = DateTime.UtcNow;
            entity.LastUsedAtUtc = now;

            var consumer = identity?.EffectiveUser ?? "(none)";
            var usage = await db.SharedConnectionUsages
                .SingleOrDefaultAsync(
                    u => u.TenantId == TenantScope && u.SharedConnectionId == entity.Id
                        && u.ConsumerUser == consumer, cancellationToken);
            if (usage == null)
            {
                db.SharedConnectionUsages.Add(new SharedConnectionUsage
                {
                    TenantId = TenantScope,
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
    /// Entries without grants are usable by any caller. Entries with grants require an admin, the
    /// entry owner, or membership in a granted group; identity-less callers are denied.
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

    public async Task<IReadOnlyList<SharedConnectionAclEntry>> ListAclsAsync(
        string alias,
        CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        return await db.SharedConnectionAcls
            .AsNoTracking()
            .Where(a => a.TenantId == TenantScope && a.SharedConnectionId == entity.Id)
            .OrderBy(a => a.Group.Name)
            .Select(a => new SharedConnectionAclEntry(a.GroupId, a.Group.Name, a.Permission.ToString()))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> GrantUseAsync(
        string alias,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        var exists = await db.SharedConnectionAcls
            .AnyAsync(a => a.TenantId == TenantScope && a.SharedConnectionId == entity.Id
                && a.GroupId == groupId, cancellationToken);
        if (exists)
            return false;

        if (!await db.Groups.AnyAsync(g => g.Id == groupId, cancellationToken))
            throw new KeyNotFoundException($"Group {groupId} does not exist.");

        db.SharedConnectionAcls.Add(new SharedConnectionAcl
        {
            TenantId = TenantScope,
            SharedConnectionId = entity.Id,
            GroupId = groupId,
            Permission = SharedConnectionPermission.Use
        });
        return true;
    }

    public async Task<bool> RevokeUseAsync(
        string alias,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Require(alias, cancellationToken);
        var acl = await db.SharedConnectionAcls
            .SingleOrDefaultAsync(a => a.TenantId == TenantScope && a.SharedConnectionId == entity.Id
                && a.GroupId == groupId, cancellationToken);
        if (acl == null)
            return false;

        db.SharedConnectionAcls.Remove(acl);
        return true;
    }

    public async Task<IReadOnlyList<PortalSharedConnectionSummary>> ListAsync(CancellationToken cancellationToken = default)
        => (await db.PortalSharedConnections
                .AsNoTracking()
                .Where(c => c.TenantId == TenantScope)
                .OrderBy(c => c.Alias)
                .ToListAsync(cancellationToken))
            .Select(ToSummary)
            .ToList();

    public async Task<PortalSharedConnectionDetail?> GetDetailAsync(string alias, CancellationToken cancellationToken = default)
    {
        var entity = await db.PortalSharedConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TenantId == TenantScope && c.Alias == alias, cancellationToken);
        if (entity == null)
            return null;

        var sensitiveFields = FromCsv(entity.SensitiveFieldsCsv);
        return new PortalSharedConnectionDetail(
            ToSummary(entity),
            MaskTarget(entity.Target, entity.ConnectorType, sensitiveFields),
            MaskOptions(DeserializeOptions(entity), entity.ConnectorType, sensitiveFields),
            sensitiveFields);
    }

    public async Task<IReadOnlyList<PortalSharedConnectionExport>> ExportAsync(CancellationToken cancellationToken = default)
        => (await db.PortalSharedConnections
                .AsNoTracking()
                .Where(c => c.TenantId == TenantScope)
                .OrderBy(c => c.Alias)
                .ToListAsync(cancellationToken))
            .Select(entity => new PortalSharedConnectionExport(
                entity.Alias,
                entity.ConnectorType,
                entity.Target,
                new Dictionary<string, string>(DeserializeOptions(entity), StringComparer.OrdinalIgnoreCase),
                entity.EnvironmentScope,
                entity.Disabled,
                FromCsv(entity.SensitiveFieldsCsv).ToList()))
            .ToList();

    public async Task<SecretLifecycleStatus> GetStatusAsync(string alias, CancellationToken cancellationToken = default)
    {
        var disabled = await db.PortalSharedConnections
            .AsNoTracking()
            .Where(c => c.TenantId == TenantScope && c.Alias == alias)
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
        => await db.PortalSharedConnections.SingleOrDefaultAsync(
            c => c.TenantId == TenantScope && c.Alias == alias, cancellationToken)
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

        var maskedPlaceholder = FindMaskedPlaceholder(entry);
        if (maskedPlaceholder != null)
            throw new ArgumentException(
                $"Field '{maskedPlaceholder}' contains a masked display placeholder. Re-enter the original value or a SECRET:name reference before saving.",
                nameof(entry));
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
    // belt-and-braces for entries that arrived by import or older versions. Entry-classified
    // sensitive fields are masked even when they hold plain values.
    private static IReadOnlyDictionary<string, string> MaskOptions(
        Dictionary<string, string> options, string connectorType, IReadOnlyCollection<string> sensitiveFields)
        => options.ToDictionary(
            pair => pair.Key,
            pair => IsMaskedField(pair.Key, connectorType, sensitiveFields) && !IsReference(pair.Value)
                ? SecretRedactor.Mask
                : pair.Value,
            StringComparer.OrdinalIgnoreCase);

    private static string? MaskTarget(string? target, string connectorType, IReadOnlyCollection<string> sensitiveFields)
    {
        if (string.IsNullOrEmpty(target))
            return target;

        var segments = target.Split(';');
        for (var i = 0; i < segments.Length; i++)
        {
            var parts = segments[i].Split('=', 2);
            if (parts.Length == 2 && IsMaskedField(parts[0].Trim(), connectorType, sensitiveFields) && !IsReference(parts[1]))
                segments[i] = $"{parts[0]}={SecretRedactor.Mask}";
        }

        return string.Join(';', segments);
    }

    private static bool IsMaskedField(string key, string connectorType, IReadOnlyCollection<string> sensitiveFields) =>
        SecretResolvableFields.IsResolvable(key, connectorType)
        || sensitiveFields.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static string? FindMaskedPlaceholder(PortalSharedConnectionExport entry)
    {
        var sensitiveFields = entry.SensitiveFields ?? [];
        foreach (var (key, value) in entry.Options)
        {
            if (IsMaskedField(key, entry.ConnectorType, sensitiveFields)
                && string.Equals(value?.Trim(), SecretRedactor.Mask, StringComparison.Ordinal))
            {
                return key;
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Target))
        {
            foreach (var segment in entry.Target.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = segment.Split('=', 2);
                if (parts.Length == 2
                    && IsMaskedField(parts[0].Trim(), entry.ConnectorType, sensitiveFields)
                    && string.Equals(parts[1].Trim(), SecretRedactor.Mask, StringComparison.Ordinal))
                {
                    return parts[0].Trim();
                }
            }
        }

        return null;
    }

    private static bool IsReference(string value)
    {
        var trimmed = value.Trim().Trim('\'', '"');
        return trimmed.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ToCsv(IEnumerable<string>? fields)
    {
        var normalized = (fields ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count == 0 ? null : string.Join(',', normalized);
    }

    private static IReadOnlyList<string> FromCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
