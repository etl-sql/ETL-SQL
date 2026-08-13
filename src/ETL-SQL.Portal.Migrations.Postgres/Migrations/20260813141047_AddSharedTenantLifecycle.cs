using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
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
                    OperationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlatformOperator = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuthorizationReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AuthorizationExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TargetRelease = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TargetMaxConcurrentJobs = table.Column<int>(type: "integer", nullable: true),
                    TargetMaxStorageMb = table.Column<int>(type: "integer", nullable: true),
                    TargetMaxReportSessions = table.Column<int>(type: "integer", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedTenantLifecycleOperations", x => x.OperationId);
                });

            migrationBuilder.CreateTable(
                name: "SharedTenantLifecycles",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActiveRelease = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MaxConcurrentJobs = table.Column<int>(type: "integer", nullable: false),
                    MaxStorageMb = table.Column<int>(type: "integer", nullable: false),
                    MaxReportSessions = table.Column<int>(type: "integer", nullable: false),
                    FenceEpoch = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
