using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedTenantResourceRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedTenantResources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LogicalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ScopedId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedTenantResources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedTenantResources_TenantId_Kind_Id",
                table: "SharedTenantResources",
                columns: new[] { "TenantId", "Kind", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedTenantResources_TenantId_Kind_LogicalId",
                table: "SharedTenantResources",
                columns: new[] { "TenantId", "Kind", "LogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedTenantResources_TenantId_ScopedId",
                table: "SharedTenantResources",
                columns: new[] { "TenantId", "ScopedId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedTenantResources");
        }
    }
}
