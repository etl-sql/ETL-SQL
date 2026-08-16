using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPrincipalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrincipalKey",
                table: "Groups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrincipalKey",
                table: "AspNetUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // See the SQLite migration for why this is backfilled before the unique index rather than
            // defaulted: a constant default cannot be unique. md5(random()||clock_timestamp()) rather
            // than gen_random_uuid() so the statement does not depend on pgcrypto being installed, and
            // it produces the same 32-character lower-case hex shape the application mints.
            migrationBuilder.Sql(
                "UPDATE \"AspNetUsers\" SET \"PrincipalKey\" = md5(random()::text || clock_timestamp()::text) WHERE \"PrincipalKey\" IS NULL;");
            migrationBuilder.Sql(
                "UPDATE \"Groups\" SET \"PrincipalKey\" = md5(random()::text || clock_timestamp()::text) WHERE \"PrincipalKey\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_PrincipalKey",
                table: "Groups",
                column: "PrincipalKey",
                unique: true,
                filter: "\"PrincipalKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PrincipalKey",
                table: "AspNetUsers",
                column: "PrincipalKey",
                unique: true,
                filter: "\"PrincipalKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Groups_PrincipalKey",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_PrincipalKey",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PrincipalKey",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "PrincipalKey",
                table: "AspNetUsers");
        }
    }
}
