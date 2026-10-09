using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Orchestrator.Storage;

public static class OrchestratorSchemaMigration
{
    public static async Task MigrateLegacyTableAsync(
        IOrchestratorStoreDialect dialect,
        DbConnection connection,
        string oldTable,
        string newTable,
        CancellationToken ct = default)
    {
        try
        {
            var tables = await dialect.GetTableNamesAsync(connection, ct);
            if (tables.Contains(oldTable) && !tables.Contains(newTable))
            {
                await using var cmd = connection.CreateCommand();
                var tmpTable = $"_tmp_snake_{newTable}";
                cmd.CommandText = $"ALTER TABLE \"{oldTable}\" RENAME TO \"{tmpTable}\";";
                await cmd.ExecuteNonQueryAsync(ct);
                cmd.CommandText = $"ALTER TABLE \"{tmpTable}\" RENAME TO \"{newTable}\";";
                await cmd.ExecuteNonQueryAsync(ct);
            }

            tables = await dialect.GetTableNamesAsync(connection, ct);
            if (tables.Contains(newTable))
            {
                var cols = await dialect.GetExactColumnNamesAsync(connection, newTable, ct);
                foreach (var col in cols)
                {
                    var snakeCol = DatabaseNamingConvention.ToSnakeCase(col);
                    if (!string.Equals(col, snakeCol, StringComparison.Ordinal))
                    {
                        await using var cmd = connection.CreateCommand();
                        cmd.CommandText = $"ALTER TABLE \"{newTable}\" RENAME COLUMN \"{col}\" TO \"{snakeCol}\";";
                        try
                        {
                            await cmd.ExecuteNonQueryAsync(ct);
                        }
                        catch
                        {
                            // Ignore if column already renamed or provider limitation
                        }
                    }
                }
            }
        }
        catch
        {
            // Best effort backward-compatible migration
        }
    }
}
