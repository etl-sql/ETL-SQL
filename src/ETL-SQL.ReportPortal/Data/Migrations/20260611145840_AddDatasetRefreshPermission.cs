using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetRefreshPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only migration: a new Refresh permission is inserted between Viewer and Editor in the
            // DatasetPermission enum. Renumber existing DatasetAcls grants to preserve their meaning.
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

        /// <inheritdoc />
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
