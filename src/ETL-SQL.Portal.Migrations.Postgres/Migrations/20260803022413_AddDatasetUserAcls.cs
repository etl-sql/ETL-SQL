using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <summary>
    /// Adds per-user dataset grants so dataset authorship no longer has to act as standing
    /// permission, and backfills one Owner grant for every dataset that already has a creator, so
    /// removing the <c>CreatedBy</c> short-circuit does not silently revoke access to datasets that
    /// already exist.
    ///
    /// Scaffolding this migration also re-proposed operations that earlier Postgres migrations had
    /// already applied (the AlertNotifications table and ReportAlerts columns from
    /// <c>AddAlertNotifications</c>, and the ReportEmbedTokens index swap from
    /// <c>AddShareLinkNames</c>), because the Postgres model snapshot had never been regenerated for
    /// them. Those were removed from Up — re-running them would fail against any migrated database.
    /// The regenerated snapshot shipping alongside this migration is the first accurate one since.
    /// A pending <c>Datasets.Name</c> widening (character varying(200) → text, Postgres only) was
    /// removed as well: it is an AlterColumn, which the rolling-expand contract rejects, and it needs
    /// its own migration rather than a ride-along.
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DatasetId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                INSERT INTO "DatasetUserAcls" ("DatasetId", "UserId", "Permission", "CreatedAt")
                SELECT DISTINCT d."Id", u."Id", 3, NOW() AT TIME ZONE 'UTC'
                FROM "Datasets" d
                JOIN "AspNetUsers" u ON u."Id" = d."CreatedBy"
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "DatasetUserAcls" ("DatasetId", "UserId", "Permission", "CreatedAt")
                SELECT DISTINCT d."Id", u."Id", 3, NOW() AT TIME ZONE 'UTC'
                FROM "Datasets" d
                JOIN "Reports" r ON r."Id" = d."OwningReportId"
                JOIN "AspNetUsers" u ON u."Id" = r."CreatedBy"
                ON CONFLICT DO NOTHING;
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
