using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.ReportPortal.Data.Migrations
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Tenant = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyHash = table.Column<string>(type: "TEXT", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    Reviewer = table.Column<string>(type: "TEXT", nullable: true),
                    SupersededVersion = table.Column<string>(type: "TEXT", nullable: true),
                    RolloutState = table.Column<string>(type: "TEXT", nullable: false),
                    SignedEnvelopeJson = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
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
