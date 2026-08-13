using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedReportPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Reports",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_TenantId_FolderId",
                table: "Reports",
                columns: new[] { "TenantId", "FolderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reports_TenantId_FolderId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Reports");
        }
    }
}
