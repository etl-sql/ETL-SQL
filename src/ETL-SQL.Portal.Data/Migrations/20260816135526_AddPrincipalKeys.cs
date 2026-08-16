using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
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
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrincipalKey",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            // Backfilled before the unique index exists, because every pre-existing row would
            // otherwise be NULL and the index — filtered to non-NULL — would leave them permanently
            // unkeyed and therefore unable to hold a grant. randomblob(16) gives each row its own
            // value in one statement; a constant default could not, which is why the column is added
            // nullable and filled here rather than defaulted.
            migrationBuilder.Sql(
                "UPDATE AspNetUsers SET PrincipalKey = lower(hex(randomblob(16))) WHERE PrincipalKey IS NULL;");
            migrationBuilder.Sql(
                "UPDATE Groups SET PrincipalKey = lower(hex(randomblob(16))) WHERE PrincipalKey IS NULL;");

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
