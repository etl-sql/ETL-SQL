using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class TenantQualifyReportAlertNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_report_alerts_name",
                table: "report_alerts");

            migrationBuilder.AddColumn<string>(
                name: "tenant_id",
                table: "report_alerts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            // An alert belongs to its report's tenant; copy it so equal alert names in two tenants
            // stop colliding on what used to be a host-wide unique name.
            migrationBuilder.Sql(
                "UPDATE report_alerts SET tenant_id = " +
                "(SELECT reports.tenant_id FROM reports WHERE reports.id = report_alerts.report_id);");

            migrationBuilder.CreateIndex(
                name: "ix_report_alerts_tenant_id_name",
                table: "report_alerts",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_report_alerts_tenant_id_name",
                table: "report_alerts");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "report_alerts");

            migrationBuilder.CreateIndex(
                name: "ix_report_alerts_name",
                table: "report_alerts",
                column: "name",
                unique: true);
        }
    }
}
