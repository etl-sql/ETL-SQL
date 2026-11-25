using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioRunIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "studio_run_idempotencies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    pipeline_id = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    requested_version = table.Column<long>(type: "bigint", nullable: false),
                    script_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    job_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    run_id = table.Column<long>(type: "bigint", nullable: true),
                    owner_instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_studio_run_idempotencies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_studio_run_idempotencies_expires_at",
                table: "studio_run_idempotencies",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_studio_run_idempotencies_tenant_id_user_id_pipeline_id_idem~",
                table: "studio_run_idempotencies",
                columns: new[] { "tenant_id", "user_id", "pipeline_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "studio_run_idempotencies");
        }
    }
}
