using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDurablePortalExecutionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortalExecutionJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ManifestPath = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalExecutionJobs", x => x.Id);
                });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortalExecutionJobs");
        }
    }
}
