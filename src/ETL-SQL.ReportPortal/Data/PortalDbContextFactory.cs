using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ETL_SQL.ReportPortal.Data;

// Used by `dotnet ef` CLI tools only — not registered in DI.
public class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite("Data Source=portal-design.db")
            .Options;
        return new PortalDbContext(options);
    }
}
