using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ETLSQL.ReportPortal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Tenant = table.Column<string>(type: "text", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    PolicyVersion = table.Column<string>(type: "text", nullable: false),
                    PolicyHash = table.Column<string>(type: "text", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: false),
                    Reviewer = table.Column<string>(type: "text", nullable: true),
                    SupersededVersion = table.Column<string>(type: "text", nullable: true),
                    RolloutState = table.Column<string>(type: "text", nullable: false),
                    SignedEnvelopeJson = table.Column<string>(type: "text", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyVersions_Tenant_Environment_PolicyVersion",
                table: "PolicyVersions",
                columns: new[] { "Tenant", "Environment", "PolicyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyVersions_Tenant_Environment_RolloutState",
                table: "PolicyVersions",
                columns: new[] { "Tenant", "Environment", "RolloutState" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyVersions");
        }
    }
}
