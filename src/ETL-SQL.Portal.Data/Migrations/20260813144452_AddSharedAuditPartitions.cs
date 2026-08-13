using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedAuditPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditOutboxMessages_EventId",
                table: "AuditOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AuditOutboxMessages_Status_NextAttemptAt",
                table: "AuditOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Action_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ResourceType_ResourceId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuditOutboxMessages",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.CreateIndex(
                name: "IX_AuditOutboxMessages_TenantId_AuditLogId",
                table: "AuditOutboxMessages",
                columns: new[] { "TenantId", "AuditLogId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditOutboxMessages_TenantId_EventId",
                table: "AuditOutboxMessages",
                columns: new[] { "TenantId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditOutboxMessages_TenantId_Status_NextAttemptAt",
                table: "AuditOutboxMessages",
                columns: new[] { "TenantId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_Action_Timestamp",
                table: "AuditLogs",
                columns: new[] { "TenantId", "Action", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ResourceType_ResourceId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "TenantId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditOutboxMessages_TenantId_AuditLogId",
                table: "AuditOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AuditOutboxMessages_TenantId_EventId",
                table: "AuditOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AuditOutboxMessages_TenantId_Status_NextAttemptAt",
                table: "AuditOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_Action_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_ResourceType_ResourceId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditOutboxMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AuditOutboxMessages_EventId",
                table: "AuditOutboxMessages",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditOutboxMessages_Status_NextAttemptAt",
                table: "AuditOutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_Timestamp",
                table: "AuditLogs",
                columns: new[] { "Action", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ResourceType_ResourceId",
                table: "AuditLogs",
                columns: new[] { "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");
        }
    }
}
