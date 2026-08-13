using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedExecutionJobPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortalExecutionJobs_CompletedAt",
                table: "PortalExecutionJobs");

            migrationBuilder.DropIndex(
                name: "IX_PortalExecutionJobs_ReportId_Kind",
                table: "PortalExecutionJobs");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PortalExecutionJobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.Sql(
                "UPDATE \"PortalExecutionJobs\" AS jobs SET \"TenantId\" = reports.\"TenantId\" FROM \"Reports\" AS reports WHERE reports.\"Id\" = jobs.\"ReportId\"");

            migrationBuilder.CreateIndex(
                name: "IX_PortalExecutionJobs_TenantId_CompletedAt",
                table: "PortalExecutionJobs",
                columns: new[] { "TenantId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PortalExecutionJobs_TenantId_ReportId_Kind",
                table: "PortalExecutionJobs",
                columns: new[] { "TenantId", "ReportId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'Refresh' AND \"Status\" IN ('Pending', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortalExecutionJobs_TenantId_CompletedAt",
                table: "PortalExecutionJobs");

            migrationBuilder.DropIndex(
                name: "IX_PortalExecutionJobs_TenantId_ReportId_Kind",
                table: "PortalExecutionJobs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PortalExecutionJobs");

            migrationBuilder.CreateIndex(
                name: "IX_PortalExecutionJobs_CompletedAt",
                table: "PortalExecutionJobs",
                column: "CompletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PortalExecutionJobs_ReportId_Kind",
                table: "PortalExecutionJobs",
                columns: new[] { "ReportId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'Refresh' AND \"Status\" IN ('Pending', 'Running')");
        }
    }
}
