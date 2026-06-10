using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatasetGlobalUniqueName : Migration
    {
        /// <inheritdoc />
        // Dataset names become globally unique (USE DATASET resolves by name portal-wide).
        // If an existing catalog has the same dataset name in two folders, creating the unique
        // IX_Datasets_Name will fail — de-duplicate those names before applying on such a DB.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Datasets_FolderPath_Name",
                table: "Datasets");

            migrationBuilder.CreateIndex(
                name: "IX_Datasets_Name",
                table: "Datasets",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Datasets_Name",
                table: "Datasets");

            migrationBuilder.CreateIndex(
                name: "IX_Datasets_FolderPath_Name",
                table: "Datasets",
                columns: new[] { "FolderPath", "Name" },
                unique: true);
        }
    }
}
