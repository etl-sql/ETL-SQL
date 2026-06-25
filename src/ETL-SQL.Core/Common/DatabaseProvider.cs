using System;

namespace ETL_SQL.Common;
/// <summary>
/// Relational database backends ETL-SQL's Portal and Orchestrator state stores can run against.
/// Shared by both subsystems so provider selection is parsed one way everywhere.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>Single-file SQLite (the default; the only fully-supported standalone backend).</summary>
    Sqlite,

    /// <summary>PostgreSQL — for shared, multi-node (Practical High Availability) deployments.</summary>
    Postgres
}

/// <summary>Parses the configured provider name into <see cref="DatabaseProvider"/>.</summary>
public static class DatabaseProviderParser
{
    /// <summary>
    /// Resolves a provider name. Null/empty defaults to <see cref="DatabaseProvider.Sqlite"/>.
    /// Accepts common spellings (sqlite/sqlite3, postgres/postgresql/npgsql/pgsql). Throws on
    /// anything else so a typo fails fast instead of silently falling back.
    /// </summary>
    public static DatabaseProvider Parse(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "sqlite" or "sqlite3" => DatabaseProvider.Sqlite,
            "postgres" or "postgresql" or "npgsql" or "pgsql" => DatabaseProvider.Postgres,
            _ => throw new ArgumentException(
                $"Unknown database provider '{value}'. Supported values: Sqlite, Postgres.")
        };
}
