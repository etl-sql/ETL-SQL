using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScriptHashFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublishedScriptHash",
                table: "Reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScriptHashAtRunTime",
                table: "ReportSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HashMatched",
                table: "ReportSnapshots",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedScriptHash",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "ScriptHashAtRunTime",
                table: "ReportSnapshots");

            migrationBuilder.DropColumn(
                name: "HashMatched",
                table: "ReportSnapshots");
        }
    }
}
