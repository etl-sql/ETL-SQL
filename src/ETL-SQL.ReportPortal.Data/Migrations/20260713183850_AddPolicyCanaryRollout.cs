using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.ReportPortal.Data.Migrations
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
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CanaryPercentage",
                table: "PolicyVersions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanaryGroup",
                table: "PolicyMachines",
                type: "TEXT",
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
