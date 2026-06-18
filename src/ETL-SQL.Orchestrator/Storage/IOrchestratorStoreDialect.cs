using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// Provider-specific seam for the relational Orchestrator store. Everything portable across SQLite
    /// and PostgreSQL stays in shared SQL (parameters use the <c>@name</c> form both accept; identity
    /// upserts use <c>ON CONFLICT ... DO UPDATE</c>/<c>DO NOTHING</c>; case-insensitive lookups use
    /// <c>COLLATE NOCASE</c>, which PostgreSQL gets via the collation in <see cref="CollationDdl"/>).
    /// Only the genuinely divergent constructs are abstracted here.
    /// </summary>
    public interface IOrchestratorStoreDialect
    {
        /// <summary>Creates a new, unopened provider connection to the configured database.</summary>
        DbConnection CreateConnection();

        /// <summary>
        /// SQL run once before the CREATE TABLE statements (e.g. PostgreSQL creates the case-insensitive
        /// <c>nocase</c> collation so <c>COLLATE NOCASE</c> resolves). Empty when nothing is needed.
        /// </summary>
        string CollationDdl { get; }

        /// <summary>
        /// Optional provider-specific SQL that serializes schema bootstrap before the cluster-lock
        /// table itself exists. Empty when provider DDL is already safe for concurrent startup.
        /// </summary>
        string SchemaInitializationLockSql { get; }

        /// <summary>Companion unlock SQL for <see cref="SchemaInitializationLockSql"/>.</summary>
        string SchemaInitializationUnlockSql { get; }

        /// <summary>Column definition for an auto-incrementing integer primary key (the <c>Id</c> column).</summary>
        string AutoIncrementPrimaryKey { get; }

        /// <summary>Provider SQL that returns the current UTC timestamp from the database clock.</summary>
        string UtcNowSql { get; }

        /// <summary>Returns the existing column names of <paramref name="table"/> (for the additive
        /// ALTER-TABLE schema-migration sweep). SQLite uses <c>PRAGMA table_info</c>; PostgreSQL uses
        /// <c>information_schema.columns</c>.</summary>
        Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string table, CancellationToken ct = default);

        /// <summary>
        /// Turns an INSERT (without trailing semicolon) into one that yields the generated identity as a
        /// scalar. SQLite appends <c>; SELECT last_insert_rowid();</c>; PostgreSQL appends
        /// <c> RETURNING &lt;idColumn&gt;</c>.
        /// </summary>
        string InsertReturningId(string insertWithoutSemicolon, string idColumn);
    }

    /// <summary>Provider-neutral parameter binding (DbCommand has no AddWithValue).</summary>
    public static class DbCommandExtensions
    {
        /// <summary>Adds a parameter; a null value is bound as <see cref="System.DBNull.Value"/>.</summary>
        public static DbParameter AddParam(this DbCommand command, string name, object? value)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? System.DBNull.Value;
            command.Parameters.Add(p);
            return p;
        }
    }
}
