using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioRecoveryDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudioRecoveryDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    BaseVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    BaseSourceRevision = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioRecoveryDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudioRecoveryDrafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudioRecoveryDrafts_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudioRecoveryDrafts_ReportId",
                table: "StudioRecoveryDrafts",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_StudioRecoveryDrafts_UserId_ReportId",
                table: "StudioRecoveryDrafts",
                columns: new[] { "UserId", "ReportId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudioRecoveryDrafts");
        }
    }
}
