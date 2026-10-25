using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAlertInlineDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // COMPAT_BREAK: 0.22 — obsolete inline delivery values are removed; named attachments remain.
            migrationBuilder.DropColumn(
                name: "recipient",
                table: "report_alerts");

            migrationBuilder.DropColumn(
                name: "smtp_alias",
                table: "report_alerts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recipient",
                table: "report_alerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "smtp_alias",
                table: "report_alerts",
                type: "TEXT",
                nullable: true);
        }
    }
}
