using System;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// Creates the Orchestrator job/bundle/lineage store for the configured database provider
    /// (<c>Orchestrator:Database:Provider</c>). Callers obtain a store through this seam instead of
    /// constructing <see cref="SQLiteJobHistoryStore"/> directly, so the provider (SQLite default or
    /// PostgreSQL) can change without touching every call site.
    /// </summary>
    public interface IOrchestratorStoreFactory
    {
        /// <summary>
        /// Creates a job-history store. For SQLite, <paramref name="dbPath"/> selects the database file
        /// (default path when null); for PostgreSQL it is ignored — the connection comes from
        /// <c>Orchestrator:Database:ConnectionString</c>.
        /// </summary>
        IJobHistoryStore Create(string? dbPath = null);
    }

    /// <inheritdoc />
    public sealed class OrchestratorStoreFactory : IOrchestratorStoreFactory
    {
        private readonly DatabaseProvider _provider;
        private readonly string? _connectionString;

        public OrchestratorStoreFactory(IConfiguration configuration)
        {
            _provider = DatabaseProviderParser.Parse(configuration["Orchestrator:Database:Provider"]);
            _connectionString = configuration["Orchestrator:Database:ConnectionString"];
        }

        public IJobHistoryStore Create(string? dbPath = null)
        {
            if (_provider == DatabaseProvider.Postgres)
            {
                var conn = _connectionString;
                if (string.IsNullOrWhiteSpace(conn))
                    throw new InvalidOperationException(
                        "Orchestrator:Database:Provider=Postgres requires Orchestrator:Database:ConnectionString to be set.");
                return new RelationalJobHistoryStore(new NpgsqlOrchestratorDialect(conn!));
            }

            return new SQLiteJobHistoryStore(dbPath);
        }
    }
}
