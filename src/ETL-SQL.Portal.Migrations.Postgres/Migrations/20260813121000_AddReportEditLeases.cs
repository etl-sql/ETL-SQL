using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(ETL_SQL.Portal.Data.PortalDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260813121000_AddReportEditLeases")]
    public partial class AddReportEditLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditSessionExpiresAtUtc",
                table: "ReportScriptDrafts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditSessionUserId",
                table: "ReportScriptDrafts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditSessionUserName",
                table: "ReportScriptDrafts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditSessionExpiresAtUtc",
                table: "Reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditSessionUserId",
                table: "Reports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditSessionUserName",
                table: "Reports",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EditSessionExpiresAtUtc", table: "ReportScriptDrafts");
            migrationBuilder.DropColumn(name: "EditSessionUserId", table: "ReportScriptDrafts");
            migrationBuilder.DropColumn(name: "EditSessionUserName", table: "ReportScriptDrafts");
            migrationBuilder.DropColumn(name: "EditSessionExpiresAtUtc", table: "Reports");
            migrationBuilder.DropColumn(name: "EditSessionUserId", table: "Reports");
            migrationBuilder.DropColumn(name: "EditSessionUserName", table: "Reports");
        }
    }
}
