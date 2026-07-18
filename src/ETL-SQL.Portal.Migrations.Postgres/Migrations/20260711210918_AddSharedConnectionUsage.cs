using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedConnectionUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedConnectionUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SharedConnectionId = table.Column<int>(type: "integer", nullable: false),
                    ConsumerUser = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UseCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedConnectionUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedConnectionUsages_PortalSharedConnections_SharedConnec~",
                        column: x => x.SharedConnectionId,
                        principalTable: "PortalSharedConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionUsages_SharedConnectionId_ConsumerUser",
                table: "SharedConnectionUsages",
                columns: new[] { "SharedConnectionId", "ConsumerUser" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedConnectionUsages");
        }
    }
}
