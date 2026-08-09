using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Orchestrator.Storage;

public partial class RelationalJobHistoryStore
{
    public async Task<IReadOnlyList<OrchestratorObjectGrant>> GetObjectGrantsAsync(
        OrchestratorObjectKind objectKind,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT ObjectKind, ObjectName, PrincipalKind, PrincipalId, Permission, GrantedBy, Version
            FROM OrchestratorObjectAcls
            WHERE ObjectKind = @kind AND ObjectName = @name COLLATE NOCASE
            ORDER BY PrincipalKind, PrincipalId;";
        command.AddParam("@kind", objectKind.ToString());
        command.AddParam("@name", objectName);

        var grants = new List<OrchestratorObjectGrant>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            grants.Add(new OrchestratorObjectGrant(
                Enum.Parse<OrchestratorObjectKind>(reader.GetString(0), ignoreCase: true),
                reader.GetString(1),
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
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO OrchestratorObjectAcls
                (ObjectKind, ObjectName, PrincipalKind, PrincipalId, Permission, GrantedBy, Version)
            VALUES (@kind, @name, @principalKind, @principalId, @permission, @grantedBy, 1)
            ON CONFLICT(ObjectKind, ObjectName, PrincipalKind, PrincipalId) DO UPDATE SET
                Permission = excluded.Permission,
                GrantedBy = excluded.GrantedBy,
                Version = OrchestratorObjectAcls.Version + 1;";
        command.AddParam("@kind", grant.ObjectKind.ToString());
        command.AddParam("@name", grant.ObjectName);
        command.AddParam("@principalKind", grant.PrincipalKind.ToString());
        command.AddParam("@principalId", grant.PrincipalId);
        command.AddParam("@permission", grant.Permission.ToString());
        command.AddParam("@grantedBy", grant.GrantedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteObjectGrantAsync(
        OrchestratorObjectKind objectKind,
        string objectName,
        OrchestratorPrincipalKind principalKind,
        string principalId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            DELETE FROM OrchestratorObjectAcls
            WHERE ObjectKind = @kind AND ObjectName = @name COLLATE NOCASE
              AND PrincipalKind = @principalKind AND PrincipalId = @principalId;";
        command.AddParam("@kind", objectKind.ToString());
        command.AddParam("@name", objectName);
        command.AddParam("@principalKind", principalKind.ToString());
        command.AddParam("@principalId", principalId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task DeleteObjectGrantsAsync(
        OrchestratorObjectKind objectKind,
        string objectName,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        using var connection = _dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = @"
            DELETE FROM OrchestratorObjectAcls
            WHERE ObjectKind = @kind AND ObjectName = @name COLLATE NOCASE;";
        command.AddParam("@kind", objectKind.ToString());
        command.AddParam("@name", objectName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
