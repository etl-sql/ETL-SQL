using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
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
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    pipeline_id = table.Column<int>(type: "INTEGER", nullable: false),
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    requested_version = table.Column<long>(type: "INTEGER", nullable: false),
                    script_hash = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    job_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    run_id = table.Column<long>(type: "INTEGER", nullable: true),
                    owner_instance_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
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
                name: "ix_studio_run_idempotencies_tenant_id_user_id_pipeline_id_idempotency_key",
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
