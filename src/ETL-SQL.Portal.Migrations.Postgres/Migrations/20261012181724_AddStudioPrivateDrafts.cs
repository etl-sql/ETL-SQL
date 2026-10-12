using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioPrivateDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "studio_private_drafts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_user_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    unsaved_content = table.Column<string>(type: "text", nullable: true),
                    unsaved_base_version = table.Column<long>(type: "bigint", nullable: true),
                    unsaved_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_studio_private_drafts", x => x.id);
                    table.ForeignKey(
                        name: "fk_studio_private_drafts_asp_net_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "studio_private_draft_revisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    draft_id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_studio_private_draft_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_studio_private_draft_revisions_studio_private_drafts_draft_~",
                        column: x => x.draft_id,
                        principalTable: "studio_private_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_studio_private_draft_revisions_draft_id_version",
                table: "studio_private_draft_revisions",
                columns: new[] { "draft_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_studio_private_drafts_owner_user_id",
                table: "studio_private_drafts",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_studio_private_drafts_tenant_id_owner_user_id",
                table: "studio_private_drafts",
                columns: new[] { "tenant_id", "owner_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "studio_private_draft_revisions");

            migrationBuilder.DropTable(
                name: "studio_private_drafts");
        }
    }
}
