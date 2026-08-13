using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedTenantLifecycleIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetClientId",
                table: "SharedTenantLifecycleOperations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetClientSecretReference",
                table: "SharedTenantLifecycleOperations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetIssuer",
                table: "SharedTenantLifecycleOperations",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetLoginDomain",
                table: "SharedTenantLifecycleOperations",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetPortalHost",
                table: "SharedTenantLifecycleOperations",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetClientId",
                table: "SharedTenantLifecycleOperations");

            migrationBuilder.DropColumn(
                name: "TargetClientSecretReference",
                table: "SharedTenantLifecycleOperations");

            migrationBuilder.DropColumn(
                name: "TargetIssuer",
                table: "SharedTenantLifecycleOperations");

            migrationBuilder.DropColumn(
                name: "TargetLoginDomain",
                table: "SharedTenantLifecycleOperations");

            migrationBuilder.DropColumn(
                name: "TargetPortalHost",
                table: "SharedTenantLifecycleOperations");
        }
    }
}
