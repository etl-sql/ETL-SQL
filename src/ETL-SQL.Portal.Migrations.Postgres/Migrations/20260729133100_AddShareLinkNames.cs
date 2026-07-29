using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PortalDbContext))]
    [Migration("20260729133100_AddShareLinkNames")]
    public partial class AddShareLinkNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReportShareLinks_ReportId",
                table: "ReportShareLinks");

            migrationBuilder.DropIndex(
                name: "IX_ReportEmbedTokens_ReportId",
                table: "ReportEmbedTokens");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ReportShareLinks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE \"ReportShareLinks\" SET \"Name\" = 'Share link ' || \"Id\" WHERE \"Name\" = '';");

            migrationBuilder.Sql(
                "UPDATE \"ReportEmbedTokens\" SET \"Name\" = \"Name\" || ' ' || \"Id\";");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ReportEmbedTokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_ReportShareLinks_ReportId_Name",
                table: "ReportShareLinks",
                columns: new[] { "ReportId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportEmbedTokens_ReportId_Name",
                table: "ReportEmbedTokens",
                columns: new[] { "ReportId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReportShareLinks_ReportId_Name",
                table: "ReportShareLinks");

            migrationBuilder.DropIndex(
                name: "IX_ReportEmbedTokens_ReportId_Name",
                table: "ReportEmbedTokens");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ReportShareLinks");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ReportEmbedTokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_ReportShareLinks_ReportId",
                table: "ReportShareLinks",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportEmbedTokens_ReportId",
                table: "ReportEmbedTokens",
                column: "ReportId");
        }
    }
}
