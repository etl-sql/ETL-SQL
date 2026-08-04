using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    ScriptText = table.Column<string>(type: "text", nullable: false),
                    ScriptHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BaseScriptHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AuthorUserId = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ApprovedByUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DraftId = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ScriptHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: false),
                    DecidedByUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
