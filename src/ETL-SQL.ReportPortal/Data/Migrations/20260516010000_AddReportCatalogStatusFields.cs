using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PortalDbContext))]
    [Migration("20260516010000_AddReportCatalogStatusFields")]
    public partial class AddReportCatalogStatusFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastRefreshError",
                table: "Reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshCompletedAt",
                table: "Reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastRefreshDurationMs",
                table: "Reports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshStartedAt",
                table: "Reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefreshStatus",
                table: "Reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastViewedAt",
                table: "Reports",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastRefreshError", table: "Reports");
            migrationBuilder.DropColumn(name: "LastRefreshCompletedAt", table: "Reports");
            migrationBuilder.DropColumn(name: "LastRefreshDurationMs", table: "Reports");
            migrationBuilder.DropColumn(name: "LastRefreshStartedAt", table: "Reports");
            migrationBuilder.DropColumn(name: "LastRefreshStatus", table: "Reports");
            migrationBuilder.DropColumn(name: "LastViewedAt", table: "Reports");
        }
    }
}
