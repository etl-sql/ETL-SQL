using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage;

public partial class RelationalJobHistoryStore
{
    /// <summary>
    /// The empty-string sentinel for an object that never received a signed tenant — a Solo or
    /// otherwise host-fixed deployment. Empty is never a valid tenant id, so it cannot collide with a
    /// real tenant, and using it rather than NULL keeps unbound objects in a single uniqueness
    /// namespace instead of the "every NULL is distinct" behaviour a nullable key column would give.
    /// </summary>
    internal const string UnboundTenantSentinel = "";

    internal static string TenantKey(string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId) ? UnboundTenantSentinel : tenantId;

    internal static string? TenantOrNull(string? storedTenantId) =>
        string.IsNullOrWhiteSpace(storedTenantId) ? null : storedTenantId;

    private static string ObjectTable(OrchestratorObjectKind objectKind) => objectKind switch
    {
        OrchestratorObjectKind.Job => "Jobs",
        OrchestratorObjectKind.Schedule => "Schedules",
        OrchestratorObjectKind.Notification => "Notifications",
        _ => throw new ArgumentOutOfRangeException(nameof(objectKind))
    };

    public async Task<string?> ResolveObjectIdAsync(
        string? tenantId,
        OrchestratorObjectKind objectKind,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName)) return null;
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        // The tenant is part of the lookup, not a filter applied afterwards: a name alone does not
        // identify an object once two tenants may each own one.
        command.CommandText = $@"
            SELECT Id FROM {ObjectTable(objectKind)}
            WHERE TenantId = @tenant AND Name = @name COLLATE NOCASE
            LIMIT 1;";
        command.AddParam("@tenant", TenantKey(tenantId));
        command.AddParam("@name", objectName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (string?)result;
    }

    public async Task<IReadOnlyList<OrchestratorObjectGrant>> GetObjectGrantsAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectId)) return [];
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ObjectId, ObjectKind, PrincipalKind, PrincipalId, Permission, GrantedBy, Version
            FROM OrchestratorObjectAcls
            WHERE ObjectId = @objectId
            ORDER BY PrincipalKind, PrincipalId;";
        command.AddParam("@objectId", objectId);

        var grants = new List<OrchestratorObjectGrant>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            grants.Add(new OrchestratorObjectGrant(
                reader.GetString(0),
                Enum.Parse<OrchestratorObjectKind>(reader.GetString(1), ignoreCase: true),
                Enum.Parse<OrchestratorPrincipalKind>(reader.GetString(2), ignoreCase: true),
                reader.GetString(3),
                Enum.Parse<OrchestratorObjectPermission>(reader.GetString(4), ignoreCase: true),
                reader.GetString(5),
                reader.GetInt64(6)));
        }
        return grants;
    }

    public async Task SaveObjectGrantAsync(
        OrchestratorObjectGrant grant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (string.IsNullOrWhiteSpace(grant.ObjectId))
            throw new ArgumentException("A grant requires a resolved object id.", nameof(grant));
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO OrchestratorObjectAcls
                (ObjectId, ObjectKind, PrincipalKind, PrincipalId, Permission, GrantedBy, Version)
            VALUES (@objectId, @kind, @principalKind, @principalId, @permission, @grantedBy, 1)
            ON CONFLICT(ObjectId, PrincipalKind, PrincipalId) DO UPDATE SET
                Permission = excluded.Permission,
                GrantedBy = excluded.GrantedBy,
                Version = OrchestratorObjectAcls.Version + 1;";
        command.AddParam("@objectId", grant.ObjectId);
        command.AddParam("@kind", grant.ObjectKind.ToString());
        command.AddParam("@principalKind", grant.PrincipalKind.ToString());
        command.AddParam("@principalId", grant.PrincipalId);
        command.AddParam("@permission", grant.Permission.ToString());
        command.AddParam("@grantedBy", grant.GrantedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteObjectGrantAsync(
        string objectId,
        OrchestratorPrincipalKind principalKind,
        string principalId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectId)) return false;
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            DELETE FROM OrchestratorObjectAcls
            WHERE ObjectId = @objectId
              AND PrincipalKind = @principalKind AND PrincipalId = @principalId;";
        command.AddParam("@objectId", objectId);
        command.AddParam("@principalKind", principalKind.ToString());
        command.AddParam("@principalId", principalId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task DeleteObjectGrantsAsync(
        string objectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectId)) return;
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OrchestratorObjectAcls WHERE ObjectId = @objectId;";
        command.AddParam("@objectId", objectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
