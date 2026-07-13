using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.ReportPortal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyCanaryRollout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanaryGroup",
                table: "PolicyVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CanaryPercentage",
                table: "PolicyVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanaryGroup",
                table: "PolicyMachines",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanaryGroup",
                table: "PolicyVersions");

            migrationBuilder.DropColumn(
                name: "CanaryPercentage",
                table: "PolicyVersions");

            migrationBuilder.DropColumn(
                name: "CanaryGroup",
                table: "PolicyMachines");
        }
    }
}
