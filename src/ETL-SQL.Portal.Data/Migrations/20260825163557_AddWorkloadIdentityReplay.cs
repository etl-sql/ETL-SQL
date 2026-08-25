using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BindingId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TokenIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
