using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportScriptDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportScriptDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScriptText = table.Column<string>(type: "TEXT", nullable: false),
                    ScriptHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BaseScriptHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AuthorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ApprovedByUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportScriptDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportScriptDrafts_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportScriptDraftDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DraftId = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    ScriptHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DecidedByUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportScriptDraftDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportScriptDraftDecisions_ReportScriptDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "ReportScriptDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportScriptDraftDecisions_DraftId_DecidedAtUtc",
                table: "ReportScriptDraftDecisions",
                columns: new[] { "DraftId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportScriptDrafts_ReportId_Status",
                table: "ReportScriptDrafts",
                columns: new[] { "ReportId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportScriptDraftDecisions");

            migrationBuilder.DropTable(
                name: "ReportScriptDrafts");
        }
    }
}
