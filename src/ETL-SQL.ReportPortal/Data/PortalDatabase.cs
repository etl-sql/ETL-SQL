using System;
using System.IO;
using ETL_SQL.Common;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Data;

/// <summary>
/// Single point that selects and configures the EF Core provider for <see cref="PortalDbContext"/>.
/// Replaces the previously hardcoded <c>UseSqlite</c> calls so the portal state store can run on
/// SQLite (default, standalone) or PostgreSQL (shared, multi-node) by configuration.
/// </summary>
public static class PortalDatabase
{
    /// <summary>Assembly holding the PostgreSQL migration set (the SQLite set lives in the Data
    /// library, the default migrations assembly). Selected via MigrationsAssembly for Postgres.</summary>
    public const string PostgresMigrationsAssembly = "ETL-SQL.ReportPortal.Migrations.Postgres";

    /// <summary>
    /// Configures <paramref name="builder"/> for the provider named in <paramref name="config"/>'s
    /// <c>Database</c> section. SQLite derives its connection from <see cref="PortalConfig.DatabasePath"/>
    /// unless an explicit connection string is given; Postgres requires an explicit connection string.
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, PortalConfig config)
    {
        var provider = DatabaseProviderParser.Parse(config.Database.Provider);
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                var sqliteConn = !string.IsNullOrWhiteSpace(config.Database.ConnectionString)
                    ? config.Database.ConnectionString!
                    : $"Data Source={Path.GetFullPath(config.DatabasePath)}";
                builder.UseSqlite(sqliteConn);
                break;

            case DatabaseProvider.Postgres:
                var pgConn = config.Database.ConnectionString;
                if (string.IsNullOrWhiteSpace(pgConn))
                    throw new InvalidOperationException(
                        "Portal:Database:Provider=Postgres requires Portal:Database:ConnectionString to be set.");
                builder.UseNpgsql(pgConn!, npg => npg.MigrationsAssembly(PostgresMigrationsAssembly));
                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider: {provider}.");
        }

        return builder;
    }
}
