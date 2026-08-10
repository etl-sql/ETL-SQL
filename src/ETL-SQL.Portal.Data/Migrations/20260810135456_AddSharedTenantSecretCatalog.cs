using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedTenantSecretCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SharedConnectionUsages_SharedConnectionId_ConsumerUser",
                table: "SharedConnectionUsages");

            migrationBuilder.DropIndex(
                name: "IX_SharedConnectionAcls_SharedConnectionId_GroupId",
                table: "SharedConnectionAcls");

            migrationBuilder.DropIndex(
                name: "IX_PortalSharedConnections_Alias",
                table: "PortalSharedConnections");

            migrationBuilder.DropIndex(
                name: "IX_PortalSecrets_Name",
                table: "PortalSecrets");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SharedConnectionUsages",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SharedConnectionAcls",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PortalSharedConnections",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PortalSecrets",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionUsages_SharedConnectionId",
                table: "SharedConnectionUsages",
                column: "SharedConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionUsages_TenantId_SharedConnectionId_ConsumerUser",
                table: "SharedConnectionUsages",
                columns: new[] { "TenantId", "SharedConnectionId", "ConsumerUser" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionAcls_SharedConnectionId",
                table: "SharedConnectionAcls",
                column: "SharedConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionAcls_TenantId_SharedConnectionId_GroupId",
                table: "SharedConnectionAcls",
                columns: new[] { "TenantId", "SharedConnectionId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalSharedConnections_TenantId_Alias",
                table: "PortalSharedConnections",
                columns: new[] { "TenantId", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalSecrets_TenantId_Name",
                table: "PortalSecrets",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SharedConnectionUsages_SharedConnectionId",
                table: "SharedConnectionUsages");

            migrationBuilder.DropIndex(
                name: "IX_SharedConnectionUsages_TenantId_SharedConnectionId_ConsumerUser",
                table: "SharedConnectionUsages");

            migrationBuilder.DropIndex(
                name: "IX_SharedConnectionAcls_SharedConnectionId",
                table: "SharedConnectionAcls");

            migrationBuilder.DropIndex(
                name: "IX_SharedConnectionAcls_TenantId_SharedConnectionId_GroupId",
                table: "SharedConnectionAcls");

            migrationBuilder.DropIndex(
                name: "IX_PortalSharedConnections_TenantId_Alias",
                table: "PortalSharedConnections");

            migrationBuilder.DropIndex(
                name: "IX_PortalSecrets_TenantId_Name",
                table: "PortalSecrets");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SharedConnectionUsages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SharedConnectionAcls");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PortalSharedConnections");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PortalSecrets");

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionUsages_SharedConnectionId_ConsumerUser",
                table: "SharedConnectionUsages",
                columns: new[] { "SharedConnectionId", "ConsumerUser" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionAcls_SharedConnectionId_GroupId",
                table: "SharedConnectionAcls",
                columns: new[] { "SharedConnectionId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalSharedConnections_Alias",
                table: "PortalSharedConnections",
                column: "Alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalSecrets_Name",
                table: "PortalSecrets",
                column: "Name",
                unique: true);
        }
    }
}
