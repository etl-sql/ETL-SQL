using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeSnakeCaseNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AlertNotifications_ReportAlerts_AlertId",
                table: "AlertNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                table: "AspNetRoleClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                table: "AspNetUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                table: "AspNetUserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                table: "AspNetUserTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditOutboxMessages_AuditLogs_AuditLogId",
                table: "AuditOutboxMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_DatasetAcls_Datasets_DatasetId",
                table: "DatasetAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_DatasetAcls_Groups_GroupId",
                table: "DatasetAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_Datasets_Folders_FolderId",
                table: "Datasets");

            migrationBuilder.DropForeignKey(
                name: "FK_Datasets_Reports_OwningReportId",
                table: "Datasets");

            migrationBuilder.DropForeignKey(
                name: "FK_DatasetUserAcls_AspNetUsers_UserId",
                table: "DatasetUserAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_DatasetUserAcls_Datasets_DatasetId",
                table: "DatasetUserAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_FolderAcls_Folders_FolderId",
                table: "FolderAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_FolderAcls_Groups_GroupId",
                table: "FolderAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_Folders_Folders_ParentId",
                table: "Folders");

            migrationBuilder.DropForeignKey(
                name: "FK_GatewayAmbiguousWriteEvents_GatewayAmbiguousWriteCases_CaseId",
                table: "GatewayAmbiguousWriteEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_GroupStudioCapabilities_Groups_GroupId",
                table: "GroupStudioCapabilities");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_UserId",
                table: "RefreshTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAccessRequests_AspNetUsers_DecidedByUserId",
                table: "ReportAccessRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAccessRequests_AspNetUsers_RequesterUserId",
                table: "ReportAccessRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAccessRequests_Reports_ReportId",
                table: "ReportAccessRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAcls_AspNetUsers_UserId",
                table: "ReportAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAcls_Groups_GroupId",
                table: "ReportAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAcls_Reports_ReportId",
                table: "ReportAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAlerts_AspNetUsers_OwnerId",
                table: "ReportAlerts");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportAlerts_Reports_ReportId",
                table: "ReportAlerts");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportEmbedTokens_AspNetUsers_CreatedBy",
                table: "ReportEmbedTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportEmbedTokens_Reports_ReportId",
                table: "ReportEmbedTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportFavorites_AspNetUsers_UserId",
                table: "ReportFavorites");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportFavorites_Reports_ReportId",
                table: "ReportFavorites");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportJobLinks_Reports_ReportId",
                table: "ReportJobLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Folders_FolderId",
                table: "Reports");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportScriptDraftDecisions_ReportScriptDrafts_DraftId",
                table: "ReportScriptDraftDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportScriptDrafts_Reports_ReportId",
                table: "ReportScriptDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportShareLinks_AspNetUsers_CreatedBy",
                table: "ReportShareLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportShareLinks_Reports_ReportId",
                table: "ReportShareLinks");

            migrationBuilder.DropForeignKey(
                name: "FK_ReportSnapshots_Reports_ReportId",
                table: "ReportSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedReportViews_AspNetUsers_UserId",
                table: "SavedReportViews");

            migrationBuilder.DropForeignKey(
                name: "FK_SavedReportViews_Reports_ReportId",
                table: "SavedReportViews");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceAccounts_AspNetUsers_OwnerUserId",
                table: "ServiceAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_SharedConnectionAcls_Groups_GroupId",
                table: "SharedConnectionAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_SharedConnectionAcls_PortalSharedConnections_SharedConnectionId",
                table: "SharedConnectionAcls");

            migrationBuilder.DropForeignKey(
                name: "FK_SharedConnectionUsages_PortalSharedConnections_SharedConnectionId",
                table: "SharedConnectionUsages");

            migrationBuilder.DropForeignKey(
                name: "FK_StewardshipFindingDecisions_StewardshipFindings_FindingId",
                table: "StewardshipFindingDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_StudioRecoveryDrafts_AspNetUsers_UserId",
                table: "StudioRecoveryDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_StudioRecoveryDrafts_Reports_ReportId",
                table: "StudioRecoveryDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_AspNetUsers_UserId",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Reports_ReportId",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserGroups_AspNetUsers_UserId",
                table: "UserGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_UserGroups_Groups_GroupId",
                table: "UserGroups");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Subscriptions",
                table: "Subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Reports",
                table: "Reports");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Groups",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_PrincipalKey",
                table: "Groups");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Folders",
                table: "Folders");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Datasets",
                table: "Datasets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkloadIdentityReplays",
                table: "WorkloadIdentityReplays");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserGroups",
                table: "UserGroups");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionDeliveries",
                table: "SubscriptionDeliveries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StudioRecoveryDrafts",
                table: "StudioRecoveryDrafts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipSettings",
                table: "StewardshipSettings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipScans",
                table: "StewardshipScans");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipResolutionCategories",
                table: "StewardshipResolutionCategories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipGlossaryTerms",
                table: "StewardshipGlossaryTerms");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipFindings",
                table: "StewardshipFindings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipFindingDecisions",
                table: "StewardshipFindingDecisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipAssetReviews",
                table: "StewardshipAssetReviews");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StewardshipAssetBadges",
                table: "StewardshipAssetBadges");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SharedTenantResources",
                table: "SharedTenantResources");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SharedTenantLifecycles",
                table: "SharedTenantLifecycles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SharedTenantLifecycleOperations",
                table: "SharedTenantLifecycleOperations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SharedIdentityAuthorities",
                table: "SharedIdentityAuthorities");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SharedConnectionUsages",
                table: "SharedConnectionUsages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SharedConnectionAcls",
                table: "SharedConnectionAcls");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ServiceAccounts",
                table: "ServiceAccounts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SavedReportViews",
                table: "SavedReportViews");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportSnapshots",
                table: "ReportSnapshots");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportShareLinks",
                table: "ReportShareLinks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportScriptDrafts",
                table: "ReportScriptDrafts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportScriptDraftDecisions",
                table: "ReportScriptDraftDecisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportJobLinks",
                table: "ReportJobLinks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportFavorites",
                table: "ReportFavorites");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportEmbedTokens",
                table: "ReportEmbedTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportAlerts",
                table: "ReportAlerts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportAcls",
                table: "ReportAcls");

            migrationBuilder.DropIndex(
                name: "IX_ReportAcls_ReportId_GroupId",
                table: "ReportAcls");

            migrationBuilder.DropIndex(
                name: "IX_ReportAcls_ReportId_UserId",
                table: "ReportAcls");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportAccessRequests",
                table: "ReportAccessRequests");

            migrationBuilder.DropIndex(
                name: "IX_ReportAccessRequests_RequesterUserId_ReportId",
                table: "ReportAccessRequests");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RefreshTokens",
                table: "RefreshTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PortalSharedConnections",
                table: "PortalSharedConnections");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PortalSecrets",
                table: "PortalSecrets");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PortalExecutionJobs",
                table: "PortalExecutionJobs");

            migrationBuilder.DropIndex(
                name: "IX_PortalExecutionJobs_TenantId_ReportId_Kind",
                table: "PortalExecutionJobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PolicyVersions",
                table: "PolicyVersions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PolicyMachines",
                table: "PolicyMachines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GroupStudioCapabilities",
                table: "GroupStudioCapabilities");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GatewayEnrollments",
                table: "GatewayEnrollments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GatewayAmbiguousWriteEvents",
                table: "GatewayAmbiguousWriteEvents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GatewayAmbiguousWriteCases",
                table: "GatewayAmbiguousWriteCases");

            migrationBuilder.DropPrimaryKey(
                name: "PK_FolderAcls",
                table: "FolderAcls");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DatasetUserAcls",
                table: "DatasetUserAcls");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DatasetAcls",
                table: "DatasetAcls");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AuditOutboxMessages",
                table: "AuditOutboxMessages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserTokens",
                table: "AspNetUserTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUsers",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_PrincipalKey",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId_NormalizedUserName",
                table: "AspNetUsers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserRoles",
                table: "AspNetUserRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserLogins",
                table: "AspNetUserLogins");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUserClaims",
                table: "AspNetUserClaims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetRoles",
                table: "AspNetRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetRoleClaims",
                table: "AspNetRoleClaims");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AlertNotifications",
                table: "AlertNotifications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AdminServiceRuns",
                table: "AdminServiceRuns");

            migrationBuilder.RenameTable(
                name: "Subscriptions",
                newName: "_tmp_subscriptions");

            migrationBuilder.RenameTable(
                name: "_tmp_subscriptions",
                newName: "subscriptions");

            migrationBuilder.RenameTable(
                name: "Reports",
                newName: "_tmp_reports");

            migrationBuilder.RenameTable(
                name: "_tmp_reports",
                newName: "reports");

            migrationBuilder.RenameTable(
                name: "Groups",
                newName: "_tmp_groups");

            migrationBuilder.RenameTable(
                name: "_tmp_groups",
                newName: "groups");

            migrationBuilder.RenameTable(
                name: "Folders",
                newName: "_tmp_folders");

            migrationBuilder.RenameTable(
                name: "_tmp_folders",
                newName: "folders");

            migrationBuilder.RenameTable(
                name: "Datasets",
                newName: "_tmp_datasets");

            migrationBuilder.RenameTable(
                name: "_tmp_datasets",
                newName: "datasets");

            migrationBuilder.RenameTable(
                name: "WorkloadIdentityReplays",
                newName: "_tmp_workload_identity_replays");

            migrationBuilder.RenameTable(
                name: "_tmp_workload_identity_replays",
                newName: "workload_identity_replays");

            migrationBuilder.RenameTable(
                name: "UserGroups",
                newName: "_tmp_user_groups");

            migrationBuilder.RenameTable(
                name: "_tmp_user_groups",
                newName: "user_groups");

            migrationBuilder.RenameTable(
                name: "SubscriptionDeliveries",
                newName: "_tmp_subscription_deliveries");

            migrationBuilder.RenameTable(
                name: "_tmp_subscription_deliveries",
                newName: "subscription_deliveries");

            migrationBuilder.RenameTable(
                name: "StudioRecoveryDrafts",
                newName: "_tmp_studio_recovery_drafts");

            migrationBuilder.RenameTable(
                name: "_tmp_studio_recovery_drafts",
                newName: "studio_recovery_drafts");

            migrationBuilder.RenameTable(
                name: "StewardshipSettings",
                newName: "_tmp_stewardship_settings");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_settings",
                newName: "stewardship_settings");

            migrationBuilder.RenameTable(
                name: "StewardshipScans",
                newName: "_tmp_stewardship_scans");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_scans",
                newName: "stewardship_scans");

            migrationBuilder.RenameTable(
                name: "StewardshipResolutionCategories",
                newName: "_tmp_stewardship_resolution_categories");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_resolution_categories",
                newName: "stewardship_resolution_categories");

            migrationBuilder.RenameTable(
                name: "StewardshipGlossaryTerms",
                newName: "_tmp_stewardship_glossary_terms");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_glossary_terms",
                newName: "stewardship_glossary_terms");

            migrationBuilder.RenameTable(
                name: "StewardshipFindings",
                newName: "_tmp_stewardship_findings");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_findings",
                newName: "stewardship_findings");

            migrationBuilder.RenameTable(
                name: "StewardshipFindingDecisions",
                newName: "_tmp_stewardship_finding_decisions");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_finding_decisions",
                newName: "stewardship_finding_decisions");

            migrationBuilder.RenameTable(
                name: "StewardshipAssetReviews",
                newName: "_tmp_stewardship_asset_reviews");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_asset_reviews",
                newName: "stewardship_asset_reviews");

            migrationBuilder.RenameTable(
                name: "StewardshipAssetBadges",
                newName: "_tmp_stewardship_asset_badges");

            migrationBuilder.RenameTable(
                name: "_tmp_stewardship_asset_badges",
                newName: "stewardship_asset_badges");

            migrationBuilder.RenameTable(
                name: "SharedTenantResources",
                newName: "_tmp_shared_tenant_resources");

            migrationBuilder.RenameTable(
                name: "_tmp_shared_tenant_resources",
                newName: "shared_tenant_resources");

            migrationBuilder.RenameTable(
                name: "SharedTenantLifecycles",
                newName: "_tmp_shared_tenant_lifecycles");

            migrationBuilder.RenameTable(
                name: "_tmp_shared_tenant_lifecycles",
                newName: "shared_tenant_lifecycles");

            migrationBuilder.RenameTable(
                name: "SharedTenantLifecycleOperations",
                newName: "_tmp_shared_tenant_lifecycle_operations");

            migrationBuilder.RenameTable(
                name: "_tmp_shared_tenant_lifecycle_operations",
                newName: "shared_tenant_lifecycle_operations");

            migrationBuilder.RenameTable(
                name: "SharedIdentityAuthorities",
                newName: "_tmp_shared_identity_authorities");

            migrationBuilder.RenameTable(
                name: "_tmp_shared_identity_authorities",
                newName: "shared_identity_authorities");

            migrationBuilder.RenameTable(
                name: "SharedConnectionUsages",
                newName: "_tmp_shared_connection_usages");

            migrationBuilder.RenameTable(
                name: "_tmp_shared_connection_usages",
                newName: "shared_connection_usages");

            migrationBuilder.RenameTable(
                name: "SharedConnectionAcls",
                newName: "_tmp_shared_connection_acls");

            migrationBuilder.RenameTable(
                name: "_tmp_shared_connection_acls",
                newName: "shared_connection_acls");

            migrationBuilder.RenameTable(
                name: "ServiceAccounts",
                newName: "_tmp_service_accounts");

            migrationBuilder.RenameTable(
                name: "_tmp_service_accounts",
                newName: "service_accounts");

            migrationBuilder.RenameTable(
                name: "SavedReportViews",
                newName: "_tmp_saved_report_views");

            migrationBuilder.RenameTable(
                name: "_tmp_saved_report_views",
                newName: "saved_report_views");

            migrationBuilder.RenameTable(
                name: "ReportSnapshots",
                newName: "_tmp_report_snapshots");

            migrationBuilder.RenameTable(
                name: "_tmp_report_snapshots",
                newName: "report_snapshots");

            migrationBuilder.RenameTable(
                name: "ReportShareLinks",
                newName: "_tmp_report_share_links");

            migrationBuilder.RenameTable(
                name: "_tmp_report_share_links",
                newName: "report_share_links");

            migrationBuilder.RenameTable(
                name: "ReportScriptDrafts",
                newName: "_tmp_report_script_drafts");

            migrationBuilder.RenameTable(
                name: "_tmp_report_script_drafts",
                newName: "report_script_drafts");

            migrationBuilder.RenameTable(
                name: "ReportScriptDraftDecisions",
                newName: "_tmp_report_script_draft_decisions");

            migrationBuilder.RenameTable(
                name: "_tmp_report_script_draft_decisions",
                newName: "report_script_draft_decisions");

            migrationBuilder.RenameTable(
                name: "ReportJobLinks",
                newName: "_tmp_report_job_links");

            migrationBuilder.RenameTable(
                name: "_tmp_report_job_links",
                newName: "report_job_links");

            migrationBuilder.RenameTable(
                name: "ReportFavorites",
                newName: "_tmp_report_favorites");

            migrationBuilder.RenameTable(
                name: "_tmp_report_favorites",
                newName: "report_favorites");

            migrationBuilder.RenameTable(
                name: "ReportEmbedTokens",
                newName: "_tmp_report_embed_tokens");

            migrationBuilder.RenameTable(
                name: "_tmp_report_embed_tokens",
                newName: "report_embed_tokens");

            migrationBuilder.RenameTable(
                name: "ReportAlerts",
                newName: "_tmp_report_alerts");

            migrationBuilder.RenameTable(
                name: "_tmp_report_alerts",
                newName: "report_alerts");

            migrationBuilder.RenameTable(
                name: "ReportAcls",
                newName: "_tmp_report_acls");

            migrationBuilder.RenameTable(
                name: "_tmp_report_acls",
                newName: "report_acls");

            migrationBuilder.RenameTable(
                name: "ReportAccessRequests",
                newName: "_tmp_report_access_requests");

            migrationBuilder.RenameTable(
                name: "_tmp_report_access_requests",
                newName: "report_access_requests");

            migrationBuilder.RenameTable(
                name: "RefreshTokens",
                newName: "_tmp_refresh_tokens");

            migrationBuilder.RenameTable(
                name: "_tmp_refresh_tokens",
                newName: "refresh_tokens");

            migrationBuilder.RenameTable(
                name: "PortalSharedConnections",
                newName: "_tmp_portal_shared_connections");

            migrationBuilder.RenameTable(
                name: "_tmp_portal_shared_connections",
                newName: "portal_shared_connections");

            migrationBuilder.RenameTable(
                name: "PortalSecrets",
                newName: "_tmp_portal_secrets");

            migrationBuilder.RenameTable(
                name: "_tmp_portal_secrets",
                newName: "portal_secrets");

            migrationBuilder.RenameTable(
                name: "PortalExecutionJobs",
                newName: "_tmp_portal_execution_jobs");

            migrationBuilder.RenameTable(
                name: "_tmp_portal_execution_jobs",
                newName: "portal_execution_jobs");

            migrationBuilder.RenameTable(
                name: "PolicyVersions",
                newName: "_tmp_policy_versions");

            migrationBuilder.RenameTable(
                name: "_tmp_policy_versions",
                newName: "policy_versions");

            migrationBuilder.RenameTable(
                name: "PolicyMachines",
                newName: "_tmp_policy_machines");

            migrationBuilder.RenameTable(
                name: "_tmp_policy_machines",
                newName: "policy_machines");

            migrationBuilder.RenameTable(
                name: "GroupStudioCapabilities",
                newName: "_tmp_group_studio_capabilities");

            migrationBuilder.RenameTable(
                name: "_tmp_group_studio_capabilities",
                newName: "group_studio_capabilities");

            migrationBuilder.RenameTable(
                name: "GatewayEnrollments",
                newName: "_tmp_gateway_enrollments");

            migrationBuilder.RenameTable(
                name: "_tmp_gateway_enrollments",
                newName: "gateway_enrollments");

            migrationBuilder.RenameTable(
                name: "GatewayAmbiguousWriteEvents",
                newName: "_tmp_gateway_ambiguous_write_events");

            migrationBuilder.RenameTable(
                name: "_tmp_gateway_ambiguous_write_events",
                newName: "gateway_ambiguous_write_events");

            migrationBuilder.RenameTable(
                name: "GatewayAmbiguousWriteCases",
                newName: "_tmp_gateway_ambiguous_write_cases");

            migrationBuilder.RenameTable(
                name: "_tmp_gateway_ambiguous_write_cases",
                newName: "gateway_ambiguous_write_cases");

            migrationBuilder.RenameTable(
                name: "FolderAcls",
                newName: "_tmp_folder_acls");

            migrationBuilder.RenameTable(
                name: "_tmp_folder_acls",
                newName: "folder_acls");

            migrationBuilder.RenameTable(
                name: "DatasetUserAcls",
                newName: "_tmp_dataset_user_acls");

            migrationBuilder.RenameTable(
                name: "_tmp_dataset_user_acls",
                newName: "dataset_user_acls");

            migrationBuilder.RenameTable(
                name: "DatasetAcls",
                newName: "_tmp_dataset_acls");

            migrationBuilder.RenameTable(
                name: "_tmp_dataset_acls",
                newName: "dataset_acls");

            migrationBuilder.RenameTable(
                name: "AuditOutboxMessages",
                newName: "_tmp_audit_outbox_messages");

            migrationBuilder.RenameTable(
                name: "_tmp_audit_outbox_messages",
                newName: "audit_outbox_messages");

            migrationBuilder.RenameTable(
                name: "AuditLogs",
                newName: "_tmp_audit_logs");

            migrationBuilder.RenameTable(
                name: "_tmp_audit_logs",
                newName: "audit_logs");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                newName: "_tmp_asp_net_user_tokens");

            migrationBuilder.RenameTable(
                name: "_tmp_asp_net_user_tokens",
                newName: "asp_net_user_tokens");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "_tmp_asp_net_users");

            migrationBuilder.RenameTable(
                name: "_tmp_asp_net_users",
                newName: "asp_net_users");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                newName: "_tmp_asp_net_user_roles");

            migrationBuilder.RenameTable(
                name: "_tmp_asp_net_user_roles",
                newName: "asp_net_user_roles");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                newName: "_tmp_asp_net_user_logins");

            migrationBuilder.RenameTable(
                name: "_tmp_asp_net_user_logins",
                newName: "asp_net_user_logins");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                newName: "_tmp_asp_net_user_claims");

            migrationBuilder.RenameTable(
                name: "_tmp_asp_net_user_claims",
                newName: "asp_net_user_claims");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                newName: "_tmp_asp_net_roles");

            migrationBuilder.RenameTable(
                name: "_tmp_asp_net_roles",
                newName: "asp_net_roles");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                newName: "_tmp_asp_net_role_claims");

            migrationBuilder.RenameTable(
                name: "_tmp_asp_net_role_claims",
                newName: "asp_net_role_claims");

            migrationBuilder.RenameTable(
                name: "AlertNotifications",
                newName: "_tmp_alert_notifications");

            migrationBuilder.RenameTable(
                name: "_tmp_alert_notifications",
                newName: "alert_notifications");

            migrationBuilder.RenameTable(
                name: "AdminServiceRuns",
                newName: "_tmp_admin_service_runs");

            migrationBuilder.RenameTable(
                name: "_tmp_admin_service_runs",
                newName: "admin_service_runs");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "subscriptions",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Schedule",
                table: "subscriptions",
                newName: "schedule");

            migrationBuilder.RenameColumn(
                name: "Recipients",
                table: "subscriptions",
                newName: "recipients");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "subscriptions",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Format",
                table: "subscriptions",
                newName: "format");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "subscriptions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "subscriptions",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "SmtpAlias",
                table: "subscriptions",
                newName: "smtp_alias");

            migrationBuilder.RenameColumn(
                name: "ScriptPath",
                table: "subscriptions",
                newName: "script_path");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "subscriptions",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "ParametersJson",
                table: "subscriptions",
                newName: "parameters_json");

            migrationBuilder.RenameColumn(
                name: "NextRunAt",
                table: "subscriptions",
                newName: "next_run_at");

            migrationBuilder.RenameColumn(
                name: "LastTriggeredAt",
                table: "subscriptions",
                newName: "last_triggered_at");

            migrationBuilder.RenameColumn(
                name: "LastSentAt",
                table: "subscriptions",
                newName: "last_sent_at");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "subscriptions",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "FailCount",
                table: "subscriptions",
                newName: "fail_count");

            migrationBuilder.RenameColumn(
                name: "DeliverOnRefresh",
                table: "subscriptions",
                newName: "deliver_on_refresh");

            migrationBuilder.RenameColumn(
                name: "AtTime",
                table: "subscriptions",
                newName: "at_time");

            migrationBuilder.RenameIndex(
                name: "IX_Subscriptions_UserId",
                table: "subscriptions",
                newName: "ix_subscriptions_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_Subscriptions_ReportId",
                table: "subscriptions",
                newName: "ix_subscriptions_report_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "reports",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Tags",
                table: "reports",
                newName: "tags");

            migrationBuilder.RenameColumn(
                name: "Steward",
                table: "reports",
                newName: "steward");

            migrationBuilder.RenameColumn(
                name: "Owner",
                table: "reports",
                newName: "owner");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "reports",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "reports",
                newName: "kind");

            migrationBuilder.RenameColumn(
                name: "Domain",
                table: "reports",
                newName: "domain");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "reports",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Contact",
                table: "reports",
                newName: "contact");

            migrationBuilder.RenameColumn(
                name: "Certification",
                table: "reports",
                newName: "certification");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "reports",
                newName: "category");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "reports",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "reports",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "reports",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ScriptPath",
                table: "reports",
                newName: "script_path");

            migrationBuilder.RenameColumn(
                name: "ScriptLastModified",
                table: "reports",
                newName: "script_last_modified");

            migrationBuilder.RenameColumn(
                name: "PublishedScriptHash",
                table: "reports",
                newName: "published_script_hash");

            migrationBuilder.RenameColumn(
                name: "MetadataJson",
                table: "reports",
                newName: "metadata_json");

            migrationBuilder.RenameColumn(
                name: "LastViewedAt",
                table: "reports",
                newName: "last_viewed_at");

            migrationBuilder.RenameColumn(
                name: "LastRefreshStatus",
                table: "reports",
                newName: "last_refresh_status");

            migrationBuilder.RenameColumn(
                name: "LastRefreshStartedAt",
                table: "reports",
                newName: "last_refresh_started_at");

            migrationBuilder.RenameColumn(
                name: "LastRefreshError",
                table: "reports",
                newName: "last_refresh_error");

            migrationBuilder.RenameColumn(
                name: "LastRefreshDurationMs",
                table: "reports",
                newName: "last_refresh_duration_ms");

            migrationBuilder.RenameColumn(
                name: "LastRefreshCompletedAt",
                table: "reports",
                newName: "last_refresh_completed_at");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "reports",
                newName: "is_deleted");

            migrationBuilder.RenameColumn(
                name: "FolderId",
                table: "reports",
                newName: "folder_id");

            migrationBuilder.RenameColumn(
                name: "EditSessionUserName",
                table: "reports",
                newName: "edit_session_user_name");

            migrationBuilder.RenameColumn(
                name: "EditSessionUserId",
                table: "reports",
                newName: "edit_session_user_id");

            migrationBuilder.RenameColumn(
                name: "EditSessionExpiresAtUtc",
                table: "reports",
                newName: "edit_session_expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "reports",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "reports",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_Reports_TenantId_FolderId",
                table: "reports",
                newName: "ix_reports_tenant_id_folder_id");

            migrationBuilder.RenameIndex(
                name: "IX_Reports_FolderId",
                table: "reports",
                newName: "ix_reports_folder_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "groups",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Provider",
                table: "groups",
                newName: "provider");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "groups",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "groups",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "groups",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "groups",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "PrincipalKey",
                table: "groups",
                newName: "principal_key");

            migrationBuilder.RenameColumn(
                name: "AdGroup",
                table: "groups",
                newName: "ad_group");

            migrationBuilder.RenameIndex(
                name: "IX_Groups_TenantId_Name",
                table: "groups",
                newName: "ix_groups_tenant_id_name");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "folders",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Path",
                table: "folders",
                newName: "path");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "folders",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "folders",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "folders",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ParentId",
                table: "folders",
                newName: "parent_id");

            migrationBuilder.RenameColumn(
                name: "OwnerId",
                table: "folders",
                newName: "owner_id");

            migrationBuilder.RenameIndex(
                name: "IX_Folders_TenantId_Path",
                table: "folders",
                newName: "ix_folders_tenant_id_path");

            migrationBuilder.RenameIndex(
                name: "IX_Folders_ParentId",
                table: "folders",
                newName: "ix_folders_parent_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "datasets",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Ttl",
                table: "datasets",
                newName: "ttl");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "datasets",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "datasets",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "datasets",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "datasets",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "SourceQuery",
                table: "datasets",
                newName: "source_query");

            migrationBuilder.RenameColumn(
                name: "RowCount",
                table: "datasets",
                newName: "row_count");

            migrationBuilder.RenameColumn(
                name: "RefreshInterval",
                table: "datasets",
                newName: "refresh_interval");

            migrationBuilder.RenameColumn(
                name: "ParquetFilePath",
                table: "datasets",
                newName: "parquet_file_path");

            migrationBuilder.RenameColumn(
                name: "OwningReportId",
                table: "datasets",
                newName: "owning_report_id");

            migrationBuilder.RenameColumn(
                name: "LastRefresh",
                table: "datasets",
                newName: "last_refresh");

            migrationBuilder.RenameColumn(
                name: "FolderPath",
                table: "datasets",
                newName: "folder_path");

            migrationBuilder.RenameColumn(
                name: "FolderId",
                table: "datasets",
                newName: "folder_id");

            migrationBuilder.RenameColumn(
                name: "EncryptionMode",
                table: "datasets",
                newName: "encryption_mode");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "datasets",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "datasets",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ColumnSchema",
                table: "datasets",
                newName: "column_schema");

            migrationBuilder.RenameColumn(
                name: "AtRestKeyVersion",
                table: "datasets",
                newName: "at_rest_key_version");

            migrationBuilder.RenameColumn(
                name: "AccessLevel",
                table: "datasets",
                newName: "access_level");

            migrationBuilder.RenameIndex(
                name: "IX_Datasets_TenantId_Name",
                table: "datasets",
                newName: "ix_datasets_tenant_id_name");

            migrationBuilder.RenameIndex(
                name: "IX_Datasets_OwningReportId",
                table: "datasets",
                newName: "ix_datasets_owning_report_id");

            migrationBuilder.RenameIndex(
                name: "IX_Datasets_FolderId",
                table: "datasets",
                newName: "ix_datasets_folder_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "workload_identity_replays",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UsedAt",
                table: "workload_identity_replays",
                newName: "used_at");

            migrationBuilder.RenameColumn(
                name: "TokenIdHash",
                table: "workload_identity_replays",
                newName: "token_id_hash");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "workload_identity_replays",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "workload_identity_replays",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "BindingId",
                table: "workload_identity_replays",
                newName: "binding_id");

            migrationBuilder.RenameIndex(
                name: "IX_WorkloadIdentityReplays_TenantId_BindingId_TokenIdHash",
                table: "workload_identity_replays",
                newName: "ix_workload_identity_replays_tenant_id_binding_id_token_id_hash");

            migrationBuilder.RenameIndex(
                name: "IX_WorkloadIdentityReplays_ExpiresAt",
                table: "workload_identity_replays",
                newName: "ix_workload_identity_replays_expires_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "user_groups",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "user_groups",
                newName: "group_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "user_groups",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "IX_UserGroups_TenantId_UserId_GroupId",
                table: "user_groups",
                newName: "ix_user_groups_tenant_id_user_id_group_id");

            migrationBuilder.RenameIndex(
                name: "IX_UserGroups_GroupId",
                table: "user_groups",
                newName: "ix_user_groups_group_id");

            migrationBuilder.RenameColumn(
                name: "Recipients",
                table: "subscription_deliveries",
                newName: "recipients");

            migrationBuilder.RenameColumn(
                name: "Outcome",
                table: "subscription_deliveries",
                newName: "outcome");

            migrationBuilder.RenameColumn(
                name: "Detail",
                table: "subscription_deliveries",
                newName: "detail");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "subscription_deliveries",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TriggerKey",
                table: "subscription_deliveries",
                newName: "trigger_key");

            migrationBuilder.RenameColumn(
                name: "SubscriptionId",
                table: "subscription_deliveries",
                newName: "subscription_id");

            migrationBuilder.RenameColumn(
                name: "StartedAt",
                table: "subscription_deliveries",
                newName: "started_at");

            migrationBuilder.RenameColumn(
                name: "RecipientKey",
                table: "subscription_deliveries",
                newName: "recipient_key");

            migrationBuilder.RenameColumn(
                name: "DeliveryId",
                table: "subscription_deliveries",
                newName: "delivery_id");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "subscription_deliveries",
                newName: "completed_at");

            migrationBuilder.RenameIndex(
                name: "IX_SubscriptionDeliveries_SubscriptionId_TriggerKey_RecipientKey",
                table: "subscription_deliveries",
                newName: "ix_subscription_deliveries_subscription_id_trigger_key_recipient_key");

            migrationBuilder.RenameIndex(
                name: "IX_SubscriptionDeliveries_DeliveryId",
                table: "subscription_deliveries",
                newName: "ix_subscription_deliveries_delivery_id");

            migrationBuilder.RenameColumn(
                name: "Content",
                table: "studio_recovery_drafts",
                newName: "content");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "studio_recovery_drafts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "studio_recovery_drafts",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "studio_recovery_drafts",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "studio_recovery_drafts",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "BaseVersion",
                table: "studio_recovery_drafts",
                newName: "base_version");

            migrationBuilder.RenameColumn(
                name: "BaseSourceRevision",
                table: "studio_recovery_drafts",
                newName: "base_source_revision");

            migrationBuilder.RenameIndex(
                name: "IX_StudioRecoveryDrafts_UserId_ReportId",
                table: "studio_recovery_drafts",
                newName: "ix_studio_recovery_drafts_user_id_report_id");

            migrationBuilder.RenameIndex(
                name: "IX_StudioRecoveryDrafts_ReportId",
                table: "studio_recovery_drafts",
                newName: "ix_studio_recovery_drafts_report_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "stewardship_settings",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Scope",
                table: "stewardship_settings",
                newName: "scope");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_settings",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedByUserId",
                table: "stewardship_settings",
                newName: "updated_by_user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "stewardship_settings",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_settings",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "TargetScore",
                table: "stewardship_settings",
                newName: "target_score");

            migrationBuilder.RenameColumn(
                name: "StaleAfterDays",
                table: "stewardship_settings",
                newName: "stale_after_days");

            migrationBuilder.RenameColumn(
                name: "PolicyLevel",
                table: "stewardship_settings",
                newName: "policy_level");

            migrationBuilder.RenameColumn(
                name: "EnableStalenessCheck",
                table: "stewardship_settings",
                newName: "enable_staleness_check");

            migrationBuilder.RenameColumn(
                name: "EnableProtectedDataCheck",
                table: "stewardship_settings",
                newName: "enable_protected_data_check");

            migrationBuilder.RenameColumn(
                name: "EnableMetadataCheck",
                table: "stewardship_settings",
                newName: "enable_metadata_check");

            migrationBuilder.RenameColumn(
                name: "EnableGlossaryCheck",
                table: "stewardship_settings",
                newName: "enable_glossary_check");

            migrationBuilder.RenameColumn(
                name: "DeductStaleness",
                table: "stewardship_settings",
                newName: "deduct_staleness");

            migrationBuilder.RenameColumn(
                name: "DeductProtectedData",
                table: "stewardship_settings",
                newName: "deduct_protected_data");

            migrationBuilder.RenameColumn(
                name: "DeductMetadata",
                table: "stewardship_settings",
                newName: "deduct_metadata");

            migrationBuilder.RenameColumn(
                name: "DeductGlossary",
                table: "stewardship_settings",
                newName: "deduct_glossary");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipSettings_TenantId_Scope",
                table: "stewardship_settings",
                newName: "ix_stewardship_settings_tenant_id_scope");

            migrationBuilder.RenameColumn(
                name: "Trigger",
                table: "stewardship_scans",
                newName: "trigger");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "stewardship_scans",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Error",
                table: "stewardship_scans",
                newName: "error");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_scans",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_scans",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "StartedByUserId",
                table: "stewardship_scans",
                newName: "started_by_user_id");

            migrationBuilder.RenameColumn(
                name: "StartedAtUtc",
                table: "stewardship_scans",
                newName: "started_at_utc");

            migrationBuilder.RenameColumn(
                name: "FindingsResolved",
                table: "stewardship_scans",
                newName: "findings_resolved");

            migrationBuilder.RenameColumn(
                name: "FindingsReopened",
                table: "stewardship_scans",
                newName: "findings_reopened");

            migrationBuilder.RenameColumn(
                name: "FindingsOpened",
                table: "stewardship_scans",
                newName: "findings_opened");

            migrationBuilder.RenameColumn(
                name: "CompletedAtUtc",
                table: "stewardship_scans",
                newName: "completed_at_utc");

            migrationBuilder.RenameColumn(
                name: "AssetsScanned",
                table: "stewardship_scans",
                newName: "assets_scanned");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipScans_TenantId_StartedAtUtc",
                table: "stewardship_scans",
                newName: "ix_stewardship_scans_tenant_id_started_at_utc");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "stewardship_resolution_categories",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "stewardship_resolution_categories",
                newName: "value");

            migrationBuilder.RenameColumn(
                name: "Label",
                table: "stewardship_resolution_categories",
                newName: "label");

            migrationBuilder.RenameColumn(
                name: "Disabled",
                table: "stewardship_resolution_categories",
                newName: "disabled");

            migrationBuilder.RenameColumn(
                name: "Color",
                table: "stewardship_resolution_categories",
                newName: "color");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_resolution_categories",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_resolution_categories",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ExpiryDays",
                table: "stewardship_resolution_categories",
                newName: "expiry_days");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "stewardship_resolution_categories",
                newName: "created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "stewardship_resolution_categories",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipResolutionCategories_TenantId_Value",
                table: "stewardship_resolution_categories",
                newName: "ix_stewardship_resolution_categories_tenant_id_value");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "stewardship_glossary_terms",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Term",
                table: "stewardship_glossary_terms",
                newName: "term");

            migrationBuilder.RenameColumn(
                name: "Steward",
                table: "stewardship_glossary_terms",
                newName: "steward");

            migrationBuilder.RenameColumn(
                name: "Formula",
                table: "stewardship_glossary_terms",
                newName: "formula");

            migrationBuilder.RenameColumn(
                name: "Disabled",
                table: "stewardship_glossary_terms",
                newName: "disabled");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "stewardship_glossary_terms",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Aliases",
                table: "stewardship_glossary_terms",
                newName: "aliases");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_glossary_terms",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedByUserId",
                table: "stewardship_glossary_terms",
                newName: "updated_by_user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "stewardship_glossary_terms",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_glossary_terms",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "DataType",
                table: "stewardship_glossary_terms",
                newName: "data_type");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "stewardship_glossary_terms",
                newName: "created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "stewardship_glossary_terms",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipGlossaryTerms_TenantId_Term",
                table: "stewardship_glossary_terms",
                newName: "ix_stewardship_glossary_terms_tenant_id_term");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "stewardship_findings",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "stewardship_findings",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Detail",
                table: "stewardship_findings",
                newName: "detail");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_findings",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_findings",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "SuppressedUntilUtc",
                table: "stewardship_findings",
                newName: "suppressed_until_utc");

            migrationBuilder.RenameColumn(
                name: "RuleKey",
                table: "stewardship_findings",
                newName: "rule_key");

            migrationBuilder.RenameColumn(
                name: "ResolvedAtUtc",
                table: "stewardship_findings",
                newName: "resolved_at_utc");

            migrationBuilder.RenameColumn(
                name: "LastSeenUtc",
                table: "stewardship_findings",
                newName: "last_seen_utc");

            migrationBuilder.RenameColumn(
                name: "FirstSeenUtc",
                table: "stewardship_findings",
                newName: "first_seen_utc");

            migrationBuilder.RenameColumn(
                name: "AssetVersion",
                table: "stewardship_findings",
                newName: "asset_version");

            migrationBuilder.RenameColumn(
                name: "AssetKey",
                table: "stewardship_findings",
                newName: "asset_key");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipFindings_TenantId_Status",
                table: "stewardship_findings",
                newName: "ix_stewardship_findings_tenant_id_status");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipFindings_TenantId_AssetKey_RuleKey",
                table: "stewardship_findings",
                newName: "ix_stewardship_findings_tenant_id_asset_key_rule_key");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "stewardship_finding_decisions",
                newName: "reason");

            migrationBuilder.RenameColumn(
                name: "Decision",
                table: "stewardship_finding_decisions",
                newName: "decision");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_finding_decisions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_finding_decisions",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "FindingId",
                table: "stewardship_finding_decisions",
                newName: "finding_id");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserName",
                table: "stewardship_finding_decisions",
                newName: "decided_by_user_name");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserId",
                table: "stewardship_finding_decisions",
                newName: "decided_by_user_id");

            migrationBuilder.RenameColumn(
                name: "DecidedAtUtc",
                table: "stewardship_finding_decisions",
                newName: "decided_at_utc");

            migrationBuilder.RenameColumn(
                name: "CategoryValue",
                table: "stewardship_finding_decisions",
                newName: "category_value");

            migrationBuilder.RenameColumn(
                name: "AssetVersion",
                table: "stewardship_finding_decisions",
                newName: "asset_version");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipFindingDecisions_TenantId_FindingId_DecidedAtUtc",
                table: "stewardship_finding_decisions",
                newName: "ix_stewardship_finding_decisions_tenant_id_finding_id_decided_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipFindingDecisions_FindingId",
                table: "stewardship_finding_decisions",
                newName: "ix_stewardship_finding_decisions_finding_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "stewardship_asset_reviews",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Note",
                table: "stewardship_asset_reviews",
                newName: "note");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_asset_reviews",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_asset_reviews",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ReviewedVersion",
                table: "stewardship_asset_reviews",
                newName: "reviewed_version");

            migrationBuilder.RenameColumn(
                name: "ReviewedByUserId",
                table: "stewardship_asset_reviews",
                newName: "reviewed_by_user_id");

            migrationBuilder.RenameColumn(
                name: "ReviewedAtUtc",
                table: "stewardship_asset_reviews",
                newName: "reviewed_at_utc");

            migrationBuilder.RenameColumn(
                name: "AssetKey",
                table: "stewardship_asset_reviews",
                newName: "asset_key");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipAssetReviews_TenantId_AssetKey",
                table: "stewardship_asset_reviews",
                newName: "ix_stewardship_asset_reviews_tenant_id_asset_key");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "stewardship_asset_badges",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "stewardship_asset_badges",
                newName: "reason");

            migrationBuilder.RenameColumn(
                name: "Badge",
                table: "stewardship_asset_badges",
                newName: "badge");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "stewardship_asset_badges",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "stewardship_asset_badges",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "AssignedByUserId",
                table: "stewardship_asset_badges",
                newName: "assigned_by_user_id");

            migrationBuilder.RenameColumn(
                name: "AssignedAtUtc",
                table: "stewardship_asset_badges",
                newName: "assigned_at_utc");

            migrationBuilder.RenameColumn(
                name: "AssetVersion",
                table: "stewardship_asset_badges",
                newName: "asset_version");

            migrationBuilder.RenameColumn(
                name: "AssetKey",
                table: "stewardship_asset_badges",
                newName: "asset_key");

            migrationBuilder.RenameIndex(
                name: "IX_StewardshipAssetBadges_TenantId_AssetKey_Badge",
                table: "stewardship_asset_badges",
                newName: "ix_stewardship_asset_badges_tenant_id_asset_key_badge");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "shared_tenant_resources",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "shared_tenant_resources",
                newName: "kind");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "shared_tenant_resources",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "shared_tenant_resources",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "shared_tenant_resources",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ScopedId",
                table: "shared_tenant_resources",
                newName: "scoped_id");

            migrationBuilder.RenameColumn(
                name: "LogicalId",
                table: "shared_tenant_resources",
                newName: "logical_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "shared_tenant_resources",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_SharedTenantResources_TenantId_ScopedId",
                table: "shared_tenant_resources",
                newName: "ix_shared_tenant_resources_tenant_id_scoped_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedTenantResources_TenantId_Kind_LogicalId",
                table: "shared_tenant_resources",
                newName: "ix_shared_tenant_resources_tenant_id_kind_logical_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedTenantResources_TenantId_Kind_Id",
                table: "shared_tenant_resources",
                newName: "ix_shared_tenant_resources_tenant_id_kind_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "shared_tenant_lifecycles",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "shared_tenant_lifecycles",
                newName: "state");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "shared_tenant_lifecycles",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "MaxStorageMb",
                table: "shared_tenant_lifecycles",
                newName: "max_storage_mb");

            migrationBuilder.RenameColumn(
                name: "MaxReportSessions",
                table: "shared_tenant_lifecycles",
                newName: "max_report_sessions");

            migrationBuilder.RenameColumn(
                name: "MaxConcurrentJobs",
                table: "shared_tenant_lifecycles",
                newName: "max_concurrent_jobs");

            migrationBuilder.RenameColumn(
                name: "FenceEpoch",
                table: "shared_tenant_lifecycles",
                newName: "fence_epoch");

            migrationBuilder.RenameColumn(
                name: "DeletedAtUtc",
                table: "shared_tenant_lifecycles",
                newName: "deleted_at_utc");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "shared_tenant_lifecycles",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "ActiveRelease",
                table: "shared_tenant_lifecycles",
                newName: "active_release");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "shared_tenant_lifecycles",
                newName: "tenant_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedTenantLifecycles_State",
                table: "shared_tenant_lifecycles",
                newName: "ix_shared_tenant_lifecycles_state");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "shared_tenant_lifecycle_operations",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "shared_tenant_lifecycle_operations",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "shared_tenant_lifecycle_operations",
                newName: "reason");

            migrationBuilder.RenameColumn(
                name: "Phase",
                table: "shared_tenant_lifecycle_operations",
                newName: "phase");

            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "shared_tenant_lifecycle_operations",
                newName: "kind");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "shared_tenant_lifecycle_operations",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "shared_tenant_lifecycle_operations",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "TargetRelease",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_release");

            migrationBuilder.RenameColumn(
                name: "TargetPortalHost",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_portal_host");

            migrationBuilder.RenameColumn(
                name: "TargetMaxStorageMb",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_max_storage_mb");

            migrationBuilder.RenameColumn(
                name: "TargetMaxReportSessions",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_max_report_sessions");

            migrationBuilder.RenameColumn(
                name: "TargetMaxConcurrentJobs",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_max_concurrent_jobs");

            migrationBuilder.RenameColumn(
                name: "TargetLoginDomain",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_login_domain");

            migrationBuilder.RenameColumn(
                name: "TargetIssuer",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_issuer");

            migrationBuilder.RenameColumn(
                name: "TargetClientSecretReference",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_client_secret_reference");

            migrationBuilder.RenameColumn(
                name: "TargetClientId",
                table: "shared_tenant_lifecycle_operations",
                newName: "target_client_id");

            migrationBuilder.RenameColumn(
                name: "StartedAtUtc",
                table: "shared_tenant_lifecycle_operations",
                newName: "started_at_utc");

            migrationBuilder.RenameColumn(
                name: "PlatformOperator",
                table: "shared_tenant_lifecycle_operations",
                newName: "platform_operator");

            migrationBuilder.RenameColumn(
                name: "FailureCode",
                table: "shared_tenant_lifecycle_operations",
                newName: "failure_code");

            migrationBuilder.RenameColumn(
                name: "CompletedAtUtc",
                table: "shared_tenant_lifecycle_operations",
                newName: "completed_at_utc");

            migrationBuilder.RenameColumn(
                name: "AuthorizationReference",
                table: "shared_tenant_lifecycle_operations",
                newName: "authorization_reference");

            migrationBuilder.RenameColumn(
                name: "AuthorizationExpiresUtc",
                table: "shared_tenant_lifecycle_operations",
                newName: "authorization_expires_utc");

            migrationBuilder.RenameColumn(
                name: "OperationId",
                table: "shared_tenant_lifecycle_operations",
                newName: "operation_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedTenantLifecycleOperations_TenantId_Status",
                table: "shared_tenant_lifecycle_operations",
                newName: "ix_shared_tenant_lifecycle_operations_tenant_id_status");

            migrationBuilder.RenameIndex(
                name: "IX_SharedTenantLifecycleOperations_Kind_AuthorizationReference",
                table: "shared_tenant_lifecycle_operations",
                newName: "ix_shared_tenant_lifecycle_operations_kind_authorization_reference");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "shared_identity_authorities",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Issuer",
                table: "shared_identity_authorities",
                newName: "issuer");

            migrationBuilder.RenameColumn(
                name: "Enabled",
                table: "shared_identity_authorities",
                newName: "enabled");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "shared_identity_authorities",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "shared_identity_authorities",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "shared_identity_authorities",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "PortalHost",
                table: "shared_identity_authorities",
                newName: "portal_host");

            migrationBuilder.RenameColumn(
                name: "LoginDomain",
                table: "shared_identity_authorities",
                newName: "login_domain");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "shared_identity_authorities",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "ClientSecretReference",
                table: "shared_identity_authorities",
                newName: "client_secret_reference");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "shared_identity_authorities",
                newName: "client_id");

            migrationBuilder.RenameColumn(
                name: "AuthorityId",
                table: "shared_identity_authorities",
                newName: "authority_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedIdentityAuthorities_TenantId_Issuer",
                table: "shared_identity_authorities",
                newName: "ix_shared_identity_authorities_tenant_id_issuer");

            migrationBuilder.RenameIndex(
                name: "IX_SharedIdentityAuthorities_PortalHost",
                table: "shared_identity_authorities",
                newName: "ix_shared_identity_authorities_portal_host");

            migrationBuilder.RenameIndex(
                name: "IX_SharedIdentityAuthorities_LoginDomain",
                table: "shared_identity_authorities",
                newName: "ix_shared_identity_authorities_login_domain");

            migrationBuilder.RenameIndex(
                name: "IX_SharedIdentityAuthorities_AuthorityId",
                table: "shared_identity_authorities",
                newName: "ix_shared_identity_authorities_authority_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "shared_connection_usages",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UseCount",
                table: "shared_connection_usages",
                newName: "use_count");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "shared_connection_usages",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "SharedConnectionId",
                table: "shared_connection_usages",
                newName: "shared_connection_id");

            migrationBuilder.RenameColumn(
                name: "LastUsedAtUtc",
                table: "shared_connection_usages",
                newName: "last_used_at_utc");

            migrationBuilder.RenameColumn(
                name: "ConsumerUser",
                table: "shared_connection_usages",
                newName: "consumer_user");

            migrationBuilder.RenameIndex(
                name: "IX_SharedConnectionUsages_TenantId_SharedConnectionId_ConsumerUser",
                table: "shared_connection_usages",
                newName: "ix_shared_connection_usages_tenant_id_shared_connection_id_consumer_user");

            migrationBuilder.RenameIndex(
                name: "IX_SharedConnectionUsages_SharedConnectionId",
                table: "shared_connection_usages",
                newName: "ix_shared_connection_usages_shared_connection_id");

            migrationBuilder.RenameColumn(
                name: "Permission",
                table: "shared_connection_acls",
                newName: "permission");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "shared_connection_acls",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "shared_connection_acls",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "SharedConnectionId",
                table: "shared_connection_acls",
                newName: "shared_connection_id");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "shared_connection_acls",
                newName: "group_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedConnectionAcls_TenantId_SharedConnectionId_GroupId",
                table: "shared_connection_acls",
                newName: "ix_shared_connection_acls_tenant_id_shared_connection_id_group_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedConnectionAcls_SharedConnectionId",
                table: "shared_connection_acls",
                newName: "ix_shared_connection_acls_shared_connection_id");

            migrationBuilder.RenameIndex(
                name: "IX_SharedConnectionAcls_GroupId",
                table: "shared_connection_acls",
                newName: "ix_shared_connection_acls_group_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "service_accounts",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Scopes",
                table: "service_accounts",
                newName: "scopes");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "service_accounts",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "service_accounts",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "service_accounts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "service_accounts",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "service_accounts",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "StudioCapabilities",
                table: "service_accounts",
                newName: "studio_capabilities");

            migrationBuilder.RenameColumn(
                name: "SecurityStamp",
                table: "service_accounts",
                newName: "security_stamp");

            migrationBuilder.RenameColumn(
                name: "SecretHash",
                table: "service_accounts",
                newName: "secret_hash");

            migrationBuilder.RenameColumn(
                name: "RoleNames",
                table: "service_accounts",
                newName: "role_names");

            migrationBuilder.RenameColumn(
                name: "RevokedAt",
                table: "service_accounts",
                newName: "revoked_at");

            migrationBuilder.RenameColumn(
                name: "OwnerUserId",
                table: "service_accounts",
                newName: "owner_user_id");

            migrationBuilder.RenameColumn(
                name: "NormalizedName",
                table: "service_accounts",
                newName: "normalized_name");

            migrationBuilder.RenameColumn(
                name: "LastUsedAt",
                table: "service_accounts",
                newName: "last_used_at");

            migrationBuilder.RenameColumn(
                name: "IsEnabled",
                table: "service_accounts",
                newName: "is_enabled");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "service_accounts",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "service_accounts",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "service_accounts",
                newName: "client_id");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceAccounts_TenantId_NormalizedName",
                table: "service_accounts",
                newName: "ix_service_accounts_tenant_id_normalized_name");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceAccounts_OwnerUserId",
                table: "service_accounts",
                newName: "ix_service_accounts_owner_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_ServiceAccounts_ClientId",
                table: "service_accounts",
                newName: "ix_service_accounts_client_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "saved_report_views",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "saved_report_views",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "saved_report_views",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "saved_report_views",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "StateJson",
                table: "saved_report_views",
                newName: "state_json");

            migrationBuilder.RenameColumn(
                name: "ScriptHash",
                table: "saved_report_views",
                newName: "script_hash");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "saved_report_views",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "ParametersJson",
                table: "saved_report_views",
                newName: "parameters_json");

            migrationBuilder.RenameColumn(
                name: "IsDefault",
                table: "saved_report_views",
                newName: "is_default");

            migrationBuilder.RenameColumn(
                name: "FiltersJson",
                table: "saved_report_views",
                newName: "filters_json");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "saved_report_views",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_SavedReportViews_UserId_ReportId_Name",
                table: "saved_report_views",
                newName: "ix_saved_report_views_user_id_report_id_name");

            migrationBuilder.RenameIndex(
                name: "IX_SavedReportViews_ReportId",
                table: "saved_report_views",
                newName: "ix_saved_report_views_report_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_snapshots",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ScriptHashAtRunTime",
                table: "report_snapshots",
                newName: "script_hash_at_run_time");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_snapshots",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "ParametersJson",
                table: "report_snapshots",
                newName: "parameters_json");

            migrationBuilder.RenameColumn(
                name: "ManifestPath",
                table: "report_snapshots",
                newName: "manifest_path");

            migrationBuilder.RenameColumn(
                name: "HashMatched",
                table: "report_snapshots",
                newName: "hash_matched");

            migrationBuilder.RenameColumn(
                name: "BuiltBy",
                table: "report_snapshots",
                newName: "built_by");

            migrationBuilder.RenameColumn(
                name: "BuiltAt",
                table: "report_snapshots",
                newName: "built_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportSnapshots_ReportId",
                table: "report_snapshots",
                newName: "ix_report_snapshots_report_id");

            migrationBuilder.RenameColumn(
                name: "Token",
                table: "report_share_links",
                newName: "token");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "report_share_links",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_share_links",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RevokedAt",
                table: "report_share_links",
                newName: "revoked_at");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_share_links",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "report_share_links",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "report_share_links",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "report_share_links",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportShareLinks_Token",
                table: "report_share_links",
                newName: "ix_report_share_links_token");

            migrationBuilder.RenameIndex(
                name: "IX_ReportShareLinks_ReportId_Name",
                table: "report_share_links",
                newName: "ix_report_share_links_report_id_name");

            migrationBuilder.RenameIndex(
                name: "IX_ReportShareLinks_CreatedBy",
                table: "report_share_links",
                newName: "ix_report_share_links_created_by");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "report_script_drafts",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "report_script_drafts",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_script_drafts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "report_script_drafts",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "SubmittedAtUtc",
                table: "report_script_drafts",
                newName: "submitted_at_utc");

            migrationBuilder.RenameColumn(
                name: "ScriptText",
                table: "report_script_drafts",
                newName: "script_text");

            migrationBuilder.RenameColumn(
                name: "ScriptHash",
                table: "report_script_drafts",
                newName: "script_hash");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_script_drafts",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "PublishedAtUtc",
                table: "report_script_drafts",
                newName: "published_at_utc");

            migrationBuilder.RenameColumn(
                name: "EditSessionUserName",
                table: "report_script_drafts",
                newName: "edit_session_user_name");

            migrationBuilder.RenameColumn(
                name: "EditSessionUserId",
                table: "report_script_drafts",
                newName: "edit_session_user_id");

            migrationBuilder.RenameColumn(
                name: "EditSessionExpiresAtUtc",
                table: "report_script_drafts",
                newName: "edit_session_expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "DecidedAtUtc",
                table: "report_script_drafts",
                newName: "decided_at_utc");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "report_script_drafts",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "BaseScriptHash",
                table: "report_script_drafts",
                newName: "base_script_hash");

            migrationBuilder.RenameColumn(
                name: "AuthorUserName",
                table: "report_script_drafts",
                newName: "author_user_name");

            migrationBuilder.RenameColumn(
                name: "AuthorUserId",
                table: "report_script_drafts",
                newName: "author_user_id");

            migrationBuilder.RenameColumn(
                name: "ApprovedByUserName",
                table: "report_script_drafts",
                newName: "approved_by_user_name");

            migrationBuilder.RenameColumn(
                name: "ApprovedByUserId",
                table: "report_script_drafts",
                newName: "approved_by_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_ReportScriptDrafts_ReportId_Status",
                table: "report_script_drafts",
                newName: "ix_report_script_drafts_report_id_status");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "report_script_draft_decisions",
                newName: "reason");

            migrationBuilder.RenameColumn(
                name: "Decision",
                table: "report_script_draft_decisions",
                newName: "decision");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_script_draft_decisions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ScriptHash",
                table: "report_script_draft_decisions",
                newName: "script_hash");

            migrationBuilder.RenameColumn(
                name: "DraftId",
                table: "report_script_draft_decisions",
                newName: "draft_id");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserName",
                table: "report_script_draft_decisions",
                newName: "decided_by_user_name");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserId",
                table: "report_script_draft_decisions",
                newName: "decided_by_user_id");

            migrationBuilder.RenameColumn(
                name: "DecidedAtUtc",
                table: "report_script_draft_decisions",
                newName: "decided_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_ReportScriptDraftDecisions_DraftId_DecidedAtUtc",
                table: "report_script_draft_decisions",
                newName: "ix_report_script_draft_decisions_draft_id_decided_at_utc");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_job_links",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "report_job_links",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_job_links",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "OrchestratorAlias",
                table: "report_job_links",
                newName: "orchestrator_alias");

            migrationBuilder.RenameColumn(
                name: "LastRefreshedAt",
                table: "report_job_links",
                newName: "last_refreshed_at");

            migrationBuilder.RenameColumn(
                name: "JobName",
                table: "report_job_links",
                newName: "job_name");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "report_job_links",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportJobLinks_ReportId_OrchestratorAlias_JobName",
                table: "report_job_links",
                newName: "ix_report_job_links_report_id_orchestrator_alias_job_name");

            migrationBuilder.RenameIndex(
                name: "IX_ReportJobLinks_JobName",
                table: "report_job_links",
                newName: "ix_report_job_links_job_name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_favorites",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "report_favorites",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_favorites",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "report_favorites",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportFavorites_UserId_ReportId",
                table: "report_favorites",
                newName: "ix_report_favorites_user_id_report_id");

            migrationBuilder.RenameIndex(
                name: "IX_ReportFavorites_ReportId",
                table: "report_favorites",
                newName: "ix_report_favorites_report_id");

            migrationBuilder.RenameColumn(
                name: "Token",
                table: "report_embed_tokens",
                newName: "token");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "report_embed_tokens",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_embed_tokens",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RevokedAt",
                table: "report_embed_tokens",
                newName: "revoked_at");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_embed_tokens",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "report_embed_tokens",
                newName: "expires_at");

            migrationBuilder.RenameColumn(
                name: "CreatedBy",
                table: "report_embed_tokens",
                newName: "created_by");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "report_embed_tokens",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportEmbedTokens_Token",
                table: "report_embed_tokens",
                newName: "ix_report_embed_tokens_token");

            migrationBuilder.RenameIndex(
                name: "IX_ReportEmbedTokens_ReportId_Name",
                table: "report_embed_tokens",
                newName: "ix_report_embed_tokens_report_id_name");

            migrationBuilder.RenameIndex(
                name: "IX_ReportEmbedTokens_CreatedBy",
                table: "report_embed_tokens",
                newName: "ix_report_embed_tokens_created_by");

            migrationBuilder.RenameColumn(
                name: "Threshold",
                table: "report_alerts",
                newName: "threshold");

            migrationBuilder.RenameColumn(
                name: "Recipient",
                table: "report_alerts",
                newName: "recipient");

            migrationBuilder.RenameColumn(
                name: "Operator",
                table: "report_alerts",
                newName: "operator");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "report_alerts",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "report_alerts",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_alerts",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "VisualName",
                table: "report_alerts",
                newName: "visual_name");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "report_alerts",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SmtpAlias",
                table: "report_alerts",
                newName: "smtp_alias");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_alerts",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "OwnerId",
                table: "report_alerts",
                newName: "owner_id");

            migrationBuilder.RenameColumn(
                name: "OptionsJson",
                table: "report_alerts",
                newName: "options_json");

            migrationBuilder.RenameColumn(
                name: "LastTriggeredAt",
                table: "report_alerts",
                newName: "last_triggered_at");

            migrationBuilder.RenameColumn(
                name: "LastState",
                table: "report_alerts",
                newName: "last_state");

            migrationBuilder.RenameColumn(
                name: "LastNotifiedAt",
                table: "report_alerts",
                newName: "last_notified_at");

            migrationBuilder.RenameColumn(
                name: "LastEvaluatedAt",
                table: "report_alerts",
                newName: "last_evaluated_at");

            migrationBuilder.RenameColumn(
                name: "LastCheckedAt",
                table: "report_alerts",
                newName: "last_checked_at");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "report_alerts",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "report_alerts",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "report_alerts",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportAlerts_ReportId",
                table: "report_alerts",
                newName: "ix_report_alerts_report_id");

            migrationBuilder.RenameIndex(
                name: "IX_ReportAlerts_OwnerId",
                table: "report_alerts",
                newName: "ix_report_alerts_owner_id");

            migrationBuilder.RenameIndex(
                name: "IX_ReportAlerts_Name",
                table: "report_alerts",
                newName: "ix_report_alerts_name");

            migrationBuilder.RenameColumn(
                name: "Permission",
                table: "report_acls",
                newName: "permission");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_acls",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "report_acls",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_acls",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "report_acls",
                newName: "group_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "report_acls",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportAcls_UserId",
                table: "report_acls",
                newName: "ix_report_acls_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_ReportAcls_GroupId",
                table: "report_acls",
                newName: "ix_report_acls_group_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "report_access_requests",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "report_access_requests",
                newName: "reason");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "report_access_requests",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "report_access_requests",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "RequesterUserId",
                table: "report_access_requests",
                newName: "requester_user_id");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "report_access_requests",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "DecisionReason",
                table: "report_access_requests",
                newName: "decision_reason");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserId",
                table: "report_access_requests",
                newName: "decided_by_user_id");

            migrationBuilder.RenameColumn(
                name: "DecidedAt",
                table: "report_access_requests",
                newName: "decided_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "report_access_requests",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_ReportAccessRequests_ReportId",
                table: "report_access_requests",
                newName: "ix_report_access_requests_report_id");

            migrationBuilder.RenameIndex(
                name: "IX_ReportAccessRequests_DecidedByUserId",
                table: "report_access_requests",
                newName: "ix_report_access_requests_decided_by_user_id");

            migrationBuilder.RenameColumn(
                name: "Token",
                table: "refresh_tokens",
                newName: "token");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "refresh_tokens",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "refresh_tokens",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "refresh_tokens",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "RevokedAt",
                table: "refresh_tokens",
                newName: "revoked_at");

            migrationBuilder.RenameColumn(
                name: "ExpiresAt",
                table: "refresh_tokens",
                newName: "expires_at");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_UserId",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_Token",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_token");

            migrationBuilder.RenameIndex(
                name: "IX_RefreshTokens_TenantId_UserId",
                table: "refresh_tokens",
                newName: "ix_refresh_tokens_tenant_id_user_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "portal_shared_connections",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Target",
                table: "portal_shared_connections",
                newName: "target");

            migrationBuilder.RenameColumn(
                name: "Disabled",
                table: "portal_shared_connections",
                newName: "disabled");

            migrationBuilder.RenameColumn(
                name: "Alias",
                table: "portal_shared_connections",
                newName: "alias");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "portal_shared_connections",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedByUserId",
                table: "portal_shared_connections",
                newName: "updated_by_user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "portal_shared_connections",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "portal_shared_connections",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "SensitiveFieldsCsv",
                table: "portal_shared_connections",
                newName: "sensitive_fields_csv");

            migrationBuilder.RenameColumn(
                name: "OwnerUserId",
                table: "portal_shared_connections",
                newName: "owner_user_id");

            migrationBuilder.RenameColumn(
                name: "OptionsJson",
                table: "portal_shared_connections",
                newName: "options_json");

            migrationBuilder.RenameColumn(
                name: "LastVerifiedAtUtc",
                table: "portal_shared_connections",
                newName: "last_verified_at_utc");

            migrationBuilder.RenameColumn(
                name: "LastUsedAtUtc",
                table: "portal_shared_connections",
                newName: "last_used_at_utc");

            migrationBuilder.RenameColumn(
                name: "EnvironmentScope",
                table: "portal_shared_connections",
                newName: "environment_scope");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "portal_shared_connections",
                newName: "created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "portal_shared_connections",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "ConnectorType",
                table: "portal_shared_connections",
                newName: "connector_type");

            migrationBuilder.RenameIndex(
                name: "IX_PortalSharedConnections_TenantId_Alias",
                table: "portal_shared_connections",
                newName: "ix_portal_shared_connections_tenant_id_alias");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "portal_secrets",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "portal_secrets",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Disabled",
                table: "portal_secrets",
                newName: "disabled");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "portal_secrets",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedByUserId",
                table: "portal_secrets",
                newName: "updated_by_user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "portal_secrets",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "portal_secrets",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "EncryptedValue",
                table: "portal_secrets",
                newName: "encrypted_value");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "portal_secrets",
                newName: "created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "portal_secrets",
                newName: "created_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_PortalSecrets_TenantId_Name",
                table: "portal_secrets",
                newName: "ix_portal_secrets_tenant_id_name");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "portal_execution_jobs",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "portal_execution_jobs",
                newName: "kind");

            migrationBuilder.RenameColumn(
                name: "Error",
                table: "portal_execution_jobs",
                newName: "error");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "portal_execution_jobs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "portal_execution_jobs",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "portal_execution_jobs",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "StartedAt",
                table: "portal_execution_jobs",
                newName: "started_at");

            migrationBuilder.RenameColumn(
                name: "RowsProcessed",
                table: "portal_execution_jobs",
                newName: "rows_processed");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "portal_execution_jobs",
                newName: "report_id");

            migrationBuilder.RenameColumn(
                name: "PeakMemoryBytes",
                table: "portal_execution_jobs",
                newName: "peak_memory_bytes");

            migrationBuilder.RenameColumn(
                name: "ManifestPath",
                table: "portal_execution_jobs",
                newName: "manifest_path");

            migrationBuilder.RenameColumn(
                name: "EffectiveScopes",
                table: "portal_execution_jobs",
                newName: "effective_scopes");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "portal_execution_jobs",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CpuTimeSeconds",
                table: "portal_execution_jobs",
                newName: "cpu_time_seconds");

            migrationBuilder.RenameColumn(
                name: "CorrelationId",
                table: "portal_execution_jobs",
                newName: "correlation_id");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "portal_execution_jobs",
                newName: "completed_at");

            migrationBuilder.RenameColumn(
                name: "ActorType",
                table: "portal_execution_jobs",
                newName: "actor_type");

            migrationBuilder.RenameColumn(
                name: "ActorId",
                table: "portal_execution_jobs",
                newName: "actor_id");

            migrationBuilder.RenameIndex(
                name: "IX_PortalExecutionJobs_TenantId_CompletedAt",
                table: "portal_execution_jobs",
                newName: "ix_portal_execution_jobs_tenant_id_completed_at");

            migrationBuilder.RenameColumn(
                name: "Tenant",
                table: "policy_versions",
                newName: "tenant");

            migrationBuilder.RenameColumn(
                name: "Reviewer",
                table: "policy_versions",
                newName: "reviewer");

            migrationBuilder.RenameColumn(
                name: "Environment",
                table: "policy_versions",
                newName: "environment");

            migrationBuilder.RenameColumn(
                name: "Author",
                table: "policy_versions",
                newName: "author");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "policy_versions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "SupersededVersion",
                table: "policy_versions",
                newName: "superseded_version");

            migrationBuilder.RenameColumn(
                name: "SignedEnvelopeJson",
                table: "policy_versions",
                newName: "signed_envelope_json");

            migrationBuilder.RenameColumn(
                name: "RolloutState",
                table: "policy_versions",
                newName: "rollout_state");

            migrationBuilder.RenameColumn(
                name: "PublishedAtUtc",
                table: "policy_versions",
                newName: "published_at_utc");

            migrationBuilder.RenameColumn(
                name: "PolicyVersion",
                table: "policy_versions",
                newName: "policy_version");

            migrationBuilder.RenameColumn(
                name: "PolicyHash",
                table: "policy_versions",
                newName: "policy_hash");

            migrationBuilder.RenameColumn(
                name: "IssuedAtUtc",
                table: "policy_versions",
                newName: "issued_at_utc");

            migrationBuilder.RenameColumn(
                name: "ExpiresAtUtc",
                table: "policy_versions",
                newName: "expires_at_utc");

            migrationBuilder.RenameColumn(
                name: "CanaryPercentage",
                table: "policy_versions",
                newName: "canary_percentage");

            migrationBuilder.RenameColumn(
                name: "CanaryGroup",
                table: "policy_versions",
                newName: "canary_group");

            migrationBuilder.RenameIndex(
                name: "IX_PolicyVersions_Tenant_Environment_RolloutState",
                table: "policy_versions",
                newName: "ix_policy_versions_tenant_environment_rollout_state");

            migrationBuilder.RenameIndex(
                name: "IX_PolicyVersions_Tenant_Environment_PolicyVersion",
                table: "policy_versions",
                newName: "ix_policy_versions_tenant_environment_policy_version");

            migrationBuilder.RenameColumn(
                name: "Tenant",
                table: "policy_machines",
                newName: "tenant");

            migrationBuilder.RenameColumn(
                name: "Revoked",
                table: "policy_machines",
                newName: "revoked");

            migrationBuilder.RenameColumn(
                name: "Environment",
                table: "policy_machines",
                newName: "environment");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "policy_machines",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RevokedReason",
                table: "policy_machines",
                newName: "revoked_reason");

            migrationBuilder.RenameColumn(
                name: "RevokedAtUtc",
                table: "policy_machines",
                newName: "revoked_at_utc");

            migrationBuilder.RenameColumn(
                name: "RegisteredAtUtc",
                table: "policy_machines",
                newName: "registered_at_utc");

            migrationBuilder.RenameColumn(
                name: "MachineId",
                table: "policy_machines",
                newName: "machine_id");

            migrationBuilder.RenameColumn(
                name: "LastSeenAtUtc",
                table: "policy_machines",
                newName: "last_seen_at_utc");

            migrationBuilder.RenameColumn(
                name: "EnrollmentId",
                table: "policy_machines",
                newName: "enrollment_id");

            migrationBuilder.RenameColumn(
                name: "ClientCertificateThumbprint",
                table: "policy_machines",
                newName: "client_certificate_thumbprint");

            migrationBuilder.RenameColumn(
                name: "CanaryGroup",
                table: "policy_machines",
                newName: "canary_group");

            migrationBuilder.RenameIndex(
                name: "IX_PolicyMachines_Tenant_Environment",
                table: "policy_machines",
                newName: "ix_policy_machines_tenant_environment");

            migrationBuilder.RenameIndex(
                name: "IX_PolicyMachines_MachineId",
                table: "policy_machines",
                newName: "ix_policy_machines_machine_id");

            migrationBuilder.RenameColumn(
                name: "Capability",
                table: "group_studio_capabilities",
                newName: "capability");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "group_studio_capabilities",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "group_studio_capabilities",
                newName: "group_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "group_studio_capabilities",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_GroupStudioCapabilities_GroupId_Capability",
                table: "group_studio_capabilities",
                newName: "ix_group_studio_capabilities_group_id_capability");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "gateway_enrollments",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "gateway_enrollments",
                newName: "state");

            migrationBuilder.RenameColumn(
                name: "WorkloadPublicKeyThumbprint",
                table: "gateway_enrollments",
                newName: "workload_public_key_thumbprint");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "gateway_enrollments",
                newName: "token_hash");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "gateway_enrollments",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "GatewayId",
                table: "gateway_enrollments",
                newName: "gateway_id");

            migrationBuilder.RenameColumn(
                name: "ExpiresUtc",
                table: "gateway_enrollments",
                newName: "expires_utc");

            migrationBuilder.RenameColumn(
                name: "CreatedUtc",
                table: "gateway_enrollments",
                newName: "created_utc");

            migrationBuilder.RenameColumn(
                name: "ConsumedUtc",
                table: "gateway_enrollments",
                newName: "consumed_utc");

            migrationBuilder.RenameColumn(
                name: "EnrollmentId",
                table: "gateway_enrollments",
                newName: "enrollment_id");

            migrationBuilder.RenameIndex(
                name: "IX_GatewayEnrollments_TenantId_TokenHash",
                table: "gateway_enrollments",
                newName: "ix_gateway_enrollments_tenant_id_token_hash");

            migrationBuilder.RenameIndex(
                name: "IX_GatewayEnrollments_TenantId_GatewayId",
                table: "gateway_enrollments",
                newName: "ix_gateway_enrollments_tenant_id_gateway_id");

            migrationBuilder.RenameColumn(
                name: "Resolution",
                table: "gateway_ambiguous_write_events",
                newName: "resolution");

            migrationBuilder.RenameColumn(
                name: "Note",
                table: "gateway_ambiguous_write_events",
                newName: "note");

            migrationBuilder.RenameColumn(
                name: "Actor",
                table: "gateway_ambiguous_write_events",
                newName: "actor");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "gateway_ambiguous_write_events",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "gateway_ambiguous_write_events",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "EvidenceReference",
                table: "gateway_ambiguous_write_events",
                newName: "evidence_reference");

            migrationBuilder.RenameColumn(
                name: "EventType",
                table: "gateway_ambiguous_write_events",
                newName: "event_type");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "gateway_ambiguous_write_events",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "CaseId",
                table: "gateway_ambiguous_write_events",
                newName: "case_id");

            migrationBuilder.RenameIndex(
                name: "IX_GatewayAmbiguousWriteEvents_TenantId_CaseId_Id",
                table: "gateway_ambiguous_write_events",
                newName: "ix_gateway_ambiguous_write_events_tenant_id_case_id_id");

            migrationBuilder.RenameIndex(
                name: "IX_GatewayAmbiguousWriteEvents_CaseId",
                table: "gateway_ambiguous_write_events",
                newName: "ix_gateway_ambiguous_write_events_case_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "gateway_ambiguous_write_cases",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "gateway_ambiguous_write_cases",
                newName: "state");

            migrationBuilder.RenameColumn(
                name: "Resolution",
                table: "gateway_ambiguous_write_cases",
                newName: "resolution");

            migrationBuilder.RenameColumn(
                name: "Priority",
                table: "gateway_ambiguous_write_cases",
                newName: "priority");

            migrationBuilder.RenameColumn(
                name: "Owner",
                table: "gateway_ambiguous_write_cases",
                newName: "owner");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "gateway_ambiguous_write_cases",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "gateway_ambiguous_write_cases",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "gateway_ambiguous_write_cases",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "ResourceId",
                table: "gateway_ambiguous_write_cases",
                newName: "resource_id");

            migrationBuilder.RenameColumn(
                name: "OperationId",
                table: "gateway_ambiguous_write_cases",
                newName: "operation_id");

            migrationBuilder.RenameColumn(
                name: "GatewayId",
                table: "gateway_ambiguous_write_cases",
                newName: "gateway_id");

            migrationBuilder.RenameColumn(
                name: "ExecutedAtUtc",
                table: "gateway_ambiguous_write_cases",
                newName: "executed_at_utc");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "gateway_ambiguous_write_cases",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "CorrelationId",
                table: "gateway_ambiguous_write_cases",
                newName: "correlation_id");

            migrationBuilder.RenameIndex(
                name: "IX_GatewayAmbiguousWriteCases_TenantId_OperationId",
                table: "gateway_ambiguous_write_cases",
                newName: "ix_gateway_ambiguous_write_cases_tenant_id_operation_id");

            migrationBuilder.RenameColumn(
                name: "Permission",
                table: "folder_acls",
                newName: "permission");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "folder_acls",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "folder_acls",
                newName: "group_id");

            migrationBuilder.RenameColumn(
                name: "FolderId",
                table: "folder_acls",
                newName: "folder_id");

            migrationBuilder.RenameIndex(
                name: "IX_FolderAcls_GroupId",
                table: "folder_acls",
                newName: "ix_folder_acls_group_id");

            migrationBuilder.RenameIndex(
                name: "IX_FolderAcls_FolderId",
                table: "folder_acls",
                newName: "ix_folder_acls_folder_id");

            migrationBuilder.RenameColumn(
                name: "Permission",
                table: "dataset_user_acls",
                newName: "permission");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "dataset_user_acls",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "dataset_user_acls",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "DatasetId",
                table: "dataset_user_acls",
                newName: "dataset_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "dataset_user_acls",
                newName: "created_at");

            migrationBuilder.RenameIndex(
                name: "IX_DatasetUserAcls_UserId",
                table: "dataset_user_acls",
                newName: "ix_dataset_user_acls_user_id");

            migrationBuilder.RenameIndex(
                name: "IX_DatasetUserAcls_DatasetId_UserId",
                table: "dataset_user_acls",
                newName: "ix_dataset_user_acls_dataset_id_user_id");

            migrationBuilder.RenameColumn(
                name: "Permission",
                table: "dataset_acls",
                newName: "permission");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "dataset_acls",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "GroupId",
                table: "dataset_acls",
                newName: "group_id");

            migrationBuilder.RenameColumn(
                name: "DatasetId",
                table: "dataset_acls",
                newName: "dataset_id");

            migrationBuilder.RenameIndex(
                name: "IX_DatasetAcls_GroupId",
                table: "dataset_acls",
                newName: "ix_dataset_acls_group_id");

            migrationBuilder.RenameIndex(
                name: "IX_DatasetAcls_DatasetId",
                table: "dataset_acls",
                newName: "ix_dataset_acls_dataset_id");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "audit_outbox_messages",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Attempts",
                table: "audit_outbox_messages",
                newName: "attempts");

            migrationBuilder.RenameColumn(
                name: "Action",
                table: "audit_outbox_messages",
                newName: "action");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "audit_outbox_messages",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "audit_outbox_messages",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "audit_outbox_messages",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "audit_outbox_messages",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "StudioCapability",
                table: "audit_outbox_messages",
                newName: "studio_capability");

            migrationBuilder.RenameColumn(
                name: "ResourceType",
                table: "audit_outbox_messages",
                newName: "resource_type");

            migrationBuilder.RenameColumn(
                name: "ResourceId",
                table: "audit_outbox_messages",
                newName: "resource_id");

            migrationBuilder.RenameColumn(
                name: "PayloadJson",
                table: "audit_outbox_messages",
                newName: "payload_json");

            migrationBuilder.RenameColumn(
                name: "OccurredAt",
                table: "audit_outbox_messages",
                newName: "occurred_at");

            migrationBuilder.RenameColumn(
                name: "NextAttemptAt",
                table: "audit_outbox_messages",
                newName: "next_attempt_at");

            migrationBuilder.RenameColumn(
                name: "LockedUntil",
                table: "audit_outbox_messages",
                newName: "locked_until");

            migrationBuilder.RenameColumn(
                name: "LastError",
                table: "audit_outbox_messages",
                newName: "last_error");

            migrationBuilder.RenameColumn(
                name: "EventId",
                table: "audit_outbox_messages",
                newName: "event_id");

            migrationBuilder.RenameColumn(
                name: "EffectiveScopes",
                table: "audit_outbox_messages",
                newName: "effective_scopes");

            migrationBuilder.RenameColumn(
                name: "DeliveredAt",
                table: "audit_outbox_messages",
                newName: "delivered_at");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "audit_outbox_messages",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CorrelationId",
                table: "audit_outbox_messages",
                newName: "correlation_id");

            migrationBuilder.RenameColumn(
                name: "AuditLogId",
                table: "audit_outbox_messages",
                newName: "audit_log_id");

            migrationBuilder.RenameColumn(
                name: "ActorType",
                table: "audit_outbox_messages",
                newName: "actor_type");

            migrationBuilder.RenameColumn(
                name: "ActorId",
                table: "audit_outbox_messages",
                newName: "actor_id");

            migrationBuilder.RenameIndex(
                name: "IX_AuditOutboxMessages_TenantId_Status_NextAttemptAt",
                table: "audit_outbox_messages",
                newName: "ix_audit_outbox_messages_tenant_id_status_next_attempt_at");

            migrationBuilder.RenameIndex(
                name: "IX_AuditOutboxMessages_TenantId_EventId",
                table: "audit_outbox_messages",
                newName: "ix_audit_outbox_messages_tenant_id_event_id");

            migrationBuilder.RenameIndex(
                name: "IX_AuditOutboxMessages_TenantId_AuditLogId",
                table: "audit_outbox_messages",
                newName: "ix_audit_outbox_messages_tenant_id_audit_log_id");

            migrationBuilder.RenameIndex(
                name: "IX_AuditOutboxMessages_AuditLogId",
                table: "audit_outbox_messages",
                newName: "ix_audit_outbox_messages_audit_log_id");

            migrationBuilder.RenameColumn(
                name: "Timestamp",
                table: "audit_logs",
                newName: "timestamp");

            migrationBuilder.RenameColumn(
                name: "Detail",
                table: "audit_logs",
                newName: "detail");

            migrationBuilder.RenameColumn(
                name: "Action",
                table: "audit_logs",
                newName: "action");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "audit_logs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "audit_logs",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "audit_logs",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "StudioCapability",
                table: "audit_logs",
                newName: "studio_capability");

            migrationBuilder.RenameColumn(
                name: "ResourceType",
                table: "audit_logs",
                newName: "resource_type");

            migrationBuilder.RenameColumn(
                name: "ResourceId",
                table: "audit_logs",
                newName: "resource_id");

            migrationBuilder.RenameColumn(
                name: "EffectiveScopes",
                table: "audit_logs",
                newName: "effective_scopes");

            migrationBuilder.RenameColumn(
                name: "CorrelationId",
                table: "audit_logs",
                newName: "correlation_id");

            migrationBuilder.RenameColumn(
                name: "ActorType",
                table: "audit_logs",
                newName: "actor_type");

            migrationBuilder.RenameColumn(
                name: "ActorId",
                table: "audit_logs",
                newName: "actor_id");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_TenantId_Timestamp",
                table: "audit_logs",
                newName: "ix_audit_logs_tenant_id_timestamp");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_TenantId_ResourceType_ResourceId",
                table: "audit_logs",
                newName: "ix_audit_logs_tenant_id_resource_type_resource_id");

            migrationBuilder.RenameIndex(
                name: "IX_AuditLogs_TenantId_Action_Timestamp",
                table: "audit_logs",
                newName: "ix_audit_logs_tenant_id_action_timestamp");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "asp_net_user_tokens",
                newName: "value");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "asp_net_user_tokens",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "LoginProvider",
                table: "asp_net_user_tokens",
                newName: "login_provider");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "asp_net_user_tokens",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "asp_net_users",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "Provider",
                table: "asp_net_users",
                newName: "provider");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "asp_net_users",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "asp_net_users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserName",
                table: "asp_net_users",
                newName: "user_name");

            migrationBuilder.RenameColumn(
                name: "TwoFactorEnabled",
                table: "asp_net_users",
                newName: "two_factor_enabled");

            migrationBuilder.RenameColumn(
                name: "TenantId",
                table: "asp_net_users",
                newName: "tenant_id");

            migrationBuilder.RenameColumn(
                name: "SecurityStamp",
                table: "asp_net_users",
                newName: "security_stamp");

            migrationBuilder.RenameColumn(
                name: "PrincipalKey",
                table: "asp_net_users",
                newName: "principal_key");

            migrationBuilder.RenameColumn(
                name: "PhoneNumberConfirmed",
                table: "asp_net_users",
                newName: "phone_number_confirmed");

            migrationBuilder.RenameColumn(
                name: "PhoneNumber",
                table: "asp_net_users",
                newName: "phone_number");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "asp_net_users",
                newName: "password_hash");

            migrationBuilder.RenameColumn(
                name: "NormalizedUserName",
                table: "asp_net_users",
                newName: "normalized_user_name");

            migrationBuilder.RenameColumn(
                name: "NormalizedEmail",
                table: "asp_net_users",
                newName: "normalized_email");

            migrationBuilder.RenameColumn(
                name: "MustChangePassword",
                table: "asp_net_users",
                newName: "must_change_password");

            migrationBuilder.RenameColumn(
                name: "MiddleInitial",
                table: "asp_net_users",
                newName: "middle_initial");

            migrationBuilder.RenameColumn(
                name: "LockoutEnd",
                table: "asp_net_users",
                newName: "lockout_end");

            migrationBuilder.RenameColumn(
                name: "LockoutEnabled",
                table: "asp_net_users",
                newName: "lockout_enabled");

            migrationBuilder.RenameColumn(
                name: "LastName",
                table: "asp_net_users",
                newName: "last_name");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "asp_net_users",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "FirstName",
                table: "asp_net_users",
                newName: "first_name");

            migrationBuilder.RenameColumn(
                name: "ExternalSubject",
                table: "asp_net_users",
                newName: "external_subject");

            migrationBuilder.RenameColumn(
                name: "ExternalIssuer",
                table: "asp_net_users",
                newName: "external_issuer");

            migrationBuilder.RenameColumn(
                name: "EmailConfirmed",
                table: "asp_net_users",
                newName: "email_confirmed");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "asp_net_users",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyStamp",
                table: "asp_net_users",
                newName: "concurrency_stamp");

            migrationBuilder.RenameColumn(
                name: "AccessFailedCount",
                table: "asp_net_users",
                newName: "access_failed_count");

            migrationBuilder.RenameIndex(
                name: "UserNameIndex",
                table: "asp_net_users",
                newName: "user_name_index");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_TenantId_Provider_ExternalIssuer_ExternalSubject",
                table: "asp_net_users",
                newName: "ix_asp_net_users_tenant_id_provider_external_issuer_external_subject");

            migrationBuilder.RenameIndex(
                name: "EmailIndex",
                table: "asp_net_users",
                newName: "email_index");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                table: "asp_net_user_roles",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "asp_net_user_roles",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "asp_net_user_roles",
                newName: "ix_asp_net_user_roles_role_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "asp_net_user_logins",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "ProviderDisplayName",
                table: "asp_net_user_logins",
                newName: "provider_display_name");

            migrationBuilder.RenameColumn(
                name: "ProviderKey",
                table: "asp_net_user_logins",
                newName: "provider_key");

            migrationBuilder.RenameColumn(
                name: "LoginProvider",
                table: "asp_net_user_logins",
                newName: "login_provider");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "asp_net_user_logins",
                newName: "ix_asp_net_user_logins_user_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "asp_net_user_claims",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "asp_net_user_claims",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "ClaimValue",
                table: "asp_net_user_claims",
                newName: "claim_value");

            migrationBuilder.RenameColumn(
                name: "ClaimType",
                table: "asp_net_user_claims",
                newName: "claim_type");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "asp_net_user_claims",
                newName: "ix_asp_net_user_claims_user_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "asp_net_roles",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "asp_net_roles",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "NormalizedName",
                table: "asp_net_roles",
                newName: "normalized_name");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyStamp",
                table: "asp_net_roles",
                newName: "concurrency_stamp");

            migrationBuilder.RenameIndex(
                name: "RoleNameIndex",
                table: "asp_net_roles",
                newName: "role_name_index");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "asp_net_role_claims",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                table: "asp_net_role_claims",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "ClaimValue",
                table: "asp_net_role_claims",
                newName: "claim_value");

            migrationBuilder.RenameColumn(
                name: "ClaimType",
                table: "asp_net_role_claims",
                newName: "claim_type");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "asp_net_role_claims",
                newName: "ix_asp_net_role_claims_role_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "alert_notifications",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "OrchestratorAlias",
                table: "alert_notifications",
                newName: "orchestrator_alias");

            migrationBuilder.RenameColumn(
                name: "NotificationName",
                table: "alert_notifications",
                newName: "notification_name");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "alert_notifications",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "AlertId",
                table: "alert_notifications",
                newName: "alert_id");

            migrationBuilder.RenameIndex(
                name: "IX_AlertNotifications_AlertId_OrchestratorAlias_NotificationName",
                table: "alert_notifications",
                newName: "ix_alert_notifications_alert_id_orchestrator_alias_notification_name");

            migrationBuilder.RenameColumn(
                name: "Outcome",
                table: "admin_service_runs",
                newName: "outcome");

            migrationBuilder.RenameColumn(
                name: "Detail",
                table: "admin_service_runs",
                newName: "detail");

            migrationBuilder.RenameColumn(
                name: "Attempts",
                table: "admin_service_runs",
                newName: "attempts");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "admin_service_runs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "StartedAtUtc",
                table: "admin_service_runs",
                newName: "started_at_utc");

            migrationBuilder.RenameColumn(
                name: "ServiceName",
                table: "admin_service_runs",
                newName: "service_name");

            migrationBuilder.RenameColumn(
                name: "NodeName",
                table: "admin_service_runs",
                newName: "node_name");

            migrationBuilder.RenameColumn(
                name: "CompletedAtUtc",
                table: "admin_service_runs",
                newName: "completed_at_utc");

            migrationBuilder.RenameIndex(
                name: "IX_AdminServiceRuns_ServiceName_StartedAtUtc",
                table: "admin_service_runs",
                newName: "ix_admin_service_runs_service_name_started_at_utc");

            migrationBuilder.AddPrimaryKey(
                name: "pk_subscriptions",
                table: "subscriptions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_reports",
                table: "reports",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_groups",
                table: "groups",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_folders",
                table: "folders",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_datasets",
                table: "datasets",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_workload_identity_replays",
                table: "workload_identity_replays",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_groups",
                table: "user_groups",
                columns: new[] { "user_id", "group_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_subscription_deliveries",
                table: "subscription_deliveries",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_studio_recovery_drafts",
                table: "studio_recovery_drafts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_settings",
                table: "stewardship_settings",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_scans",
                table: "stewardship_scans",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_resolution_categories",
                table: "stewardship_resolution_categories",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_glossary_terms",
                table: "stewardship_glossary_terms",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_findings",
                table: "stewardship_findings",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_finding_decisions",
                table: "stewardship_finding_decisions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_asset_reviews",
                table: "stewardship_asset_reviews",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_stewardship_asset_badges",
                table: "stewardship_asset_badges",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_shared_tenant_resources",
                table: "shared_tenant_resources",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_shared_tenant_lifecycles",
                table: "shared_tenant_lifecycles",
                column: "tenant_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_shared_tenant_lifecycle_operations",
                table: "shared_tenant_lifecycle_operations",
                column: "operation_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_shared_identity_authorities",
                table: "shared_identity_authorities",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_shared_connection_usages",
                table: "shared_connection_usages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_shared_connection_acls",
                table: "shared_connection_acls",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_service_accounts",
                table: "service_accounts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_saved_report_views",
                table: "saved_report_views",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_snapshots",
                table: "report_snapshots",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_share_links",
                table: "report_share_links",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_script_drafts",
                table: "report_script_drafts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_script_draft_decisions",
                table: "report_script_draft_decisions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_job_links",
                table: "report_job_links",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_favorites",
                table: "report_favorites",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_embed_tokens",
                table: "report_embed_tokens",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_alerts",
                table: "report_alerts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_acls",
                table: "report_acls",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_report_access_requests",
                table: "report_access_requests",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_refresh_tokens",
                table: "refresh_tokens",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_portal_shared_connections",
                table: "portal_shared_connections",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_portal_secrets",
                table: "portal_secrets",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_portal_execution_jobs",
                table: "portal_execution_jobs",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_policy_versions",
                table: "policy_versions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_policy_machines",
                table: "policy_machines",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_group_studio_capabilities",
                table: "group_studio_capabilities",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_gateway_enrollments",
                table: "gateway_enrollments",
                column: "enrollment_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_gateway_ambiguous_write_events",
                table: "gateway_ambiguous_write_events",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_gateway_ambiguous_write_cases",
                table: "gateway_ambiguous_write_cases",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_folder_acls",
                table: "folder_acls",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_dataset_user_acls",
                table: "dataset_user_acls",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_dataset_acls",
                table: "dataset_acls",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_audit_outbox_messages",
                table: "audit_outbox_messages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_audit_logs",
                table: "audit_logs",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_user_tokens",
                table: "asp_net_user_tokens",
                columns: new[] { "user_id", "login_provider", "name" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_users",
                table: "asp_net_users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_user_roles",
                table: "asp_net_user_roles",
                columns: new[] { "user_id", "role_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_user_logins",
                table: "asp_net_user_logins",
                columns: new[] { "login_provider", "provider_key" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_user_claims",
                table: "asp_net_user_claims",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_roles",
                table: "asp_net_roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_asp_net_role_claims",
                table: "asp_net_role_claims",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_alert_notifications",
                table: "alert_notifications",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_admin_service_runs",
                table: "admin_service_runs",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_principal_key",
                table: "groups",
                column: "principal_key",
                unique: true,
                filter: "\"principal_key\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_report_acls_report_id_group_id",
                table: "report_acls",
                columns: new[] { "report_id", "group_id" },
                unique: true,
                filter: "\"group_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_report_acls_report_id_user_id",
                table: "report_acls",
                columns: new[] { "report_id", "user_id" },
                unique: true,
                filter: "\"user_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_report_access_requests_requester_user_id_report_id",
                table: "report_access_requests",
                columns: new[] { "requester_user_id", "report_id" },
                unique: true,
                filter: "\"status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_portal_execution_jobs_tenant_id_report_id_kind",
                table: "portal_execution_jobs",
                columns: new[] { "tenant_id", "report_id", "kind" },
                unique: true,
                filter: "\"kind\" = 'Refresh' AND \"status\" IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_principal_key",
                table: "asp_net_users",
                column: "principal_key",
                unique: true,
                filter: "\"principal_key\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_tenant_id_normalized_user_name",
                table: "asp_net_users",
                columns: new[] { "tenant_id", "normalized_user_name" },
                unique: true,
                filter: "\"normalized_user_name\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_alert_notifications_report_alerts_alert_id",
                table: "alert_notifications",
                column: "alert_id",
                principalTable: "report_alerts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                table: "asp_net_role_claims",
                column: "role_id",
                principalTable: "asp_net_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_user_claims_asp_net_users_user_id",
                table: "asp_net_user_claims",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_user_logins_asp_net_users_user_id",
                table: "asp_net_user_logins",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                table: "asp_net_user_roles",
                column: "role_id",
                principalTable: "asp_net_roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_user_roles_asp_net_users_user_id",
                table: "asp_net_user_roles",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                table: "asp_net_user_tokens",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_outbox_messages_audit_logs_audit_log_id",
                table: "audit_outbox_messages",
                column: "audit_log_id",
                principalTable: "audit_logs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_dataset_acls_datasets_dataset_id",
                table: "dataset_acls",
                column: "dataset_id",
                principalTable: "datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_dataset_acls_groups_group_id",
                table: "dataset_acls",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_dataset_user_acls_asp_net_users_user_id",
                table: "dataset_user_acls",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_dataset_user_acls_datasets_dataset_id",
                table: "dataset_user_acls",
                column: "dataset_id",
                principalTable: "datasets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_datasets_folders_folder_id",
                table: "datasets",
                column: "folder_id",
                principalTable: "folders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_datasets_reports_owning_report_id",
                table: "datasets",
                column: "owning_report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_folder_acls_folders_folder_id",
                table: "folder_acls",
                column: "folder_id",
                principalTable: "folders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_folder_acls_groups_group_id",
                table: "folder_acls",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_folders_folders_parent_id",
                table: "folders",
                column: "parent_id",
                principalTable: "folders",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_gateway_ambiguous_write_events_gateway_ambiguous_write_cases_case_id",
                table: "gateway_ambiguous_write_events",
                column: "case_id",
                principalTable: "gateway_ambiguous_write_cases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_group_studio_capabilities_groups_group_id",
                table: "group_studio_capabilities",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_refresh_tokens_asp_net_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_access_requests_asp_net_users_decided_by_user_id",
                table: "report_access_requests",
                column: "decided_by_user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_report_access_requests_asp_net_users_requester_user_id",
                table: "report_access_requests",
                column: "requester_user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_access_requests_reports_report_id",
                table: "report_access_requests",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_acls_asp_net_users_user_id",
                table: "report_acls",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_acls_groups_group_id",
                table: "report_acls",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_acls_reports_report_id",
                table: "report_acls",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_alerts_asp_net_users_owner_id",
                table: "report_alerts",
                column: "owner_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_alerts_reports_report_id",
                table: "report_alerts",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_embed_tokens_asp_net_users_created_by",
                table: "report_embed_tokens",
                column: "created_by",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_embed_tokens_reports_report_id",
                table: "report_embed_tokens",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_favorites_asp_net_users_user_id",
                table: "report_favorites",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_favorites_reports_report_id",
                table: "report_favorites",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_job_links_reports_report_id",
                table: "report_job_links",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_script_draft_decisions_report_script_drafts_draft_id",
                table: "report_script_draft_decisions",
                column: "draft_id",
                principalTable: "report_script_drafts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_script_drafts_reports_report_id",
                table: "report_script_drafts",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_share_links_asp_net_users_created_by",
                table: "report_share_links",
                column: "created_by",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_share_links_reports_report_id",
                table: "report_share_links",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_report_snapshots_reports_report_id",
                table: "report_snapshots",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_reports_folders_folder_id",
                table: "reports",
                column: "folder_id",
                principalTable: "folders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_saved_report_views_asp_net_users_user_id",
                table: "saved_report_views",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_saved_report_views_reports_report_id",
                table: "saved_report_views",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_service_accounts_asp_net_users_owner_user_id",
                table: "service_accounts",
                column: "owner_user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_shared_connection_acls_groups_group_id",
                table: "shared_connection_acls",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_shared_connection_acls_portal_shared_connections_shared_connection_id",
                table: "shared_connection_acls",
                column: "shared_connection_id",
                principalTable: "portal_shared_connections",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_shared_connection_usages_portal_shared_connections_shared_connection_id",
                table: "shared_connection_usages",
                column: "shared_connection_id",
                principalTable: "portal_shared_connections",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_stewardship_finding_decisions_stewardship_findings_finding_id",
                table: "stewardship_finding_decisions",
                column: "finding_id",
                principalTable: "stewardship_findings",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_studio_recovery_drafts_asp_net_users_user_id",
                table: "studio_recovery_drafts",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_studio_recovery_drafts_reports_report_id",
                table: "studio_recovery_drafts",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_subscriptions_asp_net_users_user_id",
                table: "subscriptions",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_subscriptions_reports_report_id",
                table: "subscriptions",
                column: "report_id",
                principalTable: "reports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_groups_asp_net_users_user_id",
                table: "user_groups",
                column: "user_id",
                principalTable: "asp_net_users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_groups_groups_group_id",
                table: "user_groups",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_alert_notifications_report_alerts_alert_id",
                table: "alert_notifications");

            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                table: "asp_net_role_claims");

            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_user_claims_asp_net_users_user_id",
                table: "asp_net_user_claims");

            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_user_logins_asp_net_users_user_id",
                table: "asp_net_user_logins");

            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                table: "asp_net_user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_user_roles_asp_net_users_user_id",
                table: "asp_net_user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                table: "asp_net_user_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_audit_outbox_messages_audit_logs_audit_log_id",
                table: "audit_outbox_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_dataset_acls_datasets_dataset_id",
                table: "dataset_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_dataset_acls_groups_group_id",
                table: "dataset_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_dataset_user_acls_asp_net_users_user_id",
                table: "dataset_user_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_dataset_user_acls_datasets_dataset_id",
                table: "dataset_user_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_datasets_folders_folder_id",
                table: "datasets");

            migrationBuilder.DropForeignKey(
                name: "fk_datasets_reports_owning_report_id",
                table: "datasets");

            migrationBuilder.DropForeignKey(
                name: "fk_folder_acls_folders_folder_id",
                table: "folder_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_folder_acls_groups_group_id",
                table: "folder_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_folders_folders_parent_id",
                table: "folders");

            migrationBuilder.DropForeignKey(
                name: "fk_gateway_ambiguous_write_events_gateway_ambiguous_write_cases_case_id",
                table: "gateway_ambiguous_write_events");

            migrationBuilder.DropForeignKey(
                name: "fk_group_studio_capabilities_groups_group_id",
                table: "group_studio_capabilities");

            migrationBuilder.DropForeignKey(
                name: "fk_refresh_tokens_asp_net_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_report_access_requests_asp_net_users_decided_by_user_id",
                table: "report_access_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_report_access_requests_asp_net_users_requester_user_id",
                table: "report_access_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_report_access_requests_reports_report_id",
                table: "report_access_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_report_acls_asp_net_users_user_id",
                table: "report_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_report_acls_groups_group_id",
                table: "report_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_report_acls_reports_report_id",
                table: "report_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_report_alerts_asp_net_users_owner_id",
                table: "report_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_report_alerts_reports_report_id",
                table: "report_alerts");

            migrationBuilder.DropForeignKey(
                name: "fk_report_embed_tokens_asp_net_users_created_by",
                table: "report_embed_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_report_embed_tokens_reports_report_id",
                table: "report_embed_tokens");

            migrationBuilder.DropForeignKey(
                name: "fk_report_favorites_asp_net_users_user_id",
                table: "report_favorites");

            migrationBuilder.DropForeignKey(
                name: "fk_report_favorites_reports_report_id",
                table: "report_favorites");

            migrationBuilder.DropForeignKey(
                name: "fk_report_job_links_reports_report_id",
                table: "report_job_links");

            migrationBuilder.DropForeignKey(
                name: "fk_report_script_draft_decisions_report_script_drafts_draft_id",
                table: "report_script_draft_decisions");

            migrationBuilder.DropForeignKey(
                name: "fk_report_script_drafts_reports_report_id",
                table: "report_script_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_report_share_links_asp_net_users_created_by",
                table: "report_share_links");

            migrationBuilder.DropForeignKey(
                name: "fk_report_share_links_reports_report_id",
                table: "report_share_links");

            migrationBuilder.DropForeignKey(
                name: "fk_report_snapshots_reports_report_id",
                table: "report_snapshots");

            migrationBuilder.DropForeignKey(
                name: "fk_reports_folders_folder_id",
                table: "reports");

            migrationBuilder.DropForeignKey(
                name: "fk_saved_report_views_asp_net_users_user_id",
                table: "saved_report_views");

            migrationBuilder.DropForeignKey(
                name: "fk_saved_report_views_reports_report_id",
                table: "saved_report_views");

            migrationBuilder.DropForeignKey(
                name: "fk_service_accounts_asp_net_users_owner_user_id",
                table: "service_accounts");

            migrationBuilder.DropForeignKey(
                name: "fk_shared_connection_acls_groups_group_id",
                table: "shared_connection_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_shared_connection_acls_portal_shared_connections_shared_connection_id",
                table: "shared_connection_acls");

            migrationBuilder.DropForeignKey(
                name: "fk_shared_connection_usages_portal_shared_connections_shared_connection_id",
                table: "shared_connection_usages");

            migrationBuilder.DropForeignKey(
                name: "fk_stewardship_finding_decisions_stewardship_findings_finding_id",
                table: "stewardship_finding_decisions");

            migrationBuilder.DropForeignKey(
                name: "fk_studio_recovery_drafts_asp_net_users_user_id",
                table: "studio_recovery_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_studio_recovery_drafts_reports_report_id",
                table: "studio_recovery_drafts");

            migrationBuilder.DropForeignKey(
                name: "fk_subscriptions_asp_net_users_user_id",
                table: "subscriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_subscriptions_reports_report_id",
                table: "subscriptions");

            migrationBuilder.DropForeignKey(
                name: "fk_user_groups_asp_net_users_user_id",
                table: "user_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_user_groups_groups_group_id",
                table: "user_groups");

            migrationBuilder.DropPrimaryKey(
                name: "pk_subscriptions",
                table: "subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_reports",
                table: "reports");

            migrationBuilder.DropPrimaryKey(
                name: "pk_groups",
                table: "groups");

            migrationBuilder.DropIndex(
                name: "ix_groups_principal_key",
                table: "groups");

            migrationBuilder.DropPrimaryKey(
                name: "pk_folders",
                table: "folders");

            migrationBuilder.DropPrimaryKey(
                name: "pk_datasets",
                table: "datasets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_workload_identity_replays",
                table: "workload_identity_replays");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_groups",
                table: "user_groups");

            migrationBuilder.DropPrimaryKey(
                name: "pk_subscription_deliveries",
                table: "subscription_deliveries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_studio_recovery_drafts",
                table: "studio_recovery_drafts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_settings",
                table: "stewardship_settings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_scans",
                table: "stewardship_scans");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_resolution_categories",
                table: "stewardship_resolution_categories");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_glossary_terms",
                table: "stewardship_glossary_terms");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_findings",
                table: "stewardship_findings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_finding_decisions",
                table: "stewardship_finding_decisions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_asset_reviews",
                table: "stewardship_asset_reviews");

            migrationBuilder.DropPrimaryKey(
                name: "pk_stewardship_asset_badges",
                table: "stewardship_asset_badges");

            migrationBuilder.DropPrimaryKey(
                name: "pk_shared_tenant_resources",
                table: "shared_tenant_resources");

            migrationBuilder.DropPrimaryKey(
                name: "pk_shared_tenant_lifecycles",
                table: "shared_tenant_lifecycles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_shared_tenant_lifecycle_operations",
                table: "shared_tenant_lifecycle_operations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_shared_identity_authorities",
                table: "shared_identity_authorities");

            migrationBuilder.DropPrimaryKey(
                name: "pk_shared_connection_usages",
                table: "shared_connection_usages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_shared_connection_acls",
                table: "shared_connection_acls");

            migrationBuilder.DropPrimaryKey(
                name: "pk_service_accounts",
                table: "service_accounts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_saved_report_views",
                table: "saved_report_views");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_snapshots",
                table: "report_snapshots");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_share_links",
                table: "report_share_links");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_script_drafts",
                table: "report_script_drafts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_script_draft_decisions",
                table: "report_script_draft_decisions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_job_links",
                table: "report_job_links");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_favorites",
                table: "report_favorites");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_embed_tokens",
                table: "report_embed_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_alerts",
                table: "report_alerts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_acls",
                table: "report_acls");

            migrationBuilder.DropIndex(
                name: "ix_report_acls_report_id_group_id",
                table: "report_acls");

            migrationBuilder.DropIndex(
                name: "ix_report_acls_report_id_user_id",
                table: "report_acls");

            migrationBuilder.DropPrimaryKey(
                name: "pk_report_access_requests",
                table: "report_access_requests");

            migrationBuilder.DropIndex(
                name: "ix_report_access_requests_requester_user_id_report_id",
                table: "report_access_requests");

            migrationBuilder.DropPrimaryKey(
                name: "pk_refresh_tokens",
                table: "refresh_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "pk_portal_shared_connections",
                table: "portal_shared_connections");

            migrationBuilder.DropPrimaryKey(
                name: "pk_portal_secrets",
                table: "portal_secrets");

            migrationBuilder.DropPrimaryKey(
                name: "pk_portal_execution_jobs",
                table: "portal_execution_jobs");

            migrationBuilder.DropIndex(
                name: "ix_portal_execution_jobs_tenant_id_report_id_kind",
                table: "portal_execution_jobs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_policy_versions",
                table: "policy_versions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_policy_machines",
                table: "policy_machines");

            migrationBuilder.DropPrimaryKey(
                name: "pk_group_studio_capabilities",
                table: "group_studio_capabilities");

            migrationBuilder.DropPrimaryKey(
                name: "pk_gateway_enrollments",
                table: "gateway_enrollments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_gateway_ambiguous_write_events",
                table: "gateway_ambiguous_write_events");

            migrationBuilder.DropPrimaryKey(
                name: "pk_gateway_ambiguous_write_cases",
                table: "gateway_ambiguous_write_cases");

            migrationBuilder.DropPrimaryKey(
                name: "pk_folder_acls",
                table: "folder_acls");

            migrationBuilder.DropPrimaryKey(
                name: "pk_dataset_user_acls",
                table: "dataset_user_acls");

            migrationBuilder.DropPrimaryKey(
                name: "pk_dataset_acls",
                table: "dataset_acls");

            migrationBuilder.DropPrimaryKey(
                name: "pk_audit_outbox_messages",
                table: "audit_outbox_messages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_audit_logs",
                table: "audit_logs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_users",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "ix_asp_net_users_principal_key",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "ix_asp_net_users_tenant_id_normalized_user_name",
                table: "asp_net_users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_user_tokens",
                table: "asp_net_user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_user_roles",
                table: "asp_net_user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_user_logins",
                table: "asp_net_user_logins");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_user_claims",
                table: "asp_net_user_claims");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_roles",
                table: "asp_net_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_asp_net_role_claims",
                table: "asp_net_role_claims");

            migrationBuilder.DropPrimaryKey(
                name: "pk_alert_notifications",
                table: "alert_notifications");

            migrationBuilder.DropPrimaryKey(
                name: "pk_admin_service_runs",
                table: "admin_service_runs");

            migrationBuilder.RenameTable(
                name: "subscriptions",
                newName: "_tmp_Subscriptions");

            migrationBuilder.RenameTable(
                name: "_tmp_Subscriptions",
                newName: "Subscriptions");

            migrationBuilder.RenameTable(
                name: "reports",
                newName: "_tmp_Reports");

            migrationBuilder.RenameTable(
                name: "_tmp_Reports",
                newName: "Reports");

            migrationBuilder.RenameTable(
                name: "groups",
                newName: "_tmp_Groups");

            migrationBuilder.RenameTable(
                name: "_tmp_Groups",
                newName: "Groups");

            migrationBuilder.RenameTable(
                name: "folders",
                newName: "_tmp_Folders");

            migrationBuilder.RenameTable(
                name: "_tmp_Folders",
                newName: "Folders");

            migrationBuilder.RenameTable(
                name: "datasets",
                newName: "_tmp_Datasets");

            migrationBuilder.RenameTable(
                name: "_tmp_Datasets",
                newName: "Datasets");

            migrationBuilder.RenameTable(
                name: "workload_identity_replays",
                newName: "_tmp_WorkloadIdentityReplays");

            migrationBuilder.RenameTable(
                name: "_tmp_WorkloadIdentityReplays",
                newName: "WorkloadIdentityReplays");

            migrationBuilder.RenameTable(
                name: "user_groups",
                newName: "_tmp_UserGroups");

            migrationBuilder.RenameTable(
                name: "_tmp_UserGroups",
                newName: "UserGroups");

            migrationBuilder.RenameTable(
                name: "subscription_deliveries",
                newName: "_tmp_SubscriptionDeliveries");

            migrationBuilder.RenameTable(
                name: "_tmp_SubscriptionDeliveries",
                newName: "SubscriptionDeliveries");

            migrationBuilder.RenameTable(
                name: "studio_recovery_drafts",
                newName: "_tmp_StudioRecoveryDrafts");

            migrationBuilder.RenameTable(
                name: "_tmp_StudioRecoveryDrafts",
                newName: "StudioRecoveryDrafts");

            migrationBuilder.RenameTable(
                name: "stewardship_settings",
                newName: "_tmp_StewardshipSettings");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipSettings",
                newName: "StewardshipSettings");

            migrationBuilder.RenameTable(
                name: "stewardship_scans",
                newName: "_tmp_StewardshipScans");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipScans",
                newName: "StewardshipScans");

            migrationBuilder.RenameTable(
                name: "stewardship_resolution_categories",
                newName: "_tmp_StewardshipResolutionCategories");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipResolutionCategories",
                newName: "StewardshipResolutionCategories");

            migrationBuilder.RenameTable(
                name: "stewardship_glossary_terms",
                newName: "_tmp_StewardshipGlossaryTerms");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipGlossaryTerms",
                newName: "StewardshipGlossaryTerms");

            migrationBuilder.RenameTable(
                name: "stewardship_findings",
                newName: "_tmp_StewardshipFindings");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipFindings",
                newName: "StewardshipFindings");

            migrationBuilder.RenameTable(
                name: "stewardship_finding_decisions",
                newName: "_tmp_StewardshipFindingDecisions");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipFindingDecisions",
                newName: "StewardshipFindingDecisions");

            migrationBuilder.RenameTable(
                name: "stewardship_asset_reviews",
                newName: "_tmp_StewardshipAssetReviews");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipAssetReviews",
                newName: "StewardshipAssetReviews");

            migrationBuilder.RenameTable(
                name: "stewardship_asset_badges",
                newName: "_tmp_StewardshipAssetBadges");

            migrationBuilder.RenameTable(
                name: "_tmp_StewardshipAssetBadges",
                newName: "StewardshipAssetBadges");

            migrationBuilder.RenameTable(
                name: "shared_tenant_resources",
                newName: "_tmp_SharedTenantResources");

            migrationBuilder.RenameTable(
                name: "_tmp_SharedTenantResources",
                newName: "SharedTenantResources");

            migrationBuilder.RenameTable(
                name: "shared_tenant_lifecycles",
                newName: "_tmp_SharedTenantLifecycles");

            migrationBuilder.RenameTable(
                name: "_tmp_SharedTenantLifecycles",
                newName: "SharedTenantLifecycles");

            migrationBuilder.RenameTable(
                name: "shared_tenant_lifecycle_operations",
                newName: "_tmp_SharedTenantLifecycleOperations");

            migrationBuilder.RenameTable(
                name: "_tmp_SharedTenantLifecycleOperations",
                newName: "SharedTenantLifecycleOperations");

            migrationBuilder.RenameTable(
                name: "shared_identity_authorities",
                newName: "_tmp_SharedIdentityAuthorities");

            migrationBuilder.RenameTable(
                name: "_tmp_SharedIdentityAuthorities",
                newName: "SharedIdentityAuthorities");

            migrationBuilder.RenameTable(
                name: "shared_connection_usages",
                newName: "_tmp_SharedConnectionUsages");

            migrationBuilder.RenameTable(
                name: "_tmp_SharedConnectionUsages",
                newName: "SharedConnectionUsages");

            migrationBuilder.RenameTable(
                name: "shared_connection_acls",
                newName: "_tmp_SharedConnectionAcls");

            migrationBuilder.RenameTable(
                name: "_tmp_SharedConnectionAcls",
                newName: "SharedConnectionAcls");

            migrationBuilder.RenameTable(
                name: "service_accounts",
                newName: "_tmp_ServiceAccounts");

            migrationBuilder.RenameTable(
                name: "_tmp_ServiceAccounts",
                newName: "ServiceAccounts");

            migrationBuilder.RenameTable(
                name: "saved_report_views",
                newName: "_tmp_SavedReportViews");

            migrationBuilder.RenameTable(
                name: "_tmp_SavedReportViews",
                newName: "SavedReportViews");

            migrationBuilder.RenameTable(
                name: "report_snapshots",
                newName: "_tmp_ReportSnapshots");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportSnapshots",
                newName: "ReportSnapshots");

            migrationBuilder.RenameTable(
                name: "report_share_links",
                newName: "_tmp_ReportShareLinks");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportShareLinks",
                newName: "ReportShareLinks");

            migrationBuilder.RenameTable(
                name: "report_script_drafts",
                newName: "_tmp_ReportScriptDrafts");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportScriptDrafts",
                newName: "ReportScriptDrafts");

            migrationBuilder.RenameTable(
                name: "report_script_draft_decisions",
                newName: "_tmp_ReportScriptDraftDecisions");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportScriptDraftDecisions",
                newName: "ReportScriptDraftDecisions");

            migrationBuilder.RenameTable(
                name: "report_job_links",
                newName: "_tmp_ReportJobLinks");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportJobLinks",
                newName: "ReportJobLinks");

            migrationBuilder.RenameTable(
                name: "report_favorites",
                newName: "_tmp_ReportFavorites");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportFavorites",
                newName: "ReportFavorites");

            migrationBuilder.RenameTable(
                name: "report_embed_tokens",
                newName: "_tmp_ReportEmbedTokens");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportEmbedTokens",
                newName: "ReportEmbedTokens");

            migrationBuilder.RenameTable(
                name: "report_alerts",
                newName: "_tmp_ReportAlerts");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportAlerts",
                newName: "ReportAlerts");

            migrationBuilder.RenameTable(
                name: "report_acls",
                newName: "_tmp_ReportAcls");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportAcls",
                newName: "ReportAcls");

            migrationBuilder.RenameTable(
                name: "report_access_requests",
                newName: "_tmp_ReportAccessRequests");

            migrationBuilder.RenameTable(
                name: "_tmp_ReportAccessRequests",
                newName: "ReportAccessRequests");

            migrationBuilder.RenameTable(
                name: "refresh_tokens",
                newName: "_tmp_RefreshTokens");

            migrationBuilder.RenameTable(
                name: "_tmp_RefreshTokens",
                newName: "RefreshTokens");

            migrationBuilder.RenameTable(
                name: "portal_shared_connections",
                newName: "_tmp_PortalSharedConnections");

            migrationBuilder.RenameTable(
                name: "_tmp_PortalSharedConnections",
                newName: "PortalSharedConnections");

            migrationBuilder.RenameTable(
                name: "portal_secrets",
                newName: "_tmp_PortalSecrets");

            migrationBuilder.RenameTable(
                name: "_tmp_PortalSecrets",
                newName: "PortalSecrets");

            migrationBuilder.RenameTable(
                name: "portal_execution_jobs",
                newName: "_tmp_PortalExecutionJobs");

            migrationBuilder.RenameTable(
                name: "_tmp_PortalExecutionJobs",
                newName: "PortalExecutionJobs");

            migrationBuilder.RenameTable(
                name: "policy_versions",
                newName: "_tmp_PolicyVersions");

            migrationBuilder.RenameTable(
                name: "_tmp_PolicyVersions",
                newName: "PolicyVersions");

            migrationBuilder.RenameTable(
                name: "policy_machines",
                newName: "_tmp_PolicyMachines");

            migrationBuilder.RenameTable(
                name: "_tmp_PolicyMachines",
                newName: "PolicyMachines");

            migrationBuilder.RenameTable(
                name: "group_studio_capabilities",
                newName: "_tmp_GroupStudioCapabilities");

            migrationBuilder.RenameTable(
                name: "_tmp_GroupStudioCapabilities",
                newName: "GroupStudioCapabilities");

            migrationBuilder.RenameTable(
                name: "gateway_enrollments",
                newName: "_tmp_GatewayEnrollments");

            migrationBuilder.RenameTable(
                name: "_tmp_GatewayEnrollments",
                newName: "GatewayEnrollments");

            migrationBuilder.RenameTable(
                name: "gateway_ambiguous_write_events",
                newName: "_tmp_GatewayAmbiguousWriteEvents");

            migrationBuilder.RenameTable(
                name: "_tmp_GatewayAmbiguousWriteEvents",
                newName: "GatewayAmbiguousWriteEvents");

            migrationBuilder.RenameTable(
                name: "gateway_ambiguous_write_cases",
                newName: "_tmp_GatewayAmbiguousWriteCases");

            migrationBuilder.RenameTable(
                name: "_tmp_GatewayAmbiguousWriteCases",
                newName: "GatewayAmbiguousWriteCases");

            migrationBuilder.RenameTable(
                name: "folder_acls",
                newName: "_tmp_FolderAcls");

            migrationBuilder.RenameTable(
                name: "_tmp_FolderAcls",
                newName: "FolderAcls");

            migrationBuilder.RenameTable(
                name: "dataset_user_acls",
                newName: "_tmp_DatasetUserAcls");

            migrationBuilder.RenameTable(
                name: "_tmp_DatasetUserAcls",
                newName: "DatasetUserAcls");

            migrationBuilder.RenameTable(
                name: "dataset_acls",
                newName: "_tmp_DatasetAcls");

            migrationBuilder.RenameTable(
                name: "_tmp_DatasetAcls",
                newName: "DatasetAcls");

            migrationBuilder.RenameTable(
                name: "audit_outbox_messages",
                newName: "_tmp_AuditOutboxMessages");

            migrationBuilder.RenameTable(
                name: "_tmp_AuditOutboxMessages",
                newName: "AuditOutboxMessages");

            migrationBuilder.RenameTable(
                name: "audit_logs",
                newName: "_tmp_AuditLogs");

            migrationBuilder.RenameTable(
                name: "_tmp_AuditLogs",
                newName: "AuditLogs");

            migrationBuilder.RenameTable(
                name: "asp_net_users",
                newName: "_tmp_AspNetUsers");

            migrationBuilder.RenameTable(
                name: "_tmp_AspNetUsers",
                newName: "AspNetUsers");

            migrationBuilder.RenameTable(
                name: "asp_net_user_tokens",
                newName: "_tmp_AspNetUserTokens");

            migrationBuilder.RenameTable(
                name: "_tmp_AspNetUserTokens",
                newName: "AspNetUserTokens");

            migrationBuilder.RenameTable(
                name: "asp_net_user_roles",
                newName: "_tmp_AspNetUserRoles");

            migrationBuilder.RenameTable(
                name: "_tmp_AspNetUserRoles",
                newName: "AspNetUserRoles");

            migrationBuilder.RenameTable(
                name: "asp_net_user_logins",
                newName: "_tmp_AspNetUserLogins");

            migrationBuilder.RenameTable(
                name: "_tmp_AspNetUserLogins",
                newName: "AspNetUserLogins");

            migrationBuilder.RenameTable(
                name: "asp_net_user_claims",
                newName: "_tmp_AspNetUserClaims");

            migrationBuilder.RenameTable(
                name: "_tmp_AspNetUserClaims",
                newName: "AspNetUserClaims");

            migrationBuilder.RenameTable(
                name: "asp_net_roles",
                newName: "_tmp_AspNetRoles");

            migrationBuilder.RenameTable(
                name: "_tmp_AspNetRoles",
                newName: "AspNetRoles");

            migrationBuilder.RenameTable(
                name: "asp_net_role_claims",
                newName: "_tmp_AspNetRoleClaims");

            migrationBuilder.RenameTable(
                name: "_tmp_AspNetRoleClaims",
                newName: "AspNetRoleClaims");

            migrationBuilder.RenameTable(
                name: "alert_notifications",
                newName: "_tmp_AlertNotifications");

            migrationBuilder.RenameTable(
                name: "_tmp_AlertNotifications",
                newName: "AlertNotifications");

            migrationBuilder.RenameTable(
                name: "admin_service_runs",
                newName: "_tmp_AdminServiceRuns");

            migrationBuilder.RenameTable(
                name: "_tmp_AdminServiceRuns",
                newName: "AdminServiceRuns");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "Subscriptions",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "schedule",
                table: "Subscriptions",
                newName: "Schedule");

            migrationBuilder.RenameColumn(
                name: "recipients",
                table: "Subscriptions",
                newName: "Recipients");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Subscriptions",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "format",
                table: "Subscriptions",
                newName: "Format");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Subscriptions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "Subscriptions",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "smtp_alias",
                table: "Subscriptions",
                newName: "SmtpAlias");

            migrationBuilder.RenameColumn(
                name: "script_path",
                table: "Subscriptions",
                newName: "ScriptPath");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "Subscriptions",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "parameters_json",
                table: "Subscriptions",
                newName: "ParametersJson");

            migrationBuilder.RenameColumn(
                name: "next_run_at",
                table: "Subscriptions",
                newName: "NextRunAt");

            migrationBuilder.RenameColumn(
                name: "last_triggered_at",
                table: "Subscriptions",
                newName: "LastTriggeredAt");

            migrationBuilder.RenameColumn(
                name: "last_sent_at",
                table: "Subscriptions",
                newName: "LastSentAt");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "Subscriptions",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "fail_count",
                table: "Subscriptions",
                newName: "FailCount");

            migrationBuilder.RenameColumn(
                name: "deliver_on_refresh",
                table: "Subscriptions",
                newName: "DeliverOnRefresh");

            migrationBuilder.RenameColumn(
                name: "at_time",
                table: "Subscriptions",
                newName: "AtTime");

            migrationBuilder.RenameIndex(
                name: "ix_subscriptions_user_id",
                table: "Subscriptions",
                newName: "IX_Subscriptions_UserId");

            migrationBuilder.RenameIndex(
                name: "ix_subscriptions_report_id",
                table: "Subscriptions",
                newName: "IX_Subscriptions_ReportId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "Reports",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "tags",
                table: "Reports",
                newName: "Tags");

            migrationBuilder.RenameColumn(
                name: "steward",
                table: "Reports",
                newName: "Steward");

            migrationBuilder.RenameColumn(
                name: "owner",
                table: "Reports",
                newName: "Owner");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Reports",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "Reports",
                newName: "Kind");

            migrationBuilder.RenameColumn(
                name: "domain",
                table: "Reports",
                newName: "Domain");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Reports",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "contact",
                table: "Reports",
                newName: "Contact");

            migrationBuilder.RenameColumn(
                name: "certification",
                table: "Reports",
                newName: "Certification");

            migrationBuilder.RenameColumn(
                name: "category",
                table: "Reports",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Reports",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Reports",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "Reports",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "script_path",
                table: "Reports",
                newName: "ScriptPath");

            migrationBuilder.RenameColumn(
                name: "script_last_modified",
                table: "Reports",
                newName: "ScriptLastModified");

            migrationBuilder.RenameColumn(
                name: "published_script_hash",
                table: "Reports",
                newName: "PublishedScriptHash");

            migrationBuilder.RenameColumn(
                name: "metadata_json",
                table: "Reports",
                newName: "MetadataJson");

            migrationBuilder.RenameColumn(
                name: "last_viewed_at",
                table: "Reports",
                newName: "LastViewedAt");

            migrationBuilder.RenameColumn(
                name: "last_refresh_status",
                table: "Reports",
                newName: "LastRefreshStatus");

            migrationBuilder.RenameColumn(
                name: "last_refresh_started_at",
                table: "Reports",
                newName: "LastRefreshStartedAt");

            migrationBuilder.RenameColumn(
                name: "last_refresh_error",
                table: "Reports",
                newName: "LastRefreshError");

            migrationBuilder.RenameColumn(
                name: "last_refresh_duration_ms",
                table: "Reports",
                newName: "LastRefreshDurationMs");

            migrationBuilder.RenameColumn(
                name: "last_refresh_completed_at",
                table: "Reports",
                newName: "LastRefreshCompletedAt");

            migrationBuilder.RenameColumn(
                name: "is_deleted",
                table: "Reports",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "folder_id",
                table: "Reports",
                newName: "FolderId");

            migrationBuilder.RenameColumn(
                name: "edit_session_user_name",
                table: "Reports",
                newName: "EditSessionUserName");

            migrationBuilder.RenameColumn(
                name: "edit_session_user_id",
                table: "Reports",
                newName: "EditSessionUserId");

            migrationBuilder.RenameColumn(
                name: "edit_session_expires_at_utc",
                table: "Reports",
                newName: "EditSessionExpiresAtUtc");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "Reports",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Reports",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_reports_tenant_id_folder_id",
                table: "Reports",
                newName: "IX_Reports_TenantId_FolderId");

            migrationBuilder.RenameIndex(
                name: "ix_reports_folder_id",
                table: "Reports",
                newName: "IX_Reports_FolderId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "Groups",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "provider",
                table: "Groups",
                newName: "Provider");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Groups",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Groups",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Groups",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "Groups",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "principal_key",
                table: "Groups",
                newName: "PrincipalKey");

            migrationBuilder.RenameColumn(
                name: "ad_group",
                table: "Groups",
                newName: "AdGroup");

            migrationBuilder.RenameIndex(
                name: "ix_groups_tenant_id_name",
                table: "Groups",
                newName: "IX_Groups_TenantId_Name");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "Folders",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "path",
                table: "Folders",
                newName: "Path");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Folders",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Folders",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "Folders",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "parent_id",
                table: "Folders",
                newName: "ParentId");

            migrationBuilder.RenameColumn(
                name: "owner_id",
                table: "Folders",
                newName: "OwnerId");

            migrationBuilder.RenameIndex(
                name: "ix_folders_tenant_id_path",
                table: "Folders",
                newName: "IX_Folders_TenantId_Path");

            migrationBuilder.RenameIndex(
                name: "ix_folders_parent_id",
                table: "Folders",
                newName: "IX_Folders_ParentId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "Datasets",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "ttl",
                table: "Datasets",
                newName: "Ttl");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Datasets",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Datasets",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "Datasets",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "Datasets",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "source_query",
                table: "Datasets",
                newName: "SourceQuery");

            migrationBuilder.RenameColumn(
                name: "row_count",
                table: "Datasets",
                newName: "RowCount");

            migrationBuilder.RenameColumn(
                name: "refresh_interval",
                table: "Datasets",
                newName: "RefreshInterval");

            migrationBuilder.RenameColumn(
                name: "parquet_file_path",
                table: "Datasets",
                newName: "ParquetFilePath");

            migrationBuilder.RenameColumn(
                name: "owning_report_id",
                table: "Datasets",
                newName: "OwningReportId");

            migrationBuilder.RenameColumn(
                name: "last_refresh",
                table: "Datasets",
                newName: "LastRefresh");

            migrationBuilder.RenameColumn(
                name: "folder_path",
                table: "Datasets",
                newName: "FolderPath");

            migrationBuilder.RenameColumn(
                name: "folder_id",
                table: "Datasets",
                newName: "FolderId");

            migrationBuilder.RenameColumn(
                name: "encryption_mode",
                table: "Datasets",
                newName: "EncryptionMode");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "Datasets",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Datasets",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "column_schema",
                table: "Datasets",
                newName: "ColumnSchema");

            migrationBuilder.RenameColumn(
                name: "at_rest_key_version",
                table: "Datasets",
                newName: "AtRestKeyVersion");

            migrationBuilder.RenameColumn(
                name: "access_level",
                table: "Datasets",
                newName: "AccessLevel");

            migrationBuilder.RenameIndex(
                name: "ix_datasets_tenant_id_name",
                table: "Datasets",
                newName: "IX_Datasets_TenantId_Name");

            migrationBuilder.RenameIndex(
                name: "ix_datasets_owning_report_id",
                table: "Datasets",
                newName: "IX_Datasets_OwningReportId");

            migrationBuilder.RenameIndex(
                name: "ix_datasets_folder_id",
                table: "Datasets",
                newName: "IX_Datasets_FolderId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "WorkloadIdentityReplays",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "used_at",
                table: "WorkloadIdentityReplays",
                newName: "UsedAt");

            migrationBuilder.RenameColumn(
                name: "token_id_hash",
                table: "WorkloadIdentityReplays",
                newName: "TokenIdHash");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "WorkloadIdentityReplays",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "WorkloadIdentityReplays",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "binding_id",
                table: "WorkloadIdentityReplays",
                newName: "BindingId");

            migrationBuilder.RenameIndex(
                name: "ix_workload_identity_replays_tenant_id_binding_id_token_id_hash",
                table: "WorkloadIdentityReplays",
                newName: "IX_WorkloadIdentityReplays_TenantId_BindingId_TokenIdHash");

            migrationBuilder.RenameIndex(
                name: "ix_workload_identity_replays_expires_at",
                table: "WorkloadIdentityReplays",
                newName: "IX_WorkloadIdentityReplays_ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "UserGroups",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "UserGroups",
                newName: "GroupId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "UserGroups",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "ix_user_groups_tenant_id_user_id_group_id",
                table: "UserGroups",
                newName: "IX_UserGroups_TenantId_UserId_GroupId");

            migrationBuilder.RenameIndex(
                name: "ix_user_groups_group_id",
                table: "UserGroups",
                newName: "IX_UserGroups_GroupId");

            migrationBuilder.RenameColumn(
                name: "recipients",
                table: "SubscriptionDeliveries",
                newName: "Recipients");

            migrationBuilder.RenameColumn(
                name: "outcome",
                table: "SubscriptionDeliveries",
                newName: "Outcome");

            migrationBuilder.RenameColumn(
                name: "detail",
                table: "SubscriptionDeliveries",
                newName: "Detail");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SubscriptionDeliveries",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "trigger_key",
                table: "SubscriptionDeliveries",
                newName: "TriggerKey");

            migrationBuilder.RenameColumn(
                name: "subscription_id",
                table: "SubscriptionDeliveries",
                newName: "SubscriptionId");

            migrationBuilder.RenameColumn(
                name: "started_at",
                table: "SubscriptionDeliveries",
                newName: "StartedAt");

            migrationBuilder.RenameColumn(
                name: "recipient_key",
                table: "SubscriptionDeliveries",
                newName: "RecipientKey");

            migrationBuilder.RenameColumn(
                name: "delivery_id",
                table: "SubscriptionDeliveries",
                newName: "DeliveryId");

            migrationBuilder.RenameColumn(
                name: "completed_at",
                table: "SubscriptionDeliveries",
                newName: "CompletedAt");

            migrationBuilder.RenameIndex(
                name: "ix_subscription_deliveries_subscription_id_trigger_key_recipient_key",
                table: "SubscriptionDeliveries",
                newName: "IX_SubscriptionDeliveries_SubscriptionId_TriggerKey_RecipientKey");

            migrationBuilder.RenameIndex(
                name: "ix_subscription_deliveries_delivery_id",
                table: "SubscriptionDeliveries",
                newName: "IX_SubscriptionDeliveries_DeliveryId");

            migrationBuilder.RenameColumn(
                name: "content",
                table: "StudioRecoveryDrafts",
                newName: "Content");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StudioRecoveryDrafts",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "StudioRecoveryDrafts",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "StudioRecoveryDrafts",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "StudioRecoveryDrafts",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "base_version",
                table: "StudioRecoveryDrafts",
                newName: "BaseVersion");

            migrationBuilder.RenameColumn(
                name: "base_source_revision",
                table: "StudioRecoveryDrafts",
                newName: "BaseSourceRevision");

            migrationBuilder.RenameIndex(
                name: "ix_studio_recovery_drafts_user_id_report_id",
                table: "StudioRecoveryDrafts",
                newName: "IX_StudioRecoveryDrafts_UserId_ReportId");

            migrationBuilder.RenameIndex(
                name: "ix_studio_recovery_drafts_report_id",
                table: "StudioRecoveryDrafts",
                newName: "IX_StudioRecoveryDrafts_ReportId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "StewardshipSettings",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "scope",
                table: "StewardshipSettings",
                newName: "Scope");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipSettings",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by_user_id",
                table: "StewardshipSettings",
                newName: "UpdatedByUserId");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "StewardshipSettings",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipSettings",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "target_score",
                table: "StewardshipSettings",
                newName: "TargetScore");

            migrationBuilder.RenameColumn(
                name: "stale_after_days",
                table: "StewardshipSettings",
                newName: "StaleAfterDays");

            migrationBuilder.RenameColumn(
                name: "policy_level",
                table: "StewardshipSettings",
                newName: "PolicyLevel");

            migrationBuilder.RenameColumn(
                name: "enable_staleness_check",
                table: "StewardshipSettings",
                newName: "EnableStalenessCheck");

            migrationBuilder.RenameColumn(
                name: "enable_protected_data_check",
                table: "StewardshipSettings",
                newName: "EnableProtectedDataCheck");

            migrationBuilder.RenameColumn(
                name: "enable_metadata_check",
                table: "StewardshipSettings",
                newName: "EnableMetadataCheck");

            migrationBuilder.RenameColumn(
                name: "enable_glossary_check",
                table: "StewardshipSettings",
                newName: "EnableGlossaryCheck");

            migrationBuilder.RenameColumn(
                name: "deduct_staleness",
                table: "StewardshipSettings",
                newName: "DeductStaleness");

            migrationBuilder.RenameColumn(
                name: "deduct_protected_data",
                table: "StewardshipSettings",
                newName: "DeductProtectedData");

            migrationBuilder.RenameColumn(
                name: "deduct_metadata",
                table: "StewardshipSettings",
                newName: "DeductMetadata");

            migrationBuilder.RenameColumn(
                name: "deduct_glossary",
                table: "StewardshipSettings",
                newName: "DeductGlossary");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_settings_tenant_id_scope",
                table: "StewardshipSettings",
                newName: "IX_StewardshipSettings_TenantId_Scope");

            migrationBuilder.RenameColumn(
                name: "trigger",
                table: "StewardshipScans",
                newName: "Trigger");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "StewardshipScans",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "error",
                table: "StewardshipScans",
                newName: "Error");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipScans",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipScans",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "started_by_user_id",
                table: "StewardshipScans",
                newName: "StartedByUserId");

            migrationBuilder.RenameColumn(
                name: "started_at_utc",
                table: "StewardshipScans",
                newName: "StartedAtUtc");

            migrationBuilder.RenameColumn(
                name: "findings_resolved",
                table: "StewardshipScans",
                newName: "FindingsResolved");

            migrationBuilder.RenameColumn(
                name: "findings_reopened",
                table: "StewardshipScans",
                newName: "FindingsReopened");

            migrationBuilder.RenameColumn(
                name: "findings_opened",
                table: "StewardshipScans",
                newName: "FindingsOpened");

            migrationBuilder.RenameColumn(
                name: "completed_at_utc",
                table: "StewardshipScans",
                newName: "CompletedAtUtc");

            migrationBuilder.RenameColumn(
                name: "assets_scanned",
                table: "StewardshipScans",
                newName: "AssetsScanned");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_scans_tenant_id_started_at_utc",
                table: "StewardshipScans",
                newName: "IX_StewardshipScans_TenantId_StartedAtUtc");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "StewardshipResolutionCategories",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "value",
                table: "StewardshipResolutionCategories",
                newName: "Value");

            migrationBuilder.RenameColumn(
                name: "label",
                table: "StewardshipResolutionCategories",
                newName: "Label");

            migrationBuilder.RenameColumn(
                name: "disabled",
                table: "StewardshipResolutionCategories",
                newName: "Disabled");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "StewardshipResolutionCategories",
                newName: "Color");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipResolutionCategories",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipResolutionCategories",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "expiry_days",
                table: "StewardshipResolutionCategories",
                newName: "ExpiryDays");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "StewardshipResolutionCategories",
                newName: "CreatedByUserId");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "StewardshipResolutionCategories",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_resolution_categories_tenant_id_value",
                table: "StewardshipResolutionCategories",
                newName: "IX_StewardshipResolutionCategories_TenantId_Value");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "StewardshipGlossaryTerms",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "term",
                table: "StewardshipGlossaryTerms",
                newName: "Term");

            migrationBuilder.RenameColumn(
                name: "steward",
                table: "StewardshipGlossaryTerms",
                newName: "Steward");

            migrationBuilder.RenameColumn(
                name: "formula",
                table: "StewardshipGlossaryTerms",
                newName: "Formula");

            migrationBuilder.RenameColumn(
                name: "disabled",
                table: "StewardshipGlossaryTerms",
                newName: "Disabled");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "StewardshipGlossaryTerms",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "aliases",
                table: "StewardshipGlossaryTerms",
                newName: "Aliases");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipGlossaryTerms",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by_user_id",
                table: "StewardshipGlossaryTerms",
                newName: "UpdatedByUserId");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "StewardshipGlossaryTerms",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipGlossaryTerms",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "data_type",
                table: "StewardshipGlossaryTerms",
                newName: "DataType");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "StewardshipGlossaryTerms",
                newName: "CreatedByUserId");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "StewardshipGlossaryTerms",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_glossary_terms_tenant_id_term",
                table: "StewardshipGlossaryTerms",
                newName: "IX_StewardshipGlossaryTerms_TenantId_Term");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "StewardshipFindings",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "StewardshipFindings",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "detail",
                table: "StewardshipFindings",
                newName: "Detail");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipFindings",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipFindings",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "suppressed_until_utc",
                table: "StewardshipFindings",
                newName: "SuppressedUntilUtc");

            migrationBuilder.RenameColumn(
                name: "rule_key",
                table: "StewardshipFindings",
                newName: "RuleKey");

            migrationBuilder.RenameColumn(
                name: "resolved_at_utc",
                table: "StewardshipFindings",
                newName: "ResolvedAtUtc");

            migrationBuilder.RenameColumn(
                name: "last_seen_utc",
                table: "StewardshipFindings",
                newName: "LastSeenUtc");

            migrationBuilder.RenameColumn(
                name: "first_seen_utc",
                table: "StewardshipFindings",
                newName: "FirstSeenUtc");

            migrationBuilder.RenameColumn(
                name: "asset_version",
                table: "StewardshipFindings",
                newName: "AssetVersion");

            migrationBuilder.RenameColumn(
                name: "asset_key",
                table: "StewardshipFindings",
                newName: "AssetKey");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_findings_tenant_id_status",
                table: "StewardshipFindings",
                newName: "IX_StewardshipFindings_TenantId_Status");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_findings_tenant_id_asset_key_rule_key",
                table: "StewardshipFindings",
                newName: "IX_StewardshipFindings_TenantId_AssetKey_RuleKey");

            migrationBuilder.RenameColumn(
                name: "reason",
                table: "StewardshipFindingDecisions",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "decision",
                table: "StewardshipFindingDecisions",
                newName: "Decision");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipFindingDecisions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipFindingDecisions",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "finding_id",
                table: "StewardshipFindingDecisions",
                newName: "FindingId");

            migrationBuilder.RenameColumn(
                name: "decided_by_user_name",
                table: "StewardshipFindingDecisions",
                newName: "DecidedByUserName");

            migrationBuilder.RenameColumn(
                name: "decided_by_user_id",
                table: "StewardshipFindingDecisions",
                newName: "DecidedByUserId");

            migrationBuilder.RenameColumn(
                name: "decided_at_utc",
                table: "StewardshipFindingDecisions",
                newName: "DecidedAtUtc");

            migrationBuilder.RenameColumn(
                name: "category_value",
                table: "StewardshipFindingDecisions",
                newName: "CategoryValue");

            migrationBuilder.RenameColumn(
                name: "asset_version",
                table: "StewardshipFindingDecisions",
                newName: "AssetVersion");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_finding_decisions_tenant_id_finding_id_decided_at_utc",
                table: "StewardshipFindingDecisions",
                newName: "IX_StewardshipFindingDecisions_TenantId_FindingId_DecidedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_finding_decisions_finding_id",
                table: "StewardshipFindingDecisions",
                newName: "IX_StewardshipFindingDecisions_FindingId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "StewardshipAssetReviews",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "note",
                table: "StewardshipAssetReviews",
                newName: "Note");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipAssetReviews",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipAssetReviews",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "reviewed_version",
                table: "StewardshipAssetReviews",
                newName: "ReviewedVersion");

            migrationBuilder.RenameColumn(
                name: "reviewed_by_user_id",
                table: "StewardshipAssetReviews",
                newName: "ReviewedByUserId");

            migrationBuilder.RenameColumn(
                name: "reviewed_at_utc",
                table: "StewardshipAssetReviews",
                newName: "ReviewedAtUtc");

            migrationBuilder.RenameColumn(
                name: "asset_key",
                table: "StewardshipAssetReviews",
                newName: "AssetKey");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_asset_reviews_tenant_id_asset_key",
                table: "StewardshipAssetReviews",
                newName: "IX_StewardshipAssetReviews_TenantId_AssetKey");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "StewardshipAssetBadges",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "reason",
                table: "StewardshipAssetBadges",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "badge",
                table: "StewardshipAssetBadges",
                newName: "Badge");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "StewardshipAssetBadges",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "StewardshipAssetBadges",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "assigned_by_user_id",
                table: "StewardshipAssetBadges",
                newName: "AssignedByUserId");

            migrationBuilder.RenameColumn(
                name: "assigned_at_utc",
                table: "StewardshipAssetBadges",
                newName: "AssignedAtUtc");

            migrationBuilder.RenameColumn(
                name: "asset_version",
                table: "StewardshipAssetBadges",
                newName: "AssetVersion");

            migrationBuilder.RenameColumn(
                name: "asset_key",
                table: "StewardshipAssetBadges",
                newName: "AssetKey");

            migrationBuilder.RenameIndex(
                name: "ix_stewardship_asset_badges_tenant_id_asset_key_badge",
                table: "StewardshipAssetBadges",
                newName: "IX_StewardshipAssetBadges_TenantId_AssetKey_Badge");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "SharedTenantResources",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "SharedTenantResources",
                newName: "Kind");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SharedTenantResources",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "SharedTenantResources",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "SharedTenantResources",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "scoped_id",
                table: "SharedTenantResources",
                newName: "ScopedId");

            migrationBuilder.RenameColumn(
                name: "logical_id",
                table: "SharedTenantResources",
                newName: "LogicalId");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "SharedTenantResources",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_shared_tenant_resources_tenant_id_scoped_id",
                table: "SharedTenantResources",
                newName: "IX_SharedTenantResources_TenantId_ScopedId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_tenant_resources_tenant_id_kind_logical_id",
                table: "SharedTenantResources",
                newName: "IX_SharedTenantResources_TenantId_Kind_LogicalId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_tenant_resources_tenant_id_kind_id",
                table: "SharedTenantResources",
                newName: "IX_SharedTenantResources_TenantId_Kind_Id");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "SharedTenantLifecycles",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "state",
                table: "SharedTenantLifecycles",
                newName: "State");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "SharedTenantLifecycles",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "max_storage_mb",
                table: "SharedTenantLifecycles",
                newName: "MaxStorageMb");

            migrationBuilder.RenameColumn(
                name: "max_report_sessions",
                table: "SharedTenantLifecycles",
                newName: "MaxReportSessions");

            migrationBuilder.RenameColumn(
                name: "max_concurrent_jobs",
                table: "SharedTenantLifecycles",
                newName: "MaxConcurrentJobs");

            migrationBuilder.RenameColumn(
                name: "fence_epoch",
                table: "SharedTenantLifecycles",
                newName: "FenceEpoch");

            migrationBuilder.RenameColumn(
                name: "deleted_at_utc",
                table: "SharedTenantLifecycles",
                newName: "DeletedAtUtc");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "SharedTenantLifecycles",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "active_release",
                table: "SharedTenantLifecycles",
                newName: "ActiveRelease");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "SharedTenantLifecycles",
                newName: "TenantId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_tenant_lifecycles_state",
                table: "SharedTenantLifecycles",
                newName: "IX_SharedTenantLifecycles_State");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "SharedTenantLifecycleOperations",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "SharedTenantLifecycleOperations",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "reason",
                table: "SharedTenantLifecycleOperations",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "phase",
                table: "SharedTenantLifecycleOperations",
                newName: "Phase");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "SharedTenantLifecycleOperations",
                newName: "Kind");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "SharedTenantLifecycleOperations",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "SharedTenantLifecycleOperations",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "target_release",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetRelease");

            migrationBuilder.RenameColumn(
                name: "target_portal_host",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetPortalHost");

            migrationBuilder.RenameColumn(
                name: "target_max_storage_mb",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetMaxStorageMb");

            migrationBuilder.RenameColumn(
                name: "target_max_report_sessions",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetMaxReportSessions");

            migrationBuilder.RenameColumn(
                name: "target_max_concurrent_jobs",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetMaxConcurrentJobs");

            migrationBuilder.RenameColumn(
                name: "target_login_domain",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetLoginDomain");

            migrationBuilder.RenameColumn(
                name: "target_issuer",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetIssuer");

            migrationBuilder.RenameColumn(
                name: "target_client_secret_reference",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetClientSecretReference");

            migrationBuilder.RenameColumn(
                name: "target_client_id",
                table: "SharedTenantLifecycleOperations",
                newName: "TargetClientId");

            migrationBuilder.RenameColumn(
                name: "started_at_utc",
                table: "SharedTenantLifecycleOperations",
                newName: "StartedAtUtc");

            migrationBuilder.RenameColumn(
                name: "platform_operator",
                table: "SharedTenantLifecycleOperations",
                newName: "PlatformOperator");

            migrationBuilder.RenameColumn(
                name: "failure_code",
                table: "SharedTenantLifecycleOperations",
                newName: "FailureCode");

            migrationBuilder.RenameColumn(
                name: "completed_at_utc",
                table: "SharedTenantLifecycleOperations",
                newName: "CompletedAtUtc");

            migrationBuilder.RenameColumn(
                name: "authorization_reference",
                table: "SharedTenantLifecycleOperations",
                newName: "AuthorizationReference");

            migrationBuilder.RenameColumn(
                name: "authorization_expires_utc",
                table: "SharedTenantLifecycleOperations",
                newName: "AuthorizationExpiresUtc");

            migrationBuilder.RenameColumn(
                name: "operation_id",
                table: "SharedTenantLifecycleOperations",
                newName: "OperationId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_tenant_lifecycle_operations_tenant_id_status",
                table: "SharedTenantLifecycleOperations",
                newName: "IX_SharedTenantLifecycleOperations_TenantId_Status");

            migrationBuilder.RenameIndex(
                name: "ix_shared_tenant_lifecycle_operations_kind_authorization_reference",
                table: "SharedTenantLifecycleOperations",
                newName: "IX_SharedTenantLifecycleOperations_Kind_AuthorizationReference");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "SharedIdentityAuthorities",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "issuer",
                table: "SharedIdentityAuthorities",
                newName: "Issuer");

            migrationBuilder.RenameColumn(
                name: "enabled",
                table: "SharedIdentityAuthorities",
                newName: "Enabled");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SharedIdentityAuthorities",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "SharedIdentityAuthorities",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "SharedIdentityAuthorities",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "portal_host",
                table: "SharedIdentityAuthorities",
                newName: "PortalHost");

            migrationBuilder.RenameColumn(
                name: "login_domain",
                table: "SharedIdentityAuthorities",
                newName: "LoginDomain");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "SharedIdentityAuthorities",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "client_secret_reference",
                table: "SharedIdentityAuthorities",
                newName: "ClientSecretReference");

            migrationBuilder.RenameColumn(
                name: "client_id",
                table: "SharedIdentityAuthorities",
                newName: "ClientId");

            migrationBuilder.RenameColumn(
                name: "authority_id",
                table: "SharedIdentityAuthorities",
                newName: "AuthorityId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_identity_authorities_tenant_id_issuer",
                table: "SharedIdentityAuthorities",
                newName: "IX_SharedIdentityAuthorities_TenantId_Issuer");

            migrationBuilder.RenameIndex(
                name: "ix_shared_identity_authorities_portal_host",
                table: "SharedIdentityAuthorities",
                newName: "IX_SharedIdentityAuthorities_PortalHost");

            migrationBuilder.RenameIndex(
                name: "ix_shared_identity_authorities_login_domain",
                table: "SharedIdentityAuthorities",
                newName: "IX_SharedIdentityAuthorities_LoginDomain");

            migrationBuilder.RenameIndex(
                name: "ix_shared_identity_authorities_authority_id",
                table: "SharedIdentityAuthorities",
                newName: "IX_SharedIdentityAuthorities_AuthorityId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SharedConnectionUsages",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "use_count",
                table: "SharedConnectionUsages",
                newName: "UseCount");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "SharedConnectionUsages",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "shared_connection_id",
                table: "SharedConnectionUsages",
                newName: "SharedConnectionId");

            migrationBuilder.RenameColumn(
                name: "last_used_at_utc",
                table: "SharedConnectionUsages",
                newName: "LastUsedAtUtc");

            migrationBuilder.RenameColumn(
                name: "consumer_user",
                table: "SharedConnectionUsages",
                newName: "ConsumerUser");

            migrationBuilder.RenameIndex(
                name: "ix_shared_connection_usages_tenant_id_shared_connection_id_consumer_user",
                table: "SharedConnectionUsages",
                newName: "IX_SharedConnectionUsages_TenantId_SharedConnectionId_ConsumerUser");

            migrationBuilder.RenameIndex(
                name: "ix_shared_connection_usages_shared_connection_id",
                table: "SharedConnectionUsages",
                newName: "IX_SharedConnectionUsages_SharedConnectionId");

            migrationBuilder.RenameColumn(
                name: "permission",
                table: "SharedConnectionAcls",
                newName: "Permission");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SharedConnectionAcls",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "SharedConnectionAcls",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "shared_connection_id",
                table: "SharedConnectionAcls",
                newName: "SharedConnectionId");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "SharedConnectionAcls",
                newName: "GroupId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_connection_acls_tenant_id_shared_connection_id_group_id",
                table: "SharedConnectionAcls",
                newName: "IX_SharedConnectionAcls_TenantId_SharedConnectionId_GroupId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_connection_acls_shared_connection_id",
                table: "SharedConnectionAcls",
                newName: "IX_SharedConnectionAcls_SharedConnectionId");

            migrationBuilder.RenameIndex(
                name: "ix_shared_connection_acls_group_id",
                table: "SharedConnectionAcls",
                newName: "IX_SharedConnectionAcls_GroupId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "ServiceAccounts",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "scopes",
                table: "ServiceAccounts",
                newName: "Scopes");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ServiceAccounts",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "ServiceAccounts",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ServiceAccounts",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "ServiceAccounts",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "ServiceAccounts",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "studio_capabilities",
                table: "ServiceAccounts",
                newName: "StudioCapabilities");

            migrationBuilder.RenameColumn(
                name: "security_stamp",
                table: "ServiceAccounts",
                newName: "SecurityStamp");

            migrationBuilder.RenameColumn(
                name: "secret_hash",
                table: "ServiceAccounts",
                newName: "SecretHash");

            migrationBuilder.RenameColumn(
                name: "role_names",
                table: "ServiceAccounts",
                newName: "RoleNames");

            migrationBuilder.RenameColumn(
                name: "revoked_at",
                table: "ServiceAccounts",
                newName: "RevokedAt");

            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "ServiceAccounts",
                newName: "OwnerUserId");

            migrationBuilder.RenameColumn(
                name: "normalized_name",
                table: "ServiceAccounts",
                newName: "NormalizedName");

            migrationBuilder.RenameColumn(
                name: "last_used_at",
                table: "ServiceAccounts",
                newName: "LastUsedAt");

            migrationBuilder.RenameColumn(
                name: "is_enabled",
                table: "ServiceAccounts",
                newName: "IsEnabled");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "ServiceAccounts",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ServiceAccounts",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "client_id",
                table: "ServiceAccounts",
                newName: "ClientId");

            migrationBuilder.RenameIndex(
                name: "ix_service_accounts_tenant_id_normalized_name",
                table: "ServiceAccounts",
                newName: "IX_ServiceAccounts_TenantId_NormalizedName");

            migrationBuilder.RenameIndex(
                name: "ix_service_accounts_owner_user_id",
                table: "ServiceAccounts",
                newName: "IX_ServiceAccounts_OwnerUserId");

            migrationBuilder.RenameIndex(
                name: "ix_service_accounts_client_id",
                table: "ServiceAccounts",
                newName: "IX_ServiceAccounts_ClientId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "SavedReportViews",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "SavedReportViews",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "SavedReportViews",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "SavedReportViews",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "state_json",
                table: "SavedReportViews",
                newName: "StateJson");

            migrationBuilder.RenameColumn(
                name: "script_hash",
                table: "SavedReportViews",
                newName: "ScriptHash");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "SavedReportViews",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "parameters_json",
                table: "SavedReportViews",
                newName: "ParametersJson");

            migrationBuilder.RenameColumn(
                name: "is_default",
                table: "SavedReportViews",
                newName: "IsDefault");

            migrationBuilder.RenameColumn(
                name: "filters_json",
                table: "SavedReportViews",
                newName: "FiltersJson");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "SavedReportViews",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_saved_report_views_user_id_report_id_name",
                table: "SavedReportViews",
                newName: "IX_SavedReportViews_UserId_ReportId_Name");

            migrationBuilder.RenameIndex(
                name: "ix_saved_report_views_report_id",
                table: "SavedReportViews",
                newName: "IX_SavedReportViews_ReportId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportSnapshots",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "script_hash_at_run_time",
                table: "ReportSnapshots",
                newName: "ScriptHashAtRunTime");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportSnapshots",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "parameters_json",
                table: "ReportSnapshots",
                newName: "ParametersJson");

            migrationBuilder.RenameColumn(
                name: "manifest_path",
                table: "ReportSnapshots",
                newName: "ManifestPath");

            migrationBuilder.RenameColumn(
                name: "hash_matched",
                table: "ReportSnapshots",
                newName: "HashMatched");

            migrationBuilder.RenameColumn(
                name: "built_by",
                table: "ReportSnapshots",
                newName: "BuiltBy");

            migrationBuilder.RenameColumn(
                name: "built_at",
                table: "ReportSnapshots",
                newName: "BuiltAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_snapshots_report_id",
                table: "ReportSnapshots",
                newName: "IX_ReportSnapshots_ReportId");

            migrationBuilder.RenameColumn(
                name: "token",
                table: "ReportShareLinks",
                newName: "Token");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ReportShareLinks",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportShareLinks",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "revoked_at",
                table: "ReportShareLinks",
                newName: "RevokedAt");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportShareLinks",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "ReportShareLinks",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "ReportShareLinks",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ReportShareLinks",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_share_links_token",
                table: "ReportShareLinks",
                newName: "IX_ReportShareLinks_Token");

            migrationBuilder.RenameIndex(
                name: "ix_report_share_links_report_id_name",
                table: "ReportShareLinks",
                newName: "IX_ReportShareLinks_ReportId_Name");

            migrationBuilder.RenameIndex(
                name: "ix_report_share_links_created_by",
                table: "ReportShareLinks",
                newName: "IX_ReportShareLinks_CreatedBy");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "ReportScriptDrafts",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "ReportScriptDrafts",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportScriptDrafts",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "ReportScriptDrafts",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "submitted_at_utc",
                table: "ReportScriptDrafts",
                newName: "SubmittedAtUtc");

            migrationBuilder.RenameColumn(
                name: "script_text",
                table: "ReportScriptDrafts",
                newName: "ScriptText");

            migrationBuilder.RenameColumn(
                name: "script_hash",
                table: "ReportScriptDrafts",
                newName: "ScriptHash");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportScriptDrafts",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "published_at_utc",
                table: "ReportScriptDrafts",
                newName: "PublishedAtUtc");

            migrationBuilder.RenameColumn(
                name: "edit_session_user_name",
                table: "ReportScriptDrafts",
                newName: "EditSessionUserName");

            migrationBuilder.RenameColumn(
                name: "edit_session_user_id",
                table: "ReportScriptDrafts",
                newName: "EditSessionUserId");

            migrationBuilder.RenameColumn(
                name: "edit_session_expires_at_utc",
                table: "ReportScriptDrafts",
                newName: "EditSessionExpiresAtUtc");

            migrationBuilder.RenameColumn(
                name: "decided_at_utc",
                table: "ReportScriptDrafts",
                newName: "DecidedAtUtc");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "ReportScriptDrafts",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "base_script_hash",
                table: "ReportScriptDrafts",
                newName: "BaseScriptHash");

            migrationBuilder.RenameColumn(
                name: "author_user_name",
                table: "ReportScriptDrafts",
                newName: "AuthorUserName");

            migrationBuilder.RenameColumn(
                name: "author_user_id",
                table: "ReportScriptDrafts",
                newName: "AuthorUserId");

            migrationBuilder.RenameColumn(
                name: "approved_by_user_name",
                table: "ReportScriptDrafts",
                newName: "ApprovedByUserName");

            migrationBuilder.RenameColumn(
                name: "approved_by_user_id",
                table: "ReportScriptDrafts",
                newName: "ApprovedByUserId");

            migrationBuilder.RenameIndex(
                name: "ix_report_script_drafts_report_id_status",
                table: "ReportScriptDrafts",
                newName: "IX_ReportScriptDrafts_ReportId_Status");

            migrationBuilder.RenameColumn(
                name: "reason",
                table: "ReportScriptDraftDecisions",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "decision",
                table: "ReportScriptDraftDecisions",
                newName: "Decision");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportScriptDraftDecisions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "script_hash",
                table: "ReportScriptDraftDecisions",
                newName: "ScriptHash");

            migrationBuilder.RenameColumn(
                name: "draft_id",
                table: "ReportScriptDraftDecisions",
                newName: "DraftId");

            migrationBuilder.RenameColumn(
                name: "decided_by_user_name",
                table: "ReportScriptDraftDecisions",
                newName: "DecidedByUserName");

            migrationBuilder.RenameColumn(
                name: "decided_by_user_id",
                table: "ReportScriptDraftDecisions",
                newName: "DecidedByUserId");

            migrationBuilder.RenameColumn(
                name: "decided_at_utc",
                table: "ReportScriptDraftDecisions",
                newName: "DecidedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_report_script_draft_decisions_draft_id_decided_at_utc",
                table: "ReportScriptDraftDecisions",
                newName: "IX_ReportScriptDraftDecisions_DraftId_DecidedAtUtc");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportJobLinks",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "ReportJobLinks",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportJobLinks",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "orchestrator_alias",
                table: "ReportJobLinks",
                newName: "OrchestratorAlias");

            migrationBuilder.RenameColumn(
                name: "last_refreshed_at",
                table: "ReportJobLinks",
                newName: "LastRefreshedAt");

            migrationBuilder.RenameColumn(
                name: "job_name",
                table: "ReportJobLinks",
                newName: "JobName");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ReportJobLinks",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_job_links_report_id_orchestrator_alias_job_name",
                table: "ReportJobLinks",
                newName: "IX_ReportJobLinks_ReportId_OrchestratorAlias_JobName");

            migrationBuilder.RenameIndex(
                name: "ix_report_job_links_job_name",
                table: "ReportJobLinks",
                newName: "IX_ReportJobLinks_JobName");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportFavorites",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "ReportFavorites",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportFavorites",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ReportFavorites",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_favorites_user_id_report_id",
                table: "ReportFavorites",
                newName: "IX_ReportFavorites_UserId_ReportId");

            migrationBuilder.RenameIndex(
                name: "ix_report_favorites_report_id",
                table: "ReportFavorites",
                newName: "IX_ReportFavorites_ReportId");

            migrationBuilder.RenameColumn(
                name: "token",
                table: "ReportEmbedTokens",
                newName: "Token");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ReportEmbedTokens",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportEmbedTokens",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "revoked_at",
                table: "ReportEmbedTokens",
                newName: "RevokedAt");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportEmbedTokens",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "ReportEmbedTokens",
                newName: "ExpiresAt");

            migrationBuilder.RenameColumn(
                name: "created_by",
                table: "ReportEmbedTokens",
                newName: "CreatedBy");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ReportEmbedTokens",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_embed_tokens_token",
                table: "ReportEmbedTokens",
                newName: "IX_ReportEmbedTokens_Token");

            migrationBuilder.RenameIndex(
                name: "ix_report_embed_tokens_report_id_name",
                table: "ReportEmbedTokens",
                newName: "IX_ReportEmbedTokens_ReportId_Name");

            migrationBuilder.RenameIndex(
                name: "ix_report_embed_tokens_created_by",
                table: "ReportEmbedTokens",
                newName: "IX_ReportEmbedTokens_CreatedBy");

            migrationBuilder.RenameColumn(
                name: "threshold",
                table: "ReportAlerts",
                newName: "Threshold");

            migrationBuilder.RenameColumn(
                name: "recipient",
                table: "ReportAlerts",
                newName: "Recipient");

            migrationBuilder.RenameColumn(
                name: "operator",
                table: "ReportAlerts",
                newName: "Operator");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ReportAlerts",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "ReportAlerts",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportAlerts",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "visual_name",
                table: "ReportAlerts",
                newName: "VisualName");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "ReportAlerts",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "smtp_alias",
                table: "ReportAlerts",
                newName: "SmtpAlias");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportAlerts",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "owner_id",
                table: "ReportAlerts",
                newName: "OwnerId");

            migrationBuilder.RenameColumn(
                name: "options_json",
                table: "ReportAlerts",
                newName: "OptionsJson");

            migrationBuilder.RenameColumn(
                name: "last_triggered_at",
                table: "ReportAlerts",
                newName: "LastTriggeredAt");

            migrationBuilder.RenameColumn(
                name: "last_state",
                table: "ReportAlerts",
                newName: "LastState");

            migrationBuilder.RenameColumn(
                name: "last_notified_at",
                table: "ReportAlerts",
                newName: "LastNotifiedAt");

            migrationBuilder.RenameColumn(
                name: "last_evaluated_at",
                table: "ReportAlerts",
                newName: "LastEvaluatedAt");

            migrationBuilder.RenameColumn(
                name: "last_checked_at",
                table: "ReportAlerts",
                newName: "LastCheckedAt");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "ReportAlerts",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "display_name",
                table: "ReportAlerts",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ReportAlerts",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_alerts_report_id",
                table: "ReportAlerts",
                newName: "IX_ReportAlerts_ReportId");

            migrationBuilder.RenameIndex(
                name: "ix_report_alerts_owner_id",
                table: "ReportAlerts",
                newName: "IX_ReportAlerts_OwnerId");

            migrationBuilder.RenameIndex(
                name: "ix_report_alerts_name",
                table: "ReportAlerts",
                newName: "IX_ReportAlerts_Name");

            migrationBuilder.RenameColumn(
                name: "permission",
                table: "ReportAcls",
                newName: "Permission");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportAcls",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "ReportAcls",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportAcls",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "ReportAcls",
                newName: "GroupId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ReportAcls",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_acls_user_id",
                table: "ReportAcls",
                newName: "IX_ReportAcls_UserId");

            migrationBuilder.RenameIndex(
                name: "ix_report_acls_group_id",
                table: "ReportAcls",
                newName: "IX_ReportAcls_GroupId");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "ReportAccessRequests",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "reason",
                table: "ReportAccessRequests",
                newName: "Reason");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "ReportAccessRequests",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "ReportAccessRequests",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "requester_user_id",
                table: "ReportAccessRequests",
                newName: "RequesterUserId");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "ReportAccessRequests",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "decision_reason",
                table: "ReportAccessRequests",
                newName: "DecisionReason");

            migrationBuilder.RenameColumn(
                name: "decided_by_user_id",
                table: "ReportAccessRequests",
                newName: "DecidedByUserId");

            migrationBuilder.RenameColumn(
                name: "decided_at",
                table: "ReportAccessRequests",
                newName: "DecidedAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "ReportAccessRequests",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_report_access_requests_report_id",
                table: "ReportAccessRequests",
                newName: "IX_ReportAccessRequests_ReportId");

            migrationBuilder.RenameIndex(
                name: "ix_report_access_requests_decided_by_user_id",
                table: "ReportAccessRequests",
                newName: "IX_ReportAccessRequests_DecidedByUserId");

            migrationBuilder.RenameColumn(
                name: "token",
                table: "RefreshTokens",
                newName: "Token");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "RefreshTokens",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "RefreshTokens",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "RefreshTokens",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "revoked_at",
                table: "RefreshTokens",
                newName: "RevokedAt");

            migrationBuilder.RenameColumn(
                name: "expires_at",
                table: "RefreshTokens",
                newName: "ExpiresAt");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_user_id",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_UserId");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_token",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_Token");

            migrationBuilder.RenameIndex(
                name: "ix_refresh_tokens_tenant_id_user_id",
                table: "RefreshTokens",
                newName: "IX_RefreshTokens_TenantId_UserId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "PortalSharedConnections",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "target",
                table: "PortalSharedConnections",
                newName: "Target");

            migrationBuilder.RenameColumn(
                name: "disabled",
                table: "PortalSharedConnections",
                newName: "Disabled");

            migrationBuilder.RenameColumn(
                name: "alias",
                table: "PortalSharedConnections",
                newName: "Alias");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "PortalSharedConnections",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by_user_id",
                table: "PortalSharedConnections",
                newName: "UpdatedByUserId");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "PortalSharedConnections",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "PortalSharedConnections",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "sensitive_fields_csv",
                table: "PortalSharedConnections",
                newName: "SensitiveFieldsCsv");

            migrationBuilder.RenameColumn(
                name: "owner_user_id",
                table: "PortalSharedConnections",
                newName: "OwnerUserId");

            migrationBuilder.RenameColumn(
                name: "options_json",
                table: "PortalSharedConnections",
                newName: "OptionsJson");

            migrationBuilder.RenameColumn(
                name: "last_verified_at_utc",
                table: "PortalSharedConnections",
                newName: "LastVerifiedAtUtc");

            migrationBuilder.RenameColumn(
                name: "last_used_at_utc",
                table: "PortalSharedConnections",
                newName: "LastUsedAtUtc");

            migrationBuilder.RenameColumn(
                name: "environment_scope",
                table: "PortalSharedConnections",
                newName: "EnvironmentScope");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "PortalSharedConnections",
                newName: "CreatedByUserId");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "PortalSharedConnections",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "connector_type",
                table: "PortalSharedConnections",
                newName: "ConnectorType");

            migrationBuilder.RenameIndex(
                name: "ix_portal_shared_connections_tenant_id_alias",
                table: "PortalSharedConnections",
                newName: "IX_PortalSharedConnections_TenantId_Alias");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "PortalSecrets",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "PortalSecrets",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "disabled",
                table: "PortalSecrets",
                newName: "Disabled");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "PortalSecrets",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_by_user_id",
                table: "PortalSecrets",
                newName: "UpdatedByUserId");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "PortalSecrets",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "PortalSecrets",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "encrypted_value",
                table: "PortalSecrets",
                newName: "EncryptedValue");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "PortalSecrets",
                newName: "CreatedByUserId");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "PortalSecrets",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_portal_secrets_tenant_id_name",
                table: "PortalSecrets",
                newName: "IX_PortalSecrets_TenantId_Name");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "PortalExecutionJobs",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "PortalExecutionJobs",
                newName: "Kind");

            migrationBuilder.RenameColumn(
                name: "error",
                table: "PortalExecutionJobs",
                newName: "Error");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "PortalExecutionJobs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "PortalExecutionJobs",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "PortalExecutionJobs",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "started_at",
                table: "PortalExecutionJobs",
                newName: "StartedAt");

            migrationBuilder.RenameColumn(
                name: "rows_processed",
                table: "PortalExecutionJobs",
                newName: "RowsProcessed");

            migrationBuilder.RenameColumn(
                name: "report_id",
                table: "PortalExecutionJobs",
                newName: "ReportId");

            migrationBuilder.RenameColumn(
                name: "peak_memory_bytes",
                table: "PortalExecutionJobs",
                newName: "PeakMemoryBytes");

            migrationBuilder.RenameColumn(
                name: "manifest_path",
                table: "PortalExecutionJobs",
                newName: "ManifestPath");

            migrationBuilder.RenameColumn(
                name: "effective_scopes",
                table: "PortalExecutionJobs",
                newName: "EffectiveScopes");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "PortalExecutionJobs",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "cpu_time_seconds",
                table: "PortalExecutionJobs",
                newName: "CpuTimeSeconds");

            migrationBuilder.RenameColumn(
                name: "correlation_id",
                table: "PortalExecutionJobs",
                newName: "CorrelationId");

            migrationBuilder.RenameColumn(
                name: "completed_at",
                table: "PortalExecutionJobs",
                newName: "CompletedAt");

            migrationBuilder.RenameColumn(
                name: "actor_type",
                table: "PortalExecutionJobs",
                newName: "ActorType");

            migrationBuilder.RenameColumn(
                name: "actor_id",
                table: "PortalExecutionJobs",
                newName: "ActorId");

            migrationBuilder.RenameIndex(
                name: "ix_portal_execution_jobs_tenant_id_completed_at",
                table: "PortalExecutionJobs",
                newName: "IX_PortalExecutionJobs_TenantId_CompletedAt");

            migrationBuilder.RenameColumn(
                name: "tenant",
                table: "PolicyVersions",
                newName: "Tenant");

            migrationBuilder.RenameColumn(
                name: "reviewer",
                table: "PolicyVersions",
                newName: "Reviewer");

            migrationBuilder.RenameColumn(
                name: "environment",
                table: "PolicyVersions",
                newName: "Environment");

            migrationBuilder.RenameColumn(
                name: "author",
                table: "PolicyVersions",
                newName: "Author");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "PolicyVersions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "superseded_version",
                table: "PolicyVersions",
                newName: "SupersededVersion");

            migrationBuilder.RenameColumn(
                name: "signed_envelope_json",
                table: "PolicyVersions",
                newName: "SignedEnvelopeJson");

            migrationBuilder.RenameColumn(
                name: "rollout_state",
                table: "PolicyVersions",
                newName: "RolloutState");

            migrationBuilder.RenameColumn(
                name: "published_at_utc",
                table: "PolicyVersions",
                newName: "PublishedAtUtc");

            migrationBuilder.RenameColumn(
                name: "policy_version",
                table: "PolicyVersions",
                newName: "PolicyVersion");

            migrationBuilder.RenameColumn(
                name: "policy_hash",
                table: "PolicyVersions",
                newName: "PolicyHash");

            migrationBuilder.RenameColumn(
                name: "issued_at_utc",
                table: "PolicyVersions",
                newName: "IssuedAtUtc");

            migrationBuilder.RenameColumn(
                name: "expires_at_utc",
                table: "PolicyVersions",
                newName: "ExpiresAtUtc");

            migrationBuilder.RenameColumn(
                name: "canary_percentage",
                table: "PolicyVersions",
                newName: "CanaryPercentage");

            migrationBuilder.RenameColumn(
                name: "canary_group",
                table: "PolicyVersions",
                newName: "CanaryGroup");

            migrationBuilder.RenameIndex(
                name: "ix_policy_versions_tenant_environment_rollout_state",
                table: "PolicyVersions",
                newName: "IX_PolicyVersions_Tenant_Environment_RolloutState");

            migrationBuilder.RenameIndex(
                name: "ix_policy_versions_tenant_environment_policy_version",
                table: "PolicyVersions",
                newName: "IX_PolicyVersions_Tenant_Environment_PolicyVersion");

            migrationBuilder.RenameColumn(
                name: "tenant",
                table: "PolicyMachines",
                newName: "Tenant");

            migrationBuilder.RenameColumn(
                name: "revoked",
                table: "PolicyMachines",
                newName: "Revoked");

            migrationBuilder.RenameColumn(
                name: "environment",
                table: "PolicyMachines",
                newName: "Environment");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "PolicyMachines",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "revoked_reason",
                table: "PolicyMachines",
                newName: "RevokedReason");

            migrationBuilder.RenameColumn(
                name: "revoked_at_utc",
                table: "PolicyMachines",
                newName: "RevokedAtUtc");

            migrationBuilder.RenameColumn(
                name: "registered_at_utc",
                table: "PolicyMachines",
                newName: "RegisteredAtUtc");

            migrationBuilder.RenameColumn(
                name: "machine_id",
                table: "PolicyMachines",
                newName: "MachineId");

            migrationBuilder.RenameColumn(
                name: "last_seen_at_utc",
                table: "PolicyMachines",
                newName: "LastSeenAtUtc");

            migrationBuilder.RenameColumn(
                name: "enrollment_id",
                table: "PolicyMachines",
                newName: "EnrollmentId");

            migrationBuilder.RenameColumn(
                name: "client_certificate_thumbprint",
                table: "PolicyMachines",
                newName: "ClientCertificateThumbprint");

            migrationBuilder.RenameColumn(
                name: "canary_group",
                table: "PolicyMachines",
                newName: "CanaryGroup");

            migrationBuilder.RenameIndex(
                name: "ix_policy_machines_tenant_environment",
                table: "PolicyMachines",
                newName: "IX_PolicyMachines_Tenant_Environment");

            migrationBuilder.RenameIndex(
                name: "ix_policy_machines_machine_id",
                table: "PolicyMachines",
                newName: "IX_PolicyMachines_MachineId");

            migrationBuilder.RenameColumn(
                name: "capability",
                table: "GroupStudioCapabilities",
                newName: "Capability");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "GroupStudioCapabilities",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "GroupStudioCapabilities",
                newName: "GroupId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "GroupStudioCapabilities",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_group_studio_capabilities_group_id_capability",
                table: "GroupStudioCapabilities",
                newName: "IX_GroupStudioCapabilities_GroupId_Capability");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "GatewayEnrollments",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "state",
                table: "GatewayEnrollments",
                newName: "State");

            migrationBuilder.RenameColumn(
                name: "workload_public_key_thumbprint",
                table: "GatewayEnrollments",
                newName: "WorkloadPublicKeyThumbprint");

            migrationBuilder.RenameColumn(
                name: "token_hash",
                table: "GatewayEnrollments",
                newName: "TokenHash");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "GatewayEnrollments",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "gateway_id",
                table: "GatewayEnrollments",
                newName: "GatewayId");

            migrationBuilder.RenameColumn(
                name: "expires_utc",
                table: "GatewayEnrollments",
                newName: "ExpiresUtc");

            migrationBuilder.RenameColumn(
                name: "created_utc",
                table: "GatewayEnrollments",
                newName: "CreatedUtc");

            migrationBuilder.RenameColumn(
                name: "consumed_utc",
                table: "GatewayEnrollments",
                newName: "ConsumedUtc");

            migrationBuilder.RenameColumn(
                name: "enrollment_id",
                table: "GatewayEnrollments",
                newName: "EnrollmentId");

            migrationBuilder.RenameIndex(
                name: "ix_gateway_enrollments_tenant_id_token_hash",
                table: "GatewayEnrollments",
                newName: "IX_GatewayEnrollments_TenantId_TokenHash");

            migrationBuilder.RenameIndex(
                name: "ix_gateway_enrollments_tenant_id_gateway_id",
                table: "GatewayEnrollments",
                newName: "IX_GatewayEnrollments_TenantId_GatewayId");

            migrationBuilder.RenameColumn(
                name: "resolution",
                table: "GatewayAmbiguousWriteEvents",
                newName: "Resolution");

            migrationBuilder.RenameColumn(
                name: "note",
                table: "GatewayAmbiguousWriteEvents",
                newName: "Note");

            migrationBuilder.RenameColumn(
                name: "actor",
                table: "GatewayAmbiguousWriteEvents",
                newName: "Actor");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "GatewayAmbiguousWriteEvents",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "GatewayAmbiguousWriteEvents",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "evidence_reference",
                table: "GatewayAmbiguousWriteEvents",
                newName: "EvidenceReference");

            migrationBuilder.RenameColumn(
                name: "event_type",
                table: "GatewayAmbiguousWriteEvents",
                newName: "EventType");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "GatewayAmbiguousWriteEvents",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "case_id",
                table: "GatewayAmbiguousWriteEvents",
                newName: "CaseId");

            migrationBuilder.RenameIndex(
                name: "ix_gateway_ambiguous_write_events_tenant_id_case_id_id",
                table: "GatewayAmbiguousWriteEvents",
                newName: "IX_GatewayAmbiguousWriteEvents_TenantId_CaseId_Id");

            migrationBuilder.RenameIndex(
                name: "ix_gateway_ambiguous_write_events_case_id",
                table: "GatewayAmbiguousWriteEvents",
                newName: "IX_GatewayAmbiguousWriteEvents_CaseId");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "GatewayAmbiguousWriteCases",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "state",
                table: "GatewayAmbiguousWriteCases",
                newName: "State");

            migrationBuilder.RenameColumn(
                name: "resolution",
                table: "GatewayAmbiguousWriteCases",
                newName: "Resolution");

            migrationBuilder.RenameColumn(
                name: "priority",
                table: "GatewayAmbiguousWriteCases",
                newName: "Priority");

            migrationBuilder.RenameColumn(
                name: "owner",
                table: "GatewayAmbiguousWriteCases",
                newName: "Owner");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "GatewayAmbiguousWriteCases",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "GatewayAmbiguousWriteCases",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "GatewayAmbiguousWriteCases",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "resource_id",
                table: "GatewayAmbiguousWriteCases",
                newName: "ResourceId");

            migrationBuilder.RenameColumn(
                name: "operation_id",
                table: "GatewayAmbiguousWriteCases",
                newName: "OperationId");

            migrationBuilder.RenameColumn(
                name: "gateway_id",
                table: "GatewayAmbiguousWriteCases",
                newName: "GatewayId");

            migrationBuilder.RenameColumn(
                name: "executed_at_utc",
                table: "GatewayAmbiguousWriteCases",
                newName: "ExecutedAtUtc");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "GatewayAmbiguousWriteCases",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "correlation_id",
                table: "GatewayAmbiguousWriteCases",
                newName: "CorrelationId");

            migrationBuilder.RenameIndex(
                name: "ix_gateway_ambiguous_write_cases_tenant_id_operation_id",
                table: "GatewayAmbiguousWriteCases",
                newName: "IX_GatewayAmbiguousWriteCases_TenantId_OperationId");

            migrationBuilder.RenameColumn(
                name: "permission",
                table: "FolderAcls",
                newName: "Permission");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "FolderAcls",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "FolderAcls",
                newName: "GroupId");

            migrationBuilder.RenameColumn(
                name: "folder_id",
                table: "FolderAcls",
                newName: "FolderId");

            migrationBuilder.RenameIndex(
                name: "ix_folder_acls_group_id",
                table: "FolderAcls",
                newName: "IX_FolderAcls_GroupId");

            migrationBuilder.RenameIndex(
                name: "ix_folder_acls_folder_id",
                table: "FolderAcls",
                newName: "IX_FolderAcls_FolderId");

            migrationBuilder.RenameColumn(
                name: "permission",
                table: "DatasetUserAcls",
                newName: "Permission");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "DatasetUserAcls",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "DatasetUserAcls",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "dataset_id",
                table: "DatasetUserAcls",
                newName: "DatasetId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "DatasetUserAcls",
                newName: "CreatedAt");

            migrationBuilder.RenameIndex(
                name: "ix_dataset_user_acls_user_id",
                table: "DatasetUserAcls",
                newName: "IX_DatasetUserAcls_UserId");

            migrationBuilder.RenameIndex(
                name: "ix_dataset_user_acls_dataset_id_user_id",
                table: "DatasetUserAcls",
                newName: "IX_DatasetUserAcls_DatasetId_UserId");

            migrationBuilder.RenameColumn(
                name: "permission",
                table: "DatasetAcls",
                newName: "Permission");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "DatasetAcls",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "group_id",
                table: "DatasetAcls",
                newName: "GroupId");

            migrationBuilder.RenameColumn(
                name: "dataset_id",
                table: "DatasetAcls",
                newName: "DatasetId");

            migrationBuilder.RenameIndex(
                name: "ix_dataset_acls_group_id",
                table: "DatasetAcls",
                newName: "IX_DatasetAcls_GroupId");

            migrationBuilder.RenameIndex(
                name: "ix_dataset_acls_dataset_id",
                table: "DatasetAcls",
                newName: "IX_DatasetAcls_DatasetId");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "AuditOutboxMessages",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "attempts",
                table: "AuditOutboxMessages",
                newName: "Attempts");

            migrationBuilder.RenameColumn(
                name: "action",
                table: "AuditOutboxMessages",
                newName: "Action");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AuditOutboxMessages",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AuditOutboxMessages",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "AuditOutboxMessages",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "AuditOutboxMessages",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "studio_capability",
                table: "AuditOutboxMessages",
                newName: "StudioCapability");

            migrationBuilder.RenameColumn(
                name: "resource_type",
                table: "AuditOutboxMessages",
                newName: "ResourceType");

            migrationBuilder.RenameColumn(
                name: "resource_id",
                table: "AuditOutboxMessages",
                newName: "ResourceId");

            migrationBuilder.RenameColumn(
                name: "payload_json",
                table: "AuditOutboxMessages",
                newName: "PayloadJson");

            migrationBuilder.RenameColumn(
                name: "occurred_at",
                table: "AuditOutboxMessages",
                newName: "OccurredAt");

            migrationBuilder.RenameColumn(
                name: "next_attempt_at",
                table: "AuditOutboxMessages",
                newName: "NextAttemptAt");

            migrationBuilder.RenameColumn(
                name: "locked_until",
                table: "AuditOutboxMessages",
                newName: "LockedUntil");

            migrationBuilder.RenameColumn(
                name: "last_error",
                table: "AuditOutboxMessages",
                newName: "LastError");

            migrationBuilder.RenameColumn(
                name: "event_id",
                table: "AuditOutboxMessages",
                newName: "EventId");

            migrationBuilder.RenameColumn(
                name: "effective_scopes",
                table: "AuditOutboxMessages",
                newName: "EffectiveScopes");

            migrationBuilder.RenameColumn(
                name: "delivered_at",
                table: "AuditOutboxMessages",
                newName: "DeliveredAt");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "AuditOutboxMessages",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "correlation_id",
                table: "AuditOutboxMessages",
                newName: "CorrelationId");

            migrationBuilder.RenameColumn(
                name: "audit_log_id",
                table: "AuditOutboxMessages",
                newName: "AuditLogId");

            migrationBuilder.RenameColumn(
                name: "actor_type",
                table: "AuditOutboxMessages",
                newName: "ActorType");

            migrationBuilder.RenameColumn(
                name: "actor_id",
                table: "AuditOutboxMessages",
                newName: "ActorId");

            migrationBuilder.RenameIndex(
                name: "ix_audit_outbox_messages_tenant_id_status_next_attempt_at",
                table: "AuditOutboxMessages",
                newName: "IX_AuditOutboxMessages_TenantId_Status_NextAttemptAt");

            migrationBuilder.RenameIndex(
                name: "ix_audit_outbox_messages_tenant_id_event_id",
                table: "AuditOutboxMessages",
                newName: "IX_AuditOutboxMessages_TenantId_EventId");

            migrationBuilder.RenameIndex(
                name: "ix_audit_outbox_messages_tenant_id_audit_log_id",
                table: "AuditOutboxMessages",
                newName: "IX_AuditOutboxMessages_TenantId_AuditLogId");

            migrationBuilder.RenameIndex(
                name: "ix_audit_outbox_messages_audit_log_id",
                table: "AuditOutboxMessages",
                newName: "IX_AuditOutboxMessages_AuditLogId");

            migrationBuilder.RenameColumn(
                name: "timestamp",
                table: "AuditLogs",
                newName: "Timestamp");

            migrationBuilder.RenameColumn(
                name: "detail",
                table: "AuditLogs",
                newName: "Detail");

            migrationBuilder.RenameColumn(
                name: "action",
                table: "AuditLogs",
                newName: "Action");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AuditLogs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AuditLogs",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "AuditLogs",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "studio_capability",
                table: "AuditLogs",
                newName: "StudioCapability");

            migrationBuilder.RenameColumn(
                name: "resource_type",
                table: "AuditLogs",
                newName: "ResourceType");

            migrationBuilder.RenameColumn(
                name: "resource_id",
                table: "AuditLogs",
                newName: "ResourceId");

            migrationBuilder.RenameColumn(
                name: "effective_scopes",
                table: "AuditLogs",
                newName: "EffectiveScopes");

            migrationBuilder.RenameColumn(
                name: "correlation_id",
                table: "AuditLogs",
                newName: "CorrelationId");

            migrationBuilder.RenameColumn(
                name: "actor_type",
                table: "AuditLogs",
                newName: "ActorType");

            migrationBuilder.RenameColumn(
                name: "actor_id",
                table: "AuditLogs",
                newName: "ActorId");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_tenant_id_timestamp",
                table: "AuditLogs",
                newName: "IX_AuditLogs_TenantId_Timestamp");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_tenant_id_resource_type_resource_id",
                table: "AuditLogs",
                newName: "IX_AuditLogs_TenantId_ResourceType_ResourceId");

            migrationBuilder.RenameIndex(
                name: "ix_audit_logs_tenant_id_action_timestamp",
                table: "AuditLogs",
                newName: "IX_AuditLogs_TenantId_Action_Timestamp");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "AspNetUsers",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "provider",
                table: "AspNetUsers",
                newName: "Provider");

            migrationBuilder.RenameColumn(
                name: "email",
                table: "AspNetUsers",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AspNetUsers",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_name",
                table: "AspNetUsers",
                newName: "UserName");

            migrationBuilder.RenameColumn(
                name: "two_factor_enabled",
                table: "AspNetUsers",
                newName: "TwoFactorEnabled");

            migrationBuilder.RenameColumn(
                name: "tenant_id",
                table: "AspNetUsers",
                newName: "TenantId");

            migrationBuilder.RenameColumn(
                name: "security_stamp",
                table: "AspNetUsers",
                newName: "SecurityStamp");

            migrationBuilder.RenameColumn(
                name: "principal_key",
                table: "AspNetUsers",
                newName: "PrincipalKey");

            migrationBuilder.RenameColumn(
                name: "phone_number_confirmed",
                table: "AspNetUsers",
                newName: "PhoneNumberConfirmed");

            migrationBuilder.RenameColumn(
                name: "phone_number",
                table: "AspNetUsers",
                newName: "PhoneNumber");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                table: "AspNetUsers",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "normalized_user_name",
                table: "AspNetUsers",
                newName: "NormalizedUserName");

            migrationBuilder.RenameColumn(
                name: "normalized_email",
                table: "AspNetUsers",
                newName: "NormalizedEmail");

            migrationBuilder.RenameColumn(
                name: "must_change_password",
                table: "AspNetUsers",
                newName: "MustChangePassword");

            migrationBuilder.RenameColumn(
                name: "middle_initial",
                table: "AspNetUsers",
                newName: "MiddleInitial");

            migrationBuilder.RenameColumn(
                name: "lockout_end",
                table: "AspNetUsers",
                newName: "LockoutEnd");

            migrationBuilder.RenameColumn(
                name: "lockout_enabled",
                table: "AspNetUsers",
                newName: "LockoutEnabled");

            migrationBuilder.RenameColumn(
                name: "last_name",
                table: "AspNetUsers",
                newName: "LastName");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "AspNetUsers",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "first_name",
                table: "AspNetUsers",
                newName: "FirstName");

            migrationBuilder.RenameColumn(
                name: "external_subject",
                table: "AspNetUsers",
                newName: "ExternalSubject");

            migrationBuilder.RenameColumn(
                name: "external_issuer",
                table: "AspNetUsers",
                newName: "ExternalIssuer");

            migrationBuilder.RenameColumn(
                name: "email_confirmed",
                table: "AspNetUsers",
                newName: "EmailConfirmed");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "AspNetUsers",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "concurrency_stamp",
                table: "AspNetUsers",
                newName: "ConcurrencyStamp");

            migrationBuilder.RenameColumn(
                name: "access_failed_count",
                table: "AspNetUsers",
                newName: "AccessFailedCount");

            migrationBuilder.RenameIndex(
                name: "user_name_index",
                table: "AspNetUsers",
                newName: "UserNameIndex");

            migrationBuilder.RenameIndex(
                name: "ix_asp_net_users_tenant_id_provider_external_issuer_external_subject",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_TenantId_Provider_ExternalIssuer_ExternalSubject");

            migrationBuilder.RenameIndex(
                name: "email_index",
                table: "AspNetUsers",
                newName: "EmailIndex");

            migrationBuilder.RenameColumn(
                name: "value",
                table: "AspNetUserTokens",
                newName: "Value");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "AspNetUserTokens",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "login_provider",
                table: "AspNetUserTokens",
                newName: "LoginProvider");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AspNetUserTokens",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "role_id",
                table: "AspNetUserRoles",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AspNetUserRoles",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "AspNetUserRoles",
                newName: "IX_AspNetUserRoles_RoleId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AspNetUserLogins",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "provider_display_name",
                table: "AspNetUserLogins",
                newName: "ProviderDisplayName");

            migrationBuilder.RenameColumn(
                name: "provider_key",
                table: "AspNetUserLogins",
                newName: "ProviderKey");

            migrationBuilder.RenameColumn(
                name: "login_provider",
                table: "AspNetUserLogins",
                newName: "LoginProvider");

            migrationBuilder.RenameIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "AspNetUserLogins",
                newName: "IX_AspNetUserLogins_UserId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AspNetUserClaims",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "AspNetUserClaims",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "claim_value",
                table: "AspNetUserClaims",
                newName: "ClaimValue");

            migrationBuilder.RenameColumn(
                name: "claim_type",
                table: "AspNetUserClaims",
                newName: "ClaimType");

            migrationBuilder.RenameIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "AspNetUserClaims",
                newName: "IX_AspNetUserClaims_UserId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "AspNetRoles",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AspNetRoles",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "normalized_name",
                table: "AspNetRoles",
                newName: "NormalizedName");

            migrationBuilder.RenameColumn(
                name: "concurrency_stamp",
                table: "AspNetRoles",
                newName: "ConcurrencyStamp");

            migrationBuilder.RenameIndex(
                name: "role_name_index",
                table: "AspNetRoles",
                newName: "RoleNameIndex");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AspNetRoleClaims",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "role_id",
                table: "AspNetRoleClaims",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "claim_value",
                table: "AspNetRoleClaims",
                newName: "ClaimValue");

            migrationBuilder.RenameColumn(
                name: "claim_type",
                table: "AspNetRoleClaims",
                newName: "ClaimType");

            migrationBuilder.RenameIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "AspNetRoleClaims",
                newName: "IX_AspNetRoleClaims_RoleId");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AlertNotifications",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "orchestrator_alias",
                table: "AlertNotifications",
                newName: "OrchestratorAlias");

            migrationBuilder.RenameColumn(
                name: "notification_name",
                table: "AlertNotifications",
                newName: "NotificationName");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "AlertNotifications",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "alert_id",
                table: "AlertNotifications",
                newName: "AlertId");

            migrationBuilder.RenameIndex(
                name: "ix_alert_notifications_alert_id_orchestrator_alias_notification_name",
                table: "AlertNotifications",
                newName: "IX_AlertNotifications_AlertId_OrchestratorAlias_NotificationName");

            migrationBuilder.RenameColumn(
                name: "outcome",
                table: "AdminServiceRuns",
                newName: "Outcome");

            migrationBuilder.RenameColumn(
                name: "detail",
                table: "AdminServiceRuns",
                newName: "Detail");

            migrationBuilder.RenameColumn(
                name: "attempts",
                table: "AdminServiceRuns",
                newName: "Attempts");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "AdminServiceRuns",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "started_at_utc",
                table: "AdminServiceRuns",
                newName: "StartedAtUtc");

            migrationBuilder.RenameColumn(
                name: "service_name",
                table: "AdminServiceRuns",
                newName: "ServiceName");

            migrationBuilder.RenameColumn(
                name: "node_name",
                table: "AdminServiceRuns",
                newName: "NodeName");

            migrationBuilder.RenameColumn(
                name: "completed_at_utc",
                table: "AdminServiceRuns",
                newName: "CompletedAtUtc");

            migrationBuilder.RenameIndex(
                name: "ix_admin_service_runs_service_name_started_at_utc",
                table: "AdminServiceRuns",
                newName: "IX_AdminServiceRuns_ServiceName_StartedAtUtc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Subscriptions",
                table: "Subscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Reports",
                table: "Reports",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Groups",
                table: "Groups",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Folders",
                table: "Folders",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Datasets",
                table: "Datasets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkloadIdentityReplays",
                table: "WorkloadIdentityReplays",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserGroups",
                table: "UserGroups",
                columns: new[] { "UserId", "GroupId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionDeliveries",
                table: "SubscriptionDeliveries",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StudioRecoveryDrafts",
                table: "StudioRecoveryDrafts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipSettings",
                table: "StewardshipSettings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipScans",
                table: "StewardshipScans",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipResolutionCategories",
                table: "StewardshipResolutionCategories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipGlossaryTerms",
                table: "StewardshipGlossaryTerms",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipFindings",
                table: "StewardshipFindings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipFindingDecisions",
                table: "StewardshipFindingDecisions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipAssetReviews",
                table: "StewardshipAssetReviews",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StewardshipAssetBadges",
                table: "StewardshipAssetBadges",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SharedTenantResources",
                table: "SharedTenantResources",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SharedTenantLifecycles",
                table: "SharedTenantLifecycles",
                column: "TenantId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SharedTenantLifecycleOperations",
                table: "SharedTenantLifecycleOperations",
                column: "OperationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SharedIdentityAuthorities",
                table: "SharedIdentityAuthorities",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SharedConnectionUsages",
                table: "SharedConnectionUsages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SharedConnectionAcls",
                table: "SharedConnectionAcls",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ServiceAccounts",
                table: "ServiceAccounts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SavedReportViews",
                table: "SavedReportViews",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportSnapshots",
                table: "ReportSnapshots",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportShareLinks",
                table: "ReportShareLinks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportScriptDrafts",
                table: "ReportScriptDrafts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportScriptDraftDecisions",
                table: "ReportScriptDraftDecisions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportJobLinks",
                table: "ReportJobLinks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportFavorites",
                table: "ReportFavorites",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportEmbedTokens",
                table: "ReportEmbedTokens",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportAlerts",
                table: "ReportAlerts",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportAcls",
                table: "ReportAcls",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportAccessRequests",
                table: "ReportAccessRequests",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RefreshTokens",
                table: "RefreshTokens",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PortalSharedConnections",
                table: "PortalSharedConnections",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PortalSecrets",
                table: "PortalSecrets",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PortalExecutionJobs",
                table: "PortalExecutionJobs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PolicyVersions",
                table: "PolicyVersions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PolicyMachines",
                table: "PolicyMachines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GroupStudioCapabilities",
                table: "GroupStudioCapabilities",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GatewayEnrollments",
                table: "GatewayEnrollments",
                column: "EnrollmentId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GatewayAmbiguousWriteEvents",
                table: "GatewayAmbiguousWriteEvents",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GatewayAmbiguousWriteCases",
                table: "GatewayAmbiguousWriteCases",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_FolderAcls",
                table: "FolderAcls",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DatasetUserAcls",
                table: "DatasetUserAcls",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DatasetAcls",
                table: "DatasetAcls",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuditOutboxMessages",
                table: "AuditOutboxMessages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUsers",
                table: "AspNetUsers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserTokens",
                table: "AspNetUserTokens",
                columns: new[] { "UserId", "LoginProvider", "Name" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserRoles",
                table: "AspNetUserRoles",
                columns: new[] { "UserId", "RoleId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserLogins",
                table: "AspNetUserLogins",
                columns: new[] { "LoginProvider", "ProviderKey" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUserClaims",
                table: "AspNetUserClaims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetRoles",
                table: "AspNetRoles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetRoleClaims",
                table: "AspNetRoleClaims",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AlertNotifications",
                table: "AlertNotifications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AdminServiceRuns",
                table: "AdminServiceRuns",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_PrincipalKey",
                table: "Groups",
                column: "PrincipalKey",
                unique: true,
                filter: "\"PrincipalKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAcls_ReportId_GroupId",
                table: "ReportAcls",
                columns: new[] { "ReportId", "GroupId" },
                unique: true,
                filter: "\"GroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAcls_ReportId_UserId",
                table: "ReportAcls",
                columns: new[] { "ReportId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAccessRequests_RequesterUserId_ReportId",
                table: "ReportAccessRequests",
                columns: new[] { "RequesterUserId", "ReportId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_PortalExecutionJobs_TenantId_ReportId_Kind",
                table: "PortalExecutionJobs",
                columns: new[] { "TenantId", "ReportId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'Refresh' AND \"Status\" IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PrincipalKey",
                table: "AspNetUsers",
                column: "PrincipalKey",
                unique: true,
                filter: "\"PrincipalKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId_NormalizedUserName",
                table: "AspNetUsers",
                columns: new[] { "TenantId", "NormalizedUserName" },
                unique: true,
                filter: "\"NormalizedUserName\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AlertNotifications_ReportAlerts_AlertId",
                table: "AlertNotifications",
                column: "AlertId",
                principalTable: "ReportAlerts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId",
                principalTable: "AspNetRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                table: "AspNetUserRoles",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                table: "AspNetUserTokens",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditOutboxMessages_AuditLogs_AuditLogId",
                table: "AuditOutboxMessages",
                column: "AuditLogId",
                principalTable: "AuditLogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DatasetAcls_Datasets_DatasetId",
                table: "DatasetAcls",
                column: "DatasetId",
                principalTable: "Datasets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DatasetAcls_Groups_GroupId",
                table: "DatasetAcls",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Datasets_Folders_FolderId",
                table: "Datasets",
                column: "FolderId",
                principalTable: "Folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Datasets_Reports_OwningReportId",
                table: "Datasets",
                column: "OwningReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DatasetUserAcls_AspNetUsers_UserId",
                table: "DatasetUserAcls",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DatasetUserAcls_Datasets_DatasetId",
                table: "DatasetUserAcls",
                column: "DatasetId",
                principalTable: "Datasets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FolderAcls_Folders_FolderId",
                table: "FolderAcls",
                column: "FolderId",
                principalTable: "Folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FolderAcls_Groups_GroupId",
                table: "FolderAcls",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Folders_Folders_ParentId",
                table: "Folders",
                column: "ParentId",
                principalTable: "Folders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_GatewayAmbiguousWriteEvents_GatewayAmbiguousWriteCases_CaseId",
                table: "GatewayAmbiguousWriteEvents",
                column: "CaseId",
                principalTable: "GatewayAmbiguousWriteCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupStudioCapabilities_Groups_GroupId",
                table: "GroupStudioCapabilities",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_AspNetUsers_UserId",
                table: "RefreshTokens",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAccessRequests_AspNetUsers_DecidedByUserId",
                table: "ReportAccessRequests",
                column: "DecidedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAccessRequests_AspNetUsers_RequesterUserId",
                table: "ReportAccessRequests",
                column: "RequesterUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAccessRequests_Reports_ReportId",
                table: "ReportAccessRequests",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAcls_AspNetUsers_UserId",
                table: "ReportAcls",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAcls_Groups_GroupId",
                table: "ReportAcls",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAcls_Reports_ReportId",
                table: "ReportAcls",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAlerts_AspNetUsers_OwnerId",
                table: "ReportAlerts",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportAlerts_Reports_ReportId",
                table: "ReportAlerts",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportEmbedTokens_AspNetUsers_CreatedBy",
                table: "ReportEmbedTokens",
                column: "CreatedBy",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportEmbedTokens_Reports_ReportId",
                table: "ReportEmbedTokens",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportFavorites_AspNetUsers_UserId",
                table: "ReportFavorites",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportFavorites_Reports_ReportId",
                table: "ReportFavorites",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportJobLinks_Reports_ReportId",
                table: "ReportJobLinks",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Folders_FolderId",
                table: "Reports",
                column: "FolderId",
                principalTable: "Folders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportScriptDraftDecisions_ReportScriptDrafts_DraftId",
                table: "ReportScriptDraftDecisions",
                column: "DraftId",
                principalTable: "ReportScriptDrafts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportScriptDrafts_Reports_ReportId",
                table: "ReportScriptDrafts",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportShareLinks_AspNetUsers_CreatedBy",
                table: "ReportShareLinks",
                column: "CreatedBy",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportShareLinks_Reports_ReportId",
                table: "ReportShareLinks",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ReportSnapshots_Reports_ReportId",
                table: "ReportSnapshots",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedReportViews_AspNetUsers_UserId",
                table: "SavedReportViews",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SavedReportViews_Reports_ReportId",
                table: "SavedReportViews",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceAccounts_AspNetUsers_OwnerUserId",
                table: "ServiceAccounts",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SharedConnectionAcls_Groups_GroupId",
                table: "SharedConnectionAcls",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SharedConnectionAcls_PortalSharedConnections_SharedConnectionId",
                table: "SharedConnectionAcls",
                column: "SharedConnectionId",
                principalTable: "PortalSharedConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SharedConnectionUsages_PortalSharedConnections_SharedConnectionId",
                table: "SharedConnectionUsages",
                column: "SharedConnectionId",
                principalTable: "PortalSharedConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StewardshipFindingDecisions_StewardshipFindings_FindingId",
                table: "StewardshipFindingDecisions",
                column: "FindingId",
                principalTable: "StewardshipFindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudioRecoveryDrafts_AspNetUsers_UserId",
                table: "StudioRecoveryDrafts",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudioRecoveryDrafts_Reports_ReportId",
                table: "StudioRecoveryDrafts",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_AspNetUsers_UserId",
                table: "Subscriptions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Reports_ReportId",
                table: "Subscriptions",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserGroups_AspNetUsers_UserId",
                table: "UserGroups",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserGroups_Groups_GroupId",
                table: "UserGroups",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
