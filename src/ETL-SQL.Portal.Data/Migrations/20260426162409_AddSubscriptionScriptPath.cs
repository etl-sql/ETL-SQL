using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionScriptPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScriptPath",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScriptPath",
                table: "Subscriptions");
        }
    }
}
