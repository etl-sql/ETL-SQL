using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.ReportPortal.Data.Migrations
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SharedConnectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsumerUser = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UseCount = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedConnectionUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedConnectionUsages_PortalSharedConnections_SharedConnectionId",
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
