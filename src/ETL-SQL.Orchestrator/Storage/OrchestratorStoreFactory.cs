using System;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Orchestrator.Storage
{
    /// <summary>
    /// Creates the Orchestrator job/bundle/lineage store for a given database path, honoring the
    /// configured database provider (<c>Orchestrator:Database:Provider</c>). Callers obtain a store
    /// through this seam instead of constructing <see cref="SQLiteJobHistoryStore"/> directly, so the
    /// provider can change (PostgreSQL, Practical HA P1.2) without touching every call site.
    /// </summary>
    public interface IOrchestratorStoreFactory
    {
        /// <summary>
        /// Creates a job-history store for <paramref name="dbPath"/> (or the default path when null),
        /// using the configured provider.
        /// </summary>
        IJobHistoryStore Create(string? dbPath = null);
    }

    /// <inheritdoc />
    public sealed class OrchestratorStoreFactory : IOrchestratorStoreFactory
    {
        private readonly DatabaseProvider _provider;

        public OrchestratorStoreFactory(IConfiguration configuration)
        {
            _provider = DatabaseProviderParser.Parse(configuration["Orchestrator:Database:Provider"]);
        }

        public IJobHistoryStore Create(string? dbPath = null)
        {
            if (_provider == DatabaseProvider.Postgres)
                throw new NotSupportedException(
                    "The Orchestrator PostgreSQL state store is not yet available — its hand-written SQL " +
                    "is ported and verified in Practical HA P1.2. Set Orchestrator:Database:Provider=Sqlite.");

            return new SQLiteJobHistoryStore(dbPath);
        }
    }
}
