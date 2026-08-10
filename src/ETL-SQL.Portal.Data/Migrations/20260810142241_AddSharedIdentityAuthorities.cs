using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedIdentityAuthorities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedIdentityAuthorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuthorityId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PortalHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    LoginDomain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ClientSecretReference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedIdentityAuthorities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedIdentityAuthorities_AuthorityId",
                table: "SharedIdentityAuthorities",
                column: "AuthorityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedIdentityAuthorities_LoginDomain",
                table: "SharedIdentityAuthorities",
                column: "LoginDomain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedIdentityAuthorities_PortalHost",
                table: "SharedIdentityAuthorities",
                column: "PortalHost",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedIdentityAuthorities_TenantId_Issuer",
                table: "SharedIdentityAuthorities",
                columns: new[] { "TenantId", "Issuer" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedIdentityAuthorities");
        }
    }
}
