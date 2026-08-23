using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookmarkStateToSavedViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScriptHash",
                table: "SavedReportViews",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateJson",
                table: "SavedReportViews",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScriptHash",
                table: "SavedReportViews");

            migrationBuilder.DropColumn(
                name: "StateJson",
                table: "SavedReportViews");
        }
    }
}
