using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
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
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE \"ReportShareLinks\" SET \"Name\" = 'Share link ' || \"Id\" WHERE \"Name\" = '';");

            migrationBuilder.Sql(
                "UPDATE \"ReportEmbedTokens\" SET \"Name\" = \"Name\" || ' ' || \"Id\";");

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
