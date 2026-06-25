using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalExecutionResourceMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CpuTimeSeconds",
                table: "PortalExecutionJobs",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<long>(
                name: "PeakMemoryBytes",
                table: "PortalExecutionJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RowsProcessed",
                table: "PortalExecutionJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuTimeSeconds",
                table: "PortalExecutionJobs");

            migrationBuilder.DropColumn(
                name: "PeakMemoryBytes",
                table: "PortalExecutionJobs");

            migrationBuilder.DropColumn(
                name: "RowsProcessed",
                table: "PortalExecutionJobs");
        }
    }
}
