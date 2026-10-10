using System.Data;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ETL_SQL.Portal.Services;

public static class PiiColumnEncryptionMaintenance
{
    private sealed record ColumnSpec(string Table, string KeyColumn, string Column);

    private static readonly (Type EntityType, string Property)[] Properties =
    [
        (typeof(PortalUser), nameof(PortalUser.Email)),
        (typeof(PortalUser), nameof(PortalUser.NormalizedEmail)),
        (typeof(PortalUser), nameof(PortalUser.FirstName)),
        (typeof(PortalUser), nameof(PortalUser.LastName)),
        (typeof(PortalUser), nameof(PortalUser.PhoneNumber)),
        (typeof(Subscription), nameof(Subscription.Recipients)),
        (typeof(ReportAlert), nameof(ReportAlert.Recipient)),
        (typeof(SubscriptionDelivery), nameof(SubscriptionDelivery.Recipients))
    ];

    private static ColumnSpec ResolveColumn(PortalDbContext db, Type type, string propertyName)
    {
        var entity = db.Model.FindEntityType(type)
            ?? throw new InvalidOperationException($"PII entity mapping is missing: {type.Name}.");
        var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"PII table mapping is missing: {type.Name}.");
        var key = entity.FindPrimaryKey()?.Properties.Single()
            ?? throw new InvalidOperationException($"PII key mapping is missing: {type.Name}.");
        var column = entity.FindProperty(propertyName)?.GetColumnName(table)
            ?? throw new InvalidOperationException($"PII property mapping is missing: {type.Name}.{propertyName}.");
        var keyColumn = key.GetColumnName(table)
            ?? throw new InvalidOperationException($"PII key column mapping is missing: {type.Name}.");
        var tableName = QuoteIdentifier(table.Name);
        if (table.Schema is not null) tableName = QuoteIdentifier(table.Schema) + "." + tableName;
        return new ColumnSpec(tableName, QuoteIdentifier(keyColumn), QuoteIdentifier(column));
    }

    // Both Portal catalog providers (SQLite and PostgreSQL) use SQL double-quoted identifiers.
    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static async Task<int> EncryptExistingPlaintextAsync(
        PortalDbContext db,
        PortalPiiProtector protector,
        ILogger logger,
        CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
            await connection.OpenAsync(ct);

        try
        {
            var updated = 0;
            await using var transaction = await connection.BeginTransactionAsync(ct);
            foreach (var (entityType, property) in Properties)
            {
                var column = ResolveColumn(db, entityType, property);
                var rows = await ReadColumnAsync(connection, transaction, column, ct);
                foreach (var (id, storedValue) in rows)
                {
                    if (string.IsNullOrEmpty(storedValue)
                        || (storedValue.StartsWith("dp:", StringComparison.Ordinal)
                            && protector.IsEncrypted(storedValue)))
                    {
                        continue;
                    }

                    var plaintext = protector.Decrypt(storedValue);
                    var encrypted = protector.Encrypt(plaintext);
                    if (string.IsNullOrEmpty(encrypted) || encrypted == storedValue)
                        continue;

                    await UpdateColumnAsync(connection, transaction, column, id, encrypted, ct);
                    updated++;
                }
            }

            await transaction.CommitAsync(ct);
            if (updated > 0)
                logger.LogInformation("Encrypted {Count} existing plaintext PII database value(s).", updated);

            return updated;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task<List<(long Id, string? Value)>> ReadColumnAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        ColumnSpec column,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {column.KeyColumn}, {column.Column} FROM {column.Table} " +
            $"WHERE {column.Column} IS NOT NULL AND {column.Column} <> ''";

        var rows = new List<(long, string?)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add((Convert.ToInt64(reader.GetValue(0)), reader.GetString(1)));

        return rows;
    }

    private static async Task UpdateColumnAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        ColumnSpec column,
        long id,
        string value,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"UPDATE {column.Table} SET {column.Column} = @value WHERE {column.KeyColumn} = @id";

        var valueParameter = command.CreateParameter();
        valueParameter.ParameterName = "@value";
        valueParameter.Value = value;
        command.Parameters.Add(valueParameter);

        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "@id";
        idParameter.Value = id;
        command.Parameters.Add(idParameter);

        await command.ExecuteNonQueryAsync(ct);
    }
}
