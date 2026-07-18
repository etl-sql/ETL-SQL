using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetAddFolderId : Migration
    {
        /// <inheritdoc />
        // Links a dataset to its owning report's folder for PUBLIC folder-permission checks.
        // No data backfill: OwningReportId was never populated historically, so existing rows keep
        // FolderId = null and fall back to the "any authenticated caller" PUBLIC rule until recreated.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FolderId",
                table: "Datasets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Datasets_FolderId",
                table: "Datasets",
                column: "FolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Datasets_Folders_FolderId",
                table: "Datasets",
                column: "FolderId",
                principalTable: "Folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Datasets_Folders_FolderId",
                table: "Datasets");

            migrationBuilder.DropIndex(
                name: "IX_Datasets_FolderId",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Datasets");
        }
    }
}
