using System;
using System.IO;
using ETL_SQL.Common;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Data;

/// <summary>
/// Single point that selects and configures the EF Core provider for <see cref="PortalDbContext"/>.
/// Replaces the previously hardcoded <c>UseSqlite</c> calls so the portal state store can run on
/// SQLite (default, standalone) or PostgreSQL (shared, multi-node) by configuration.
/// </summary>
public static class PortalDatabase
{
    /// <summary>Assembly holding the PostgreSQL migration set (the SQLite set lives in the Data
    /// library, the default migrations assembly). Selected via MigrationsAssembly for Postgres.</summary>
    public const string PostgresMigrationsAssembly = "ETL-SQL.Portal.Migrations.Postgres";

    private static string? _lastConnectionString;
    private static readonly object _poolLock = new();

    /// <summary>
    /// Configures <paramref name="builder"/> for the provider named in <paramref name="config"/>'s
    /// <c>Database</c> section. SQLite derives its connection from <see cref="PortalConfig.DatabasePath"/>
    /// unless an explicit connection string is given; Postgres requires an explicit connection string.
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, PortalConfig config)
    {
        var provider = DatabaseProviderParser.Parse(config.Database.Provider);
        string connectionString;
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                connectionString = !string.IsNullOrWhiteSpace(config.Database.ConnectionString)
                    ? config.Database.ConnectionString!
                    : $"Data Source={Path.GetFullPath(config.DatabasePath)}";
                builder.UseSqlite(connectionString);
                break;

            case DatabaseProvider.Postgres:
                connectionString = config.Database.ConnectionString ?? string.Empty;
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        "Portal:Database:Provider=Postgres requires Portal:Database:ConnectionString to be set.");
                builder.UseNpgsql(connectionString, npg => npg.MigrationsAssembly(PostgresMigrationsAssembly));
                break;

            default:
                throw new InvalidOperationException($"Unsupported database provider: {provider}.");
        }

        lock (_poolLock)
        {
            if (_lastConnectionString != null && _lastConnectionString != connectionString)
            {
                try
                {
                    if (provider == DatabaseProvider.Sqlite)
                    {
                        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    }
                    else if (provider == DatabaseProvider.Postgres)
                    {
                        Npgsql.NpgsqlConnection.ClearAllPools();
                    }
                }
                catch
                {
                    // Suppress any pool clearing issues if not supported
                }
            }
            _lastConnectionString = connectionString;
        }

        return builder;
    }
}
