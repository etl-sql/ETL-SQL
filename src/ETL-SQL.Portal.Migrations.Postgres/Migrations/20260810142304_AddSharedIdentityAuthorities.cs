using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuthorityId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PortalHost = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    LoginDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ClientSecretReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
