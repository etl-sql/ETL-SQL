using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkloadIdentityReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkloadIdentityReplays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BindingId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadIdentityReplays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadIdentityReplays_ExpiresAt",
                table: "WorkloadIdentityReplays",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadIdentityReplays_TenantId_BindingId_TokenIdHash",
                table: "WorkloadIdentityReplays",
                columns: new[] { "TenantId", "BindingId", "TokenIdHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkloadIdentityReplays");
        }
    }
}
