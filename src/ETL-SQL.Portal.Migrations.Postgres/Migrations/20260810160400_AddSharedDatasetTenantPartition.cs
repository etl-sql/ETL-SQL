using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(ETL_SQL.Portal.Data.PortalDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260810160400_AddSharedDatasetTenantPartition")]
    public partial class AddSharedDatasetTenantPartition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Datasets_Name",
                table: "Datasets");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Datasets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.CreateIndex(
                name: "IX_Datasets_TenantId_Name",
                table: "Datasets",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Datasets_TenantId_Name",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Datasets");

            migrationBuilder.CreateIndex(
                name: "IX_Datasets_Name",
                table: "Datasets",
                column: "Name",
                unique: true);
        }
    }
}
