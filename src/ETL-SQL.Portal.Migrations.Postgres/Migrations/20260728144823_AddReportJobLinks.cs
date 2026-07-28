using System;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PortalDbContext))]
    [Migration("20260728144823_AddReportJobLinks")]
    public partial class AddReportJobLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportJobLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    OrchestratorAlias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    JobName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
