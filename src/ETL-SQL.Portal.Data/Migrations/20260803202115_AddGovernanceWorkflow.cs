using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernanceWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GovernanceAssetBadges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Badge = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AssetVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceAssetBadges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceAssetReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ReviewedVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceAssetReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AssetKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    RuleKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AssetVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SuppressedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceGlossaryTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Term = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Formula = table.Column<string>(type: "TEXT", nullable: true),
                    Steward = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Disabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceGlossaryTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceResolutionCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExpiryDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Disabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceResolutionCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceScans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AssetsScanned = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingsOpened = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingsResolved = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingsReopened = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    StartedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceScans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetScore = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableMetadataCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableProtectedDataCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableGlossaryCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableStalenessCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeductMetadata = table.Column<int>(type: "INTEGER", nullable: false),
                    DeductProtectedData = table.Column<int>(type: "INTEGER", nullable: false),
                    DeductGlossary = table.Column<int>(type: "INTEGER", nullable: false),
                    DeductStaleness = table.Column<int>(type: "INTEGER", nullable: false),
                    StaleAfterDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PolicyLevel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceFindingDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FindingId = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CategoryValue = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    AssetVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    DecidedByUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceFindingDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernanceFindingDecisions_GovernanceFindings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "GovernanceFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceAssetBadges_AssetKey_Badge",
                table: "GovernanceAssetBadges",
                columns: new[] { "AssetKey", "Badge" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceAssetReviews_AssetKey",
                table: "GovernanceAssetReviews",
                column: "AssetKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceFindingDecisions_FindingId_DecidedAtUtc",
                table: "GovernanceFindingDecisions",
                columns: new[] { "FindingId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceFindings_AssetKey_RuleKey",
                table: "GovernanceFindings",
                columns: new[] { "AssetKey", "RuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceFindings_Status",
                table: "GovernanceFindings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceGlossaryTerms_Term",
                table: "GovernanceGlossaryTerms",
                column: "Term",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceResolutionCategories_Value",
                table: "GovernanceResolutionCategories",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceScans_StartedAtUtc",
                table: "GovernanceScans",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceSettings_Scope",
                table: "GovernanceSettings",
                column: "Scope",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernanceAssetBadges");

            migrationBuilder.DropTable(
                name: "GovernanceAssetReviews");

            migrationBuilder.DropTable(
                name: "GovernanceFindingDecisions");

            migrationBuilder.DropTable(
                name: "GovernanceGlossaryTerms");

            migrationBuilder.DropTable(
                name: "GovernanceResolutionCategories");

            migrationBuilder.DropTable(
                name: "GovernanceScans");

            migrationBuilder.DropTable(
                name: "GovernanceSettings");

            migrationBuilder.DropTable(
                name: "GovernanceFindings");
        }
    }
}
