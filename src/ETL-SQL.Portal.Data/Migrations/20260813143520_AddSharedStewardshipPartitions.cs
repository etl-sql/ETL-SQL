using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedStewardshipPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StewardshipSettings_Scope",
                table: "StewardshipSettings");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipScans_StartedAtUtc",
                table: "StewardshipScans");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipResolutionCategories_Value",
                table: "StewardshipResolutionCategories");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipGlossaryTerms_Term",
                table: "StewardshipGlossaryTerms");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipFindings_AssetKey_RuleKey",
                table: "StewardshipFindings");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipFindings_Status",
                table: "StewardshipFindings");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipFindingDecisions_FindingId_DecidedAtUtc",
                table: "StewardshipFindingDecisions");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipAssetReviews_AssetKey",
                table: "StewardshipAssetReviews");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipAssetBadges_AssetKey_Badge",
                table: "StewardshipAssetBadges");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipSettings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipScans",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipResolutionCategories",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipGlossaryTerms",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipFindings",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipFindingDecisions",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipAssetReviews",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StewardshipAssetBadges",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "portal-host");

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipSettings_TenantId_Scope",
                table: "StewardshipSettings",
                columns: new[] { "TenantId", "Scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipScans_TenantId_StartedAtUtc",
                table: "StewardshipScans",
                columns: new[] { "TenantId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipResolutionCategories_TenantId_Value",
                table: "StewardshipResolutionCategories",
                columns: new[] { "TenantId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipGlossaryTerms_TenantId_Term",
                table: "StewardshipGlossaryTerms",
                columns: new[] { "TenantId", "Term" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipFindings_TenantId_AssetKey_RuleKey",
                table: "StewardshipFindings",
                columns: new[] { "TenantId", "AssetKey", "RuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipFindings_TenantId_Status",
                table: "StewardshipFindings",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipFindingDecisions_FindingId",
                table: "StewardshipFindingDecisions",
                column: "FindingId");

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipFindingDecisions_TenantId_FindingId_DecidedAtUtc",
                table: "StewardshipFindingDecisions",
                columns: new[] { "TenantId", "FindingId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipAssetReviews_TenantId_AssetKey",
                table: "StewardshipAssetReviews",
                columns: new[] { "TenantId", "AssetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipAssetBadges_TenantId_AssetKey_Badge",
                table: "StewardshipAssetBadges",
                columns: new[] { "TenantId", "AssetKey", "Badge" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StewardshipSettings_TenantId_Scope",
                table: "StewardshipSettings");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipScans_TenantId_StartedAtUtc",
                table: "StewardshipScans");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipResolutionCategories_TenantId_Value",
                table: "StewardshipResolutionCategories");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipGlossaryTerms_TenantId_Term",
                table: "StewardshipGlossaryTerms");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipFindings_TenantId_AssetKey_RuleKey",
                table: "StewardshipFindings");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipFindings_TenantId_Status",
                table: "StewardshipFindings");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipFindingDecisions_FindingId",
                table: "StewardshipFindingDecisions");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipFindingDecisions_TenantId_FindingId_DecidedAtUtc",
                table: "StewardshipFindingDecisions");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipAssetReviews_TenantId_AssetKey",
                table: "StewardshipAssetReviews");

            migrationBuilder.DropIndex(
                name: "IX_StewardshipAssetBadges_TenantId_AssetKey_Badge",
                table: "StewardshipAssetBadges");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipScans");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipResolutionCategories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipGlossaryTerms");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipFindings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipFindingDecisions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipAssetReviews");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StewardshipAssetBadges");

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipSettings_Scope",
                table: "StewardshipSettings",
                column: "Scope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipScans_StartedAtUtc",
                table: "StewardshipScans",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipResolutionCategories_Value",
                table: "StewardshipResolutionCategories",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipGlossaryTerms_Term",
                table: "StewardshipGlossaryTerms",
                column: "Term",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipFindings_AssetKey_RuleKey",
                table: "StewardshipFindings",
                columns: new[] { "AssetKey", "RuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipFindings_Status",
                table: "StewardshipFindings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipFindingDecisions_FindingId_DecidedAtUtc",
                table: "StewardshipFindingDecisions",
                columns: new[] { "FindingId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipAssetReviews_AssetKey",
                table: "StewardshipAssetReviews",
                column: "AssetKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StewardshipAssetBadges_AssetKey_Badge",
                table: "StewardshipAssetBadges",
                columns: new[] { "AssetKey", "Badge" },
                unique: true);
        }
    }
}
