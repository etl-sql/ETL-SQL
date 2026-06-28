using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorId",
                table: "PortalExecutionJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorType",
                table: "PortalExecutionJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "PortalExecutionJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectiveScopes",
                table: "PortalExecutionJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorId",
                table: "AuditOutboxMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorType",
                table: "AuditOutboxMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "EffectiveScopes",
                table: "AuditOutboxMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorId",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorType",
                table: "AuditLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<string>(
                name: "EffectiveScopes",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 35, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RoleNames = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceAccounts_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_ClientId",
                table: "ServiceAccounts",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_NormalizedName",
                table: "ServiceAccounts",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAccounts_OwnerUserId",
                table: "ServiceAccounts",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceAccounts");

            migrationBuilder.DropColumn(
                name: "ActorId",
                table: "PortalExecutionJobs");

            migrationBuilder.DropColumn(
                name: "ActorType",
                table: "PortalExecutionJobs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "PortalExecutionJobs");

            migrationBuilder.DropColumn(
                name: "EffectiveScopes",
                table: "PortalExecutionJobs");

            migrationBuilder.DropColumn(
                name: "ActorId",
                table: "AuditOutboxMessages");

            migrationBuilder.DropColumn(
                name: "ActorType",
                table: "AuditOutboxMessages");

            migrationBuilder.DropColumn(
                name: "EffectiveScopes",
                table: "AuditOutboxMessages");

            migrationBuilder.DropColumn(
                name: "ActorId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ActorType",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "EffectiveScopes",
                table: "AuditLogs");
        }
    }
}
