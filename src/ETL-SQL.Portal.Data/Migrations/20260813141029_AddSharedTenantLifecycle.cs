using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedTenantLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedTenantLifecycleOperations",
                columns: table => new
                {
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PlatformOperator = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AuthorizationReference = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AuthorizationExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TargetRelease = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TargetMaxConcurrentJobs = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetMaxStorageMb = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetMaxReportSessions = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedTenantLifecycleOperations", x => x.OperationId);
                });

            migrationBuilder.CreateTable(
                name: "SharedTenantLifecycles",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActiveRelease = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    MaxConcurrentJobs = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxStorageMb = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxReportSessions = table.Column<int>(type: "INTEGER", nullable: false),
                    FenceEpoch = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedTenantLifecycles", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedTenantLifecycleOperations_Kind_AuthorizationReference",
                table: "SharedTenantLifecycleOperations",
                columns: new[] { "Kind", "AuthorizationReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedTenantLifecycleOperations_TenantId_Status",
                table: "SharedTenantLifecycleOperations",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedTenantLifecycles_State",
                table: "SharedTenantLifecycles",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedTenantLifecycleOperations");

            migrationBuilder.DropTable(
                name: "SharedTenantLifecycles");
        }
    }
}
