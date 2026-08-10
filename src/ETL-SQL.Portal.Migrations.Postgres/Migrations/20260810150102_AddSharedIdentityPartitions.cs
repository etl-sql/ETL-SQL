using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedIdentityPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceAccounts_NormalizedName",
                table: "ServiceAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Groups_Name",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_Provider_ExternalSubject",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "UserGroups",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ServiceAccounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "RefreshTokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Groups",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "ExternalIssuer",
                table: "AspNetUsers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_TenantId_UserId_GroupId",
                table: "UserGroups",
                columns: new[] { "TenantId", "UserId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_TenantId_NormalizedName",
                table: "ServiceAccounts",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TenantId_UserId",
                table: "RefreshTokens",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_TenantId_Name",
                table: "Groups",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId_NormalizedUserName",
                table: "AspNetUsers",
                columns: new[] { "TenantId", "NormalizedUserName" },
                unique: true,
                filter: "\"NormalizedUserName\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId_Provider_ExternalIssuer_ExternalSubject",
                table: "AspNetUsers",
                columns: new[] { "TenantId", "Provider", "ExternalIssuer", "ExternalSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserGroups_TenantId_UserId_GroupId",
                table: "UserGroups");

            migrationBuilder.DropIndex(
                name: "IX_ServiceAccounts_TenantId_NormalizedName",
                table: "ServiceAccounts");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_TenantId_UserId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Groups_TenantId_Name",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId_NormalizedUserName",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId_Provider_ExternalIssuer_ExternalSubject",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserGroups");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ServiceAccounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ExternalIssuer",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_NormalizedName",
                table: "ServiceAccounts",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                table: "Groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Provider_ExternalSubject",
                table: "AspNetUsers",
                columns: new[] { "Provider", "ExternalSubject" });

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);
        }
    }
}
