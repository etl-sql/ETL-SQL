using System;
using ETL_SQL.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETL_SQL.ReportPortal.Data;

// Used by the `dotnet ef` CLI tools only — not registered in DI.
// Provider is chosen by the ETL_SQL_DB_PROVIDER env var (default Sqlite) so the same context can
// generate SQLite migrations (default) and, in P1.2, PostgreSQL migrations:
//   ETL_SQL_DB_PROVIDER=Postgres dotnet ef migrations add <Name> -o Data/Migrations/Postgres
public class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var provider = DatabaseProviderParser.Parse(Environment.GetEnvironmentVariable("ETL_SQL_DB_PROVIDER"));
        var builder = new DbContextOptionsBuilder<PortalDbContext>();

        switch (provider)
        {
            case DatabaseProvider.Postgres:
                builder.UseNpgsql(
                    Environment.GetEnvironmentVariable("ETL_SQL_DB_CONNECTION")
                    ?? "Host=localhost;Database=portal_design;Username=postgres;Password=postgres");
                break;
            default:
                builder.UseSqlite("Data Source=portal-design.db");
                break;
        }

        return new PortalDbContext(builder.Options);
    }
}
