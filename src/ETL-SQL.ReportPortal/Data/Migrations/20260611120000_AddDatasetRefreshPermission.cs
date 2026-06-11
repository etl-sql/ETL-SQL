using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    [DbContext(typeof(PortalDbContext))]
    [Migration("20260611120000_AddDatasetRefreshPermission")]
    public partial class AddDatasetRefreshPermission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve existing meanings while inserting Refresh between Viewer and Editor.
            migrationBuilder.Sql(
                """
                UPDATE DatasetAcls
                SET Permission = CASE Permission
                    WHEN 1 THEN 2
                    WHEN 2 THEN 3
                    ELSE Permission
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refresh-only grants degrade to Viewer when returning to the three-level model.
            migrationBuilder.Sql(
                """
                UPDATE DatasetAcls
                SET Permission = CASE Permission
                    WHEN 1 THEN 0
                    WHEN 2 THEN 1
                    WHEN 3 THEN 2
                    ELSE Permission
                END
                """);
        }
    }
}
