using System.Data;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public static class PiiColumnEncryptionMaintenance
{
    private sealed record ColumnSpec(string Table, string KeyColumn, string Column);

    private static readonly ColumnSpec[] Columns =
    [
        new("AspNetUsers", "Id", "Email"),
        new("AspNetUsers", "Id", "NormalizedEmail"),
        new("AspNetUsers", "Id", "FirstName"),
        new("AspNetUsers", "Id", "LastName"),
        new("AspNetUsers", "Id", "PhoneNumber"),
        new("Subscriptions", "Id", "Recipients"),
        new("ReportAlerts", "Id", "Recipient"),
        new("SubscriptionDeliveries", "Id", "Recipients")
    ];

    public static async Task<int> EncryptExistingPlaintextAsync(
        PortalDbContext db,
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
            foreach (var column in Columns)
            {
                var rows = await ReadColumnAsync(connection, transaction, column, ct);
                foreach (var (id, storedValue) in rows)
                {
                    if (string.IsNullOrEmpty(storedValue)
                        || (storedValue.StartsWith("dp:", StringComparison.Ordinal)
                            && PortalEncryptionProvider.IsEncrypted(storedValue)))
                    {
                        continue;
                    }

                    var plaintext = PortalEncryptionProvider.Decrypt(storedValue);
                    var encrypted = PortalEncryptionProvider.Encrypt(plaintext);
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
            $"SELECT \"{column.KeyColumn}\", \"{column.Column}\" FROM \"{column.Table}\" " +
            $"WHERE \"{column.Column}\" IS NOT NULL AND \"{column.Column}\" <> ''";

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
            $"UPDATE \"{column.Table}\" SET \"{column.Column}\" = @value WHERE \"{column.KeyColumn}\" = @id";

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
