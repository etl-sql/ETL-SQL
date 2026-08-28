using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayAmbiguousWriteCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GatewayAmbiguousWriteCases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    GatewayId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayAmbiguousWriteCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GatewayAmbiguousWriteEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CaseId = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    EvidenceReference = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayAmbiguousWriteEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatewayAmbiguousWriteEvents_GatewayAmbiguousWriteCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "GatewayAmbiguousWriteCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayAmbiguousWriteCases_TenantId_OperationId",
                table: "GatewayAmbiguousWriteCases",
                columns: new[] { "TenantId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayAmbiguousWriteEvents_CaseId",
                table: "GatewayAmbiguousWriteEvents",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayAmbiguousWriteEvents_TenantId_CaseId_Id",
                table: "GatewayAmbiguousWriteEvents",
                columns: new[] { "TenantId", "CaseId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatewayAmbiguousWriteEvents");

            migrationBuilder.DropTable(
                name: "GatewayAmbiguousWriteCases");
        }
    }
}
