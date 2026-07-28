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
    [Migration("20260728123800_AddAlertNotifications")]
    public partial class AddAlertNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ReportAlerts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "ReportAlerts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEvaluatedAt",
                table: "ReportAlerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastNotifiedAt",
                table: "ReportAlerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastState",
                table: "ReportAlerts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionsJson",
                table: "ReportAlerts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlertNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AlertId = table.Column<int>(type: "integer", nullable: false),
                    OrchestratorAlias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NotificationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertNotifications_ReportAlerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "ReportAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportAlerts_Name",
                table: "ReportAlerts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotifications_AlertId_OrchestratorAlias_NotificationName",
                table: "AlertNotifications",
                columns: new[] { "AlertId", "OrchestratorAlias", "NotificationName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertNotifications");

            migrationBuilder.DropIndex(
                name: "IX_ReportAlerts_Name",
                table: "ReportAlerts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ReportAlerts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "ReportAlerts");

            migrationBuilder.DropColumn(
                name: "LastEvaluatedAt",
                table: "ReportAlerts");

            migrationBuilder.DropColumn(
                name: "LastNotifiedAt",
                table: "ReportAlerts");

            migrationBuilder.DropColumn(
                name: "LastState",
                table: "ReportAlerts");

            migrationBuilder.DropColumn(
                name: "OptionsJson",
                table: "ReportAlerts");
        }
    }
}
