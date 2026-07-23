using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(ETL_SQL.Portal.Data.PortalDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260723000900_AddReportAcls")]
    public partial class AddReportAcls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportAcls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    GroupId = table.Column<int>(type: "integer", nullable: true),
                    Permission = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAcls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportAcls_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportAcls_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportAcls_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportAcls_GroupId",
                table: "ReportAcls",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAcls_UserId",
                table: "ReportAcls",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAcls_ReportId_UserId",
                table: "ReportAcls",
                columns: new[] { "ReportId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAcls_ReportId_GroupId",
                table: "ReportAcls",
                columns: new[] { "ReportId", "GroupId" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportAcls");
        }
    }
}
