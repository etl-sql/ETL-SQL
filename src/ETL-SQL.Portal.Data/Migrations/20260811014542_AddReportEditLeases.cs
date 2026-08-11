using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportEditLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditSessionExpiresAtUtc",
                table: "ReportScriptDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditSessionUserId",
                table: "ReportScriptDrafts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditSessionUserName",
                table: "ReportScriptDrafts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditSessionExpiresAtUtc",
                table: "Reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditSessionUserId",
                table: "Reports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditSessionUserName",
                table: "Reports",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditSessionExpiresAtUtc",
                table: "ReportScriptDrafts");

            migrationBuilder.DropColumn(
                name: "EditSessionUserId",
                table: "ReportScriptDrafts");

            migrationBuilder.DropColumn(
                name: "EditSessionUserName",
                table: "ReportScriptDrafts");

            migrationBuilder.DropColumn(
                name: "EditSessionExpiresAtUtc",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "EditSessionUserId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "EditSessionUserName",
                table: "Reports");
        }
    }
}
