using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <summary>
    /// Adds per-user dataset grants so dataset authorship no longer has to act as standing
    /// permission, and backfills one Owner grant for every dataset that already has a creator, so
    /// removing the <c>CreatedBy</c> short-circuit does not silently revoke access to datasets that
    /// already exist.
    /// </summary>
    public partial class AddDatasetUserAcls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatasetUserAcls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DatasetId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Permission = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetUserAcls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetUserAcls_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatasetUserAcls_Datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetUserAcls_DatasetId_UserId",
                table: "DatasetUserAcls",
                columns: new[] { "DatasetId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DatasetUserAcls_UserId",
                table: "DatasetUserAcls",
                column: "UserId");

            // Backfill: permission 3 is DatasetPermission.Owner. Both authorship paths the old
            // short-circuit honoured are preserved — the dataset's own creator, and the creator of
            // the report that owns it — so nobody loses access the day this deploys.
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO "DatasetUserAcls" ("DatasetId", "UserId", "Permission", "CreatedAt")
                SELECT DISTINCT d."Id", u."Id", 3, strftime('%Y-%m-%d %H:%M:%S', 'now')
                FROM "Datasets" d
                JOIN "AspNetUsers" u ON u."Id" = d."CreatedBy";
                """);

            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO "DatasetUserAcls" ("DatasetId", "UserId", "Permission", "CreatedAt")
                SELECT DISTINCT d."Id", u."Id", 3, strftime('%Y-%m-%d %H:%M:%S', 'now')
                FROM "Datasets" d
                JOIN "Reports" r ON r."Id" = d."OwningReportId"
                JOIN "AspNetUsers" u ON u."Id" = r."CreatedBy";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetUserAcls");
        }
    }
}
