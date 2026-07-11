using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.ReportPortal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedConnectionAcls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedConnectionAcls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SharedConnectionId = table.Column<int>(type: "integer", nullable: false),
                    GroupId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedConnectionAcls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedConnectionAcls_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SharedConnectionAcls_PortalSharedConnections_SharedConnecti~",
                        column: x => x.SharedConnectionId,
                        principalTable: "PortalSharedConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionAcls_GroupId",
                table: "SharedConnectionAcls",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedConnectionAcls_SharedConnectionId_GroupId",
                table: "SharedConnectionAcls",
                columns: new[] { "SharedConnectionId", "GroupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedConnectionAcls");
        }
    }
}
