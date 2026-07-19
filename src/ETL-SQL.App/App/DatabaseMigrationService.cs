using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace ETL_SQL.App
{
    /// <summary>
    /// Practical HA P1.3: <c>etl-sql admin migrate-database --from sqlite --to postgres [--dry-run]</c>.
    ///
    /// Copies the Portal and Orchestrator state out of their single-node SQLite files into the
    /// PostgreSQL deployment configured for HA (the <c>Portal:Database</c> /
    /// <c>Orchestrator:Database</c> connection strings introduced in P1.1/P1.2). The target schema is
    /// expected to <b>already exist</b> — the Portal schema is created by running its EF migrations
    /// against Postgres, and the Orchestrator schema by the store's own <c>InitializeAsync</c> — so this
    /// command is a lean, provider-neutral <i>row copy</i>, not a DDL tool.
    ///
    /// Because EF Core maps the same CLR model to different physical types per provider (e.g. a
    /// <c>bool</c> is <c>INTEGER</c> in SQLite but <c>boolean</c> in PostgreSQL; a <c>DateTime</c> is
    /// <c>TEXT</c> vs <c>timestamp</c>), every value is coerced to the <i>target</i> column's type
    /// before insertion. Foreign-key ordering is bypassed for the load with
    /// <c>session_replication_role = replica</c> (requires a privileged role), identity sequences are
    /// resynced afterward, and each table's row count is verified on both sides. Any mismatch
    /// <b>fails closed</b>: nothing is committed.
    /// </summary>
    internal static class DatabaseMigrationService
    {
        // Schema/bookkeeping tables we never copy: SQLite internals and EF's migration history (the
        // target's history is established when its migrations are applied, not copied from SQLite).
        private static readonly HashSet<string> SkipTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "sqlite_sequence", "sqlite_stat1", "sqlite_stat4", "__EFMigrationsHistory",
        };

        internal static async Task<int> RunAsync(CliContext ctx, ILogger logger, CancellationToken ct = default)
        {
            var config = Program.ServiceProvider.GetService<IConfiguration>()
                ?? new ConfigurationBuilder().Build();
            return await RunAsync(ctx, logger, config, AppContext.BaseDirectory, ct);
        }

        internal static async Task<int> RunAsync(
            CliContext ctx,
            ILogger logger,
            IConfiguration config,
            string baseDir,
            CancellationToken ct = default)
        {
            var from = (ctx.MigrateFrom ?? "sqlite").Trim().ToLowerInvariant();
            var to = (ctx.MigrateTo ?? "postgres").Trim().ToLowerInvariant();
            if (from != "sqlite" || to != "postgres")
            {
                logger.WriteLine(
                    $"Unsupported migration direction '--from {from} --to {to}'. Only 'sqlite' -> 'postgres' is supported.",
                    ConsoleColor.Red);
                return 1;
            }

            var portalSqlite = Resolve(config["Portal:DatabasePath"] ?? "./portal.db", baseDir);
            var orchSqlite = Resolve(
                config["Portal:Orchestrator:DatabasePath"]
                ?? config["Orchestrator:HistoryDbPath"]
                ?? config["Orchestrator:DatabasePath"]
                ?? "./etlsql.db", baseDir);
            var portalPg = config["Portal:Database:ConnectionString"];
            var orchPg = config["Orchestrator:Database:ConnectionString"];

            bool dryRun = ctx.MigrateDryRun;
            if (dryRun)
            {
                logger.WriteLine(
                    "DRY RUN — counts, values, and target schema are verified, but no data is written.",
                    ConsoleColor.Cyan);
            }
            else
            {
                logger.WriteLine(
                    "PREFLIGHT — validating both stores before any target tables are cleared.",
                    ConsoleColor.Cyan);
                bool preflightOk = true;
                preflightOk &= await MigrateStoreAsync("Portal", portalSqlite, portalPg, true, logger, ct);
                preflightOk &= await MigrateStoreAsync("Orchestrator", orchSqlite, orchPg, true, logger, ct);
                if (!preflightOk)
                {
                    logger.WriteLine(
                        "Migration preflight FAILED — no target data was changed.",
                        ConsoleColor.Red);
                    return 1;
                }

                logger.WriteLine(
                    "LIVE migration — target tables are CLEARED and repopulated from SQLite.",
                    ConsoleColor.Yellow);
            }

            bool ok = true;
            ok &= await MigrateStoreAsync("Portal", portalSqlite, portalPg, dryRun, logger, ct);
            ok &= await MigrateStoreAsync("Orchestrator", orchSqlite, orchPg, dryRun, logger, ct);

            if (!ok)
            {
                logger.WriteLine(
                    "Migration FAILED — each failing store was rolled back. A store committed before a later " +
                    "store failed may already contain migrated data; fix the error and rerun the idempotent migration.",
                    ConsoleColor.Red);
                return 1;
            }

            logger.WriteLine(
                dryRun
                    ? "Dry run complete: source and target schemas are compatible."
                    : "Migration complete. Switch the node's Provider to 'Postgres' to cut over.",
                ConsoleColor.Green);
            return 0;
        }

        /// <summary>Migrates one store; returns false on any problem (including a missing target).</summary>
        private static async Task<bool> MigrateStoreAsync(
            string label, string sqlitePath, string? pgConnString, bool dryRun, ILogger logger, CancellationToken ct)
        {
            if (!File.Exists(sqlitePath))
            {
                logger.WriteLine($"[{label}] no SQLite database at '{sqlitePath}' — nothing to migrate; skipping.", ConsoleColor.Gray);
                return true;
            }
            if (string.IsNullOrWhiteSpace(pgConnString))
            {
                logger.WriteLine(
                    $"[{label}] target PostgreSQL ConnectionString is not configured — set it before migrating. Failing closed.",
                    ConsoleColor.Red);
                return false;
            }

            logger.WriteLine($"[{label}] migrating '{sqlitePath}' -> PostgreSQL …", ConsoleColor.White);
            try
            {
                var result = await MigrateDatabaseAsync(sqlitePath, pgConnString!, dryRun, logger, label, ct);
                return result.Success;
            }
            catch (Exception ex)
            {
                logger.WriteLine($"[{label}] migration error: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        internal readonly record struct MigrationResult(bool Success, IReadOnlyDictionary<string, long> RowsPerTable);

        /// <summary>
        /// Testable core: copy every (non-skipped) table from a SQLite file into a pre-existing Postgres
        /// schema, coercing each value to the target column type, then verify row counts. The whole load
        /// runs in a single transaction so a verification failure rolls everything back (fail closed).
        /// </summary>
        internal static async Task<MigrationResult> MigrateDatabaseAsync(
            string sqlitePath, string pgConnString, bool dryRun, ILogger logger, string label, CancellationToken ct = default)
        {
            var rows = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            bool ok = true;

            await using var src = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
            await src.OpenAsync(ct);

            await using var dst = new NpgsqlConnection(pgConnString);
            await dst.OpenAsync(ct);

            var tables = await GetSqliteTablesAsync(src, ct);
            if (tables.Count == 0)
            {
                logger.WriteLine($"[{label}] source has no user tables.", ConsoleColor.Gray);
                return new MigrationResult(true, rows);
            }

            // FK ordering is bypassed for the load; on a non-superuser role this SET fails — surface it
            // clearly rather than letting per-row FK violations cascade into a confusing failure.
            await using var tx = dryRun ? null : await dst.BeginTransactionAsync(ct);
            if (!dryRun)
            {
                try
                {
                    await ExecAsync(dst, tx, "SET session_replication_role = replica;", ct);
                }
                catch (PostgresException pe)
                {
                    logger.WriteLine(
                        $"[{label}] could not disable FK enforcement (session_replication_role): {pe.MessageText}. " +
                        "Run the migration as a privileged role (e.g. the database owner / superuser).",
                        ConsoleColor.Red);
                    return new MigrationResult(false, rows);
                }
            }

            // First pass: clear targets (live only) in reverse so children go before parents even though
            // FK triggers are disabled — keeps the operation sane if replica role was a no-op.
            if (!dryRun)
            {
                foreach (var table in Enumerable.Reverse(tables))
                {
                    var target = await GetPostgresTableAsync(dst, tx, table, ct);
                    if (target is null) continue;
                    await ExecAsync(dst, tx, $"DELETE FROM {QuoteIdent(target.Name)};", ct);
                }
            }

            foreach (var table in tables)
            {
                ct.ThrowIfCancellationRequested();

                var target = await GetPostgresTableAsync(dst, tx, table, ct);
                if (target is null)
                {
                    logger.WriteLine(
                        $"[{label}] target table '{table}' is missing (apply the schema/migrations first). Failing closed.",
                        ConsoleColor.Red);
                    ok = false;
                    continue;
                }

                var sourceCols = await GetSqliteColumnsAsync(src, table, ct);
                var sourceColSet = sourceCols.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var requiredMissing = target.Columns.Values
                    .Where(c => !sourceColSet.Contains(c.Name)
                        && !c.IsNullable
                        && !c.HasDefault
                        && !c.IsIdentity)
                    .Select(c => c.Name)
                    .ToList();
                if (requiredMissing.Count > 0)
                {
                    logger.WriteLine(
                        $"[{label}] table '{table}': source is missing required target column(s): " +
                        $"{string.Join(", ", requiredMissing)}. Failing closed.",
                        ConsoleColor.Red);
                    ok = false;
                    continue;
                }

                // Copy only columns present on both sides; target-only nullable/defaulted columns use
                // the target schema's default.
                var cols = sourceCols
                    .Where(c => target.Columns.ContainsKey(c))
                    .Select(c => new ColumnMapping(c, target.Columns[c]))
                    .ToList();
                if (cols.Count == 0)
                {
                    logger.WriteLine($"[{label}] table '{table}': no overlapping columns; skipping.", ConsoleColor.Yellow);
                    ok = false;
                    continue;
                }

                long sourceCount = await ScalarLongAsync(src, $"SELECT COUNT(*) FROM {QuoteIdent(table)};", ct);

                if (sourceCount > 0)
                {
                    if (dryRun)
                        await ValidateTableValuesAsync(src, table, cols, ct);
                    else
                        await CopyTableAsync(src, dst, tx!, table, target.Name, cols, ct);
                }

                long targetCount = dryRun
                    ? 0
                    : await ScalarLongAsync(dst, tx, $"SELECT COUNT(*) FROM {QuoteIdent(target.Name)};", ct);

                rows[table] = sourceCount;

                if (dryRun)
                {
                    logger.WriteLine($"[{label}]   {table}: {sourceCount} row(s) ready, {cols.Count} column(s) mapped.", ConsoleColor.Gray);
                }
                else if (targetCount != sourceCount)
                {
                    logger.WriteLine(
                        $"[{label}]   {table}: row-count mismatch (source {sourceCount}, target {targetCount}). Failing closed.",
                        ConsoleColor.Red);
                    ok = false;
                }
                else
                {
                    logger.WriteLine($"[{label}]   {table}: {sourceCount} row(s) copied and verified.", ConsoleColor.Gray);
                }
            }

            if (!dryRun && ok)
            {
                // Identity columns were inserted with explicit values; advance their sequences so future
                // inserts don't collide with copied keys.
                await ResyncIdentitySequencesAsync(dst, tx!, tables, ct);
                await ExecAsync(dst, tx, "SET session_replication_role = origin;", ct);
                await tx!.CommitAsync(ct);
            }
            else if (!dryRun)
            {
                await tx!.RollbackAsync(ct);
            }

            return new MigrationResult(ok, rows);
        }

        // ── Per-table copy ───────────────────────────────────────────────────────────

        private sealed record TargetColumn(
            string Name,
            string DataType,
            bool IsNullable,
            bool HasDefault,
            bool IsIdentity);

        private sealed record TargetTable(
            string Name,
            IReadOnlyDictionary<string, TargetColumn> Columns);

        private sealed record ColumnMapping(string SourceName, TargetColumn Target);

        private static async Task CopyTableAsync(
            SqliteConnection src, NpgsqlConnection dst, DbTransaction tx, string sourceTable,
            string targetTable, List<ColumnMapping> cols, CancellationToken ct)
        {
            var colList = string.Join(", ", cols.Select(c => QuoteIdent(c.Target.Name)));
            var paramList = string.Join(", ", cols.Select((_, i) => "$" + (i + 1)));
            var insertSql = $"INSERT INTO {QuoteIdent(targetTable)} ({colList}) VALUES ({paramList});";

            // Resolve each parameter's type from the target column up front: NpgsqlCommand.Prepare
            // requires every parameter to carry a type before the first row is seen.
            var dbTypes = cols.Select(c => ResolveDbType(c.Target.DataType)).ToArray();

            await using var insert = new NpgsqlCommand(insertSql, dst, (NpgsqlTransaction)tx);
            foreach (var dbType in dbTypes)
                insert.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = dbType });
            await insert.PrepareAsync(ct);

            await using var read = src.CreateCommand();
            read.CommandText =
                $"SELECT {string.Join(", ", cols.Select(c => QuoteIdent(c.SourceName)))} " +
                $"FROM {QuoteIdent(sourceTable)};";
            await using var reader = await read.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                for (int i = 0; i < cols.Count; i++)
                {
                    var raw = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    insert.Parameters[i].Value = raw is null ? DBNull.Value : CoerceValue(raw, dbTypes[i]);
                }
                await insert.ExecuteNonQueryAsync(ct);
            }
        }

        private static async Task ValidateTableValuesAsync(
            SqliteConnection src, string table, List<ColumnMapping> cols, CancellationToken ct)
        {
            var dbTypes = cols.Select(c => ResolveDbType(c.Target.DataType)).ToArray();
            await using var read = src.CreateCommand();
            read.CommandText =
                $"SELECT {string.Join(", ", cols.Select(c => QuoteIdent(c.SourceName)))} " +
                $"FROM {QuoteIdent(table)};";
            await using var reader = await read.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                for (int i = 0; i < cols.Count; i++)
                {
                    if (!reader.IsDBNull(i))
                        _ = CoerceValue(reader.GetValue(i), dbTypes[i]);
                }
            }
        }

        /// <summary>Maps a PostgreSQL <c>data_type</c> string to the NpgsqlDbType to bind a value as.</summary>
        private static NpgsqlDbType ResolveDbType(string pgType)
        {
            // Normalize "timestamp without time zone", "character varying", etc. to a base keyword.
            var t = pgType.ToLowerInvariant();
            if (t.Contains("bool")) return NpgsqlDbType.Boolean;
            if (t.Contains("timestamptz") || t.Contains("timestamp with time zone")) return NpgsqlDbType.TimestampTz;
            if (t.Contains("timestamp")) return NpgsqlDbType.Timestamp;
            if (t == "date") return NpgsqlDbType.Date;
            if (t.Contains("uuid")) return NpgsqlDbType.Uuid;
            if (t.Contains("numeric") || t.Contains("decimal") || t == "money") return NpgsqlDbType.Numeric;
            if (t.Contains("double") || t == "real" || t.Contains("float")) return NpgsqlDbType.Double;
            if (t == "bigint" || t == "int8") return NpgsqlDbType.Bigint;
            if (t.Contains("smallint") || t == "int2") return NpgsqlDbType.Smallint;
            if (t.Contains("int")) return NpgsqlDbType.Integer;
            if (t == "bytea") return NpgsqlDbType.Bytea;
            if (t == "jsonb") return NpgsqlDbType.Jsonb;
            if (t == "json") return NpgsqlDbType.Json;
            return NpgsqlDbType.Text; // text / varchar / char fallback
        }

        /// <summary>Coerces a (non-null) SQLite value to the CLR type expected by the target PG type.</summary>
        private static object CoerceValue(object raw, NpgsqlDbType dbType) => dbType switch
        {
            NpgsqlDbType.Boolean => ToBool(raw),
            NpgsqlDbType.TimestampTz => DateTime.SpecifyKind(ParseDateTime(raw), DateTimeKind.Utc),
            NpgsqlDbType.Timestamp => DateTime.SpecifyKind(ParseDateTime(raw), DateTimeKind.Unspecified),
            NpgsqlDbType.Date => ParseDateTime(raw).Date,
            NpgsqlDbType.Uuid => raw is Guid g ? g : Guid.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!),
            NpgsqlDbType.Numeric => Convert.ToDecimal(raw, CultureInfo.InvariantCulture),
            NpgsqlDbType.Double => Convert.ToDouble(raw, CultureInfo.InvariantCulture),
            NpgsqlDbType.Bigint => Convert.ToInt64(raw, CultureInfo.InvariantCulture),
            NpgsqlDbType.Smallint => Convert.ToInt16(raw, CultureInfo.InvariantCulture),
            NpgsqlDbType.Integer => Convert.ToInt32(raw, CultureInfo.InvariantCulture),
            NpgsqlDbType.Bytea => raw is byte[] b ? b : Convert.FromBase64String(Convert.ToString(raw, CultureInfo.InvariantCulture)!),
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture)!, // text / json / jsonb
        };

        private static bool ToBool(object raw) => raw switch
        {
            bool b => b,
            long l => l != 0,
            int i => i != 0,
            double d => d != 0,
            string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0,
        };

        private static DateTime ParseDateTime(object raw) => raw switch
        {
            DateTime dt => dt,
            string s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            long ticks => DateTimeOffset.FromUnixTimeSeconds(ticks).UtcDateTime,
            _ => Convert.ToDateTime(raw, CultureInfo.InvariantCulture),
        };

        // ── Schema introspection ─────────────────────────────────────────────────────

        private static async Task<List<string>> GetSqliteTablesAsync(SqliteConnection src, CancellationToken ct)
        {
            var tables = new List<string>();
            await using var cmd = src.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (!SkipTables.Contains(name)) tables.Add(name);
            }
            return tables;
        }

        private static async Task<List<string>> GetSqliteColumnsAsync(SqliteConnection src, string table, CancellationToken ct)
        {
            var cols = new List<string>();
            await using var cmd = src.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({QuoteIdent(table)});";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                cols.Add(reader.GetString(reader.GetOrdinal("name")));
            return cols;
        }

        /// <summary>Resolves the target's physical table/column names and column metadata.</summary>
        private static async Task<TargetTable?> GetPostgresTableAsync(
            NpgsqlConnection dst, DbTransaction? tx, string table, CancellationToken ct)
        {
            var cols = new Dictionary<string, TargetColumn>(StringComparer.OrdinalIgnoreCase);
            string? actualTable = null;
            await using var cmd = new NpgsqlCommand(
                "SELECT table_name, column_name, data_type, is_nullable, column_default, is_identity " +
                "FROM information_schema.columns " +
                "WHERE table_schema = current_schema() AND lower(table_name) = lower(@t);", dst, (NpgsqlTransaction?)tx);
            cmd.Parameters.AddWithValue("@t", table);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                actualTable ??= reader.GetString(0);
                var column = new TargetColumn(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3).Equals("YES", StringComparison.OrdinalIgnoreCase),
                    !reader.IsDBNull(4),
                    reader.GetString(5).Equals("YES", StringComparison.OrdinalIgnoreCase));
                cols[column.Name] = column;
            }
            return actualTable is null ? null : new TargetTable(actualTable, cols);
        }

        /// <summary>
        /// Advances each table's identity/serial sequence past the max copied key, so post-cutover
        /// inserts don't collide. Only auto-increment integer columns are touched (a blind MAX() over an
        /// arbitrary column would mis-type against a bigint sequence).
        /// </summary>
        private static async Task ResyncIdentitySequencesAsync(
            NpgsqlConnection dst, DbTransaction tx, List<string> tables, CancellationToken ct)
        {
            var owned = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);

            // Identity columns (GENERATED ... AS IDENTITY) and serial columns (DEFAULT nextval(...)).
            var identity = new List<(string Table, string Column)>();
            await using (var cmd = new NpgsqlCommand(
                "SELECT table_name, column_name FROM information_schema.columns " +
                "WHERE table_schema = current_schema() " +
                "AND (is_identity = 'YES' OR column_default LIKE 'nextval(%');", dst, (NpgsqlTransaction)tx))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var table = reader.GetString(0);
                    if (owned.Contains(table)) identity.Add((table, reader.GetString(1)));
                }
            }

            foreach (var (table, col) in identity)
            {
                // is_called=true only when rows exist, so an empty table's sequence still starts at 1.
                var sql =
                    $"SELECT setval(seq, GREATEST((SELECT COALESCE(MAX({QuoteIdent(col)}), 0) FROM {QuoteIdent(table)}), 1), " +
                    $"(SELECT COUNT(*) FROM {QuoteIdent(table)}) > 0) " +
                    $"FROM (SELECT pg_get_serial_sequence(quote_ident(@tbl), @col) AS seq) s WHERE seq IS NOT NULL;";
                await using var cmd = new NpgsqlCommand(sql, dst, (NpgsqlTransaction)tx);
                cmd.Parameters.AddWithValue("@tbl", table);
                cmd.Parameters.AddWithValue("@col", col);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        // ── Small ADO helpers ────────────────────────────────────────────────────────

        private static async Task ExecAsync(NpgsqlConnection dst, DbTransaction? tx, string sql, CancellationToken ct)
        {
            await using var cmd = new NpgsqlCommand(sql, dst, (NpgsqlTransaction?)tx);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static async Task<long> ScalarLongAsync(SqliteConnection conn, string sql, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }

        private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, DbTransaction? tx, string sql, CancellationToken ct)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, (NpgsqlTransaction?)tx);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }

        /// <summary>Double-quotes a SQL identifier, escaping embedded quotes. Works for both providers.</summary>
        private static string QuoteIdent(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";

        private static string Resolve(string p, string baseDir) =>
            Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p));
    }
}
