using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedFolderPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Folders_Path",
                table: "Folders");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Folders",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_TenantId_Path",
                table: "Folders",
                columns: new[] { "TenantId", "Path" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Folders_TenantId_Path",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Folders");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_Path",
                table: "Folders",
                column: "Path",
                unique: true);
        }
    }
}
