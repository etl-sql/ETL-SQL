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
        /// <summary>The configured provider used by stores created by this factory.</summary>
        DatabaseProvider Provider { get; }

        /// <summary>
        /// Creates a job-history store. For SQLite, <paramref name="dbPath"/> selects the database file
        /// (default path when null); for PostgreSQL it is ignored — the connection comes from
        /// <c>Orchestrator:Database:ConnectionString</c>.
        /// </summary>
        IJobHistoryStore Create(string? dbPath = null);

        /// <summary>Creates the sandbox admission ledger on the same configured authority.</summary>
        ISandboxAdmissionLedger CreateSandboxAdmissionLedger(string? dbPath = null) =>
            throw new NotSupportedException("This store factory does not provide a sandbox admission authority.");

        /// <summary>Creates the counts-only tenant metering ledger on the configured authority.</summary>
        ITenantMeteringLedger CreateTenantMeteringLedger(string? dbPath = null) =>
            throw new NotSupportedException("This store factory does not provide a tenant metering authority.");
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

        public DatabaseProvider Provider => _provider;

        public IJobHistoryStore Create(string? dbPath = null)
        {
            return _provider == DatabaseProvider.Postgres
                ? new RelationalJobHistoryStore(CreateDialect(dbPath))
                : new SQLiteJobHistoryStore(dbPath);
        }

        public ISandboxAdmissionLedger CreateSandboxAdmissionLedger(string? dbPath = null) =>
            new RelationalSandboxAdmissionLedger(CreateDialect(dbPath));

        public ITenantMeteringLedger CreateTenantMeteringLedger(string? dbPath = null) =>
            new RelationalTenantMeteringLedger(CreateDialect(dbPath));

        private IOrchestratorStoreDialect CreateDialect(string? dbPath)
        {
            if (_provider != DatabaseProvider.Postgres)
                return new SqliteOrchestratorDialect(
                    $"Data Source={dbPath ?? SQLiteJobHistoryStore.DefaultDbPath()}");

            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException(
                    "Orchestrator:Database:Provider=Postgres requires Orchestrator:Database:ConnectionString to be set.");
            return new NpgsqlOrchestratorDialect(_connectionString);
        }
    }
}
