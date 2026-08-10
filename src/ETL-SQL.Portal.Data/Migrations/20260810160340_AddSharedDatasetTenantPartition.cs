using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
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
                type: "TEXT",
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
