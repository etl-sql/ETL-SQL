using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportJobLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportJobLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrchestratorAlias = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LastRefreshedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportJobLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportJobLinks_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportJobLinks_JobName",
                table: "ReportJobLinks",
                column: "JobName");

            migrationBuilder.CreateIndex(
                name: "IX_ReportJobLinks_ReportId_OrchestratorAlias_JobName",
                table: "ReportJobLinks",
                columns: new[] { "ReportId", "OrchestratorAlias", "JobName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportJobLinks");
        }
    }
}
