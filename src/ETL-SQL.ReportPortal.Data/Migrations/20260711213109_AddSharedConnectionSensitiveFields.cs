using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedConnectionSensitiveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SensitiveFieldsCsv",
                table: "PortalSharedConnections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SensitiveFieldsCsv",
                table: "PortalSharedConnections");
        }
    }
}
