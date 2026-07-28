using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ReportAlerts",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "ReportAlerts",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEvaluatedAt",
                table: "ReportAlerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastNotifiedAt",
                table: "ReportAlerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastState",
                table: "ReportAlerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionsJson",
                table: "ReportAlerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlertNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlertId = table.Column<int>(type: "INTEGER", nullable: false),
                    OrchestratorAlias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NotificationName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
