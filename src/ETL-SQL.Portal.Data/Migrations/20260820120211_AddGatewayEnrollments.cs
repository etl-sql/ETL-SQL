using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayEnrollments",
                columns: table => new
                {
                    EnrollmentId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GatewayId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ConsumedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WorkloadPublicKeyThumbprint = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayEnrollments", x => x.EnrollmentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayEnrollments_TenantId_GatewayId",
                table: "GatewayEnrollments",
                columns: new[] { "TenantId", "GatewayId" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayEnrollments_TenantId_TokenHash",
                table: "GatewayEnrollments",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatewayEnrollments");
        }
    }
}
