/**
 * GENERATED FILE - DO NOT EDIT.
 *
 * The C# types the browser code talks to, as TypeScript declarations. Regenerate with:
 *
 *   node scripts/generate-browser-contracts.mjs
 *
 * Source of truth: the records and enums themselves, read by reflection in
 * tests/ETL-SQL.Portal.Tests/BrowserContractsGenerator.cs. Editing this file by hand
 * makes the browser sources check against a shape the server does not send.
 *
 * Field names are camelCase because the hosts serialise with JsonSerializerDefaults.Web.
 */

/** PipelineTaskKind, as it crosses the wire. Matched case-insensitively on the way in. */
type PipelineTaskKind =
    | 'execution'
    | 'copyfile'
    | 'movefile'
    | 'renamefile'
    | 'deletefile'
    | 'createdirectory'
    | 'deletedirectory'
    | 'deletedirectorycontents'
    | 'renamedirectory'
    | 'movedirectory'
    | 'copydirectory'
    | 'validation'
    | 'notification'
    | 'parallel'
    | 'foreach'
    | 'transaction'
    | 'if'
    | 'for'
    | 'while'
    | 'throw'
    | 'break'
    | 'continue'
    | 'waitfor'
    | 'extract'
    | 'load'
    | 'upsert'
    | 'reshape'
    | 'join'
    | 'summarise'
    | 'expectschema'
    | 'droptemp';

/** PipelineEdgeCondition, as it crosses the wire. Matched case-insensitively on the way in. */
type PipelineEdgeCondition =
    | 'always'
    | 'onsuccess'
    | 'onfailure'
    | 'oncompletion'
    | 'expression';

/** DatasetPermission, as it crosses the wire. Matched case-insensitively on the way in. */
type DatasetPermission =
    | 'Viewer'
    | 'Refresh'
    | 'Editor'
    | 'Owner';

/** DatasetAccessLevel, as it crosses the wire. Matched case-insensitively on the way in. */
type DatasetAccessLevel =
    | 'Private'
    | 'Public';

interface AdminReportDto {
    id: number;
    name: string;
    description?: string | null;
    scriptPath: string;
    folderId: number;
    folderName: string;
    folderPath: string;
    createdAt: string;
    updatedAt: string;
    version: number;
}

interface AdminRoleOptionDto {
    name: string;
}

interface AdminServiceRunSummary {
    outcome: string;
    startedAtUtc: string;
    completedAtUtc?: string | null;
    attempts: number;
    nodeName?: string | null;
    detail?: string | null;
}

interface AdminServiceStatus {
    name: string;
    enabled: boolean;
    intervalHours: number;
    smtpAlias?: string | null;
    recipients: string;
    lastRun?: AdminServiceRunSummary | null;
}

interface AnonymousReportAccessDto {
    type: string;
    id: number;
    reportId: number;
    reportName: string;
    folderPath: string;
    name?: string | null;
    createdBy: number;
    creatorUsername?: string | null;
    creatorIsActive: boolean;
    createdAt: string;
    expiresAt?: string | null;
    revokedAt?: string | null;
    status: string;
}

interface ApproveReportAccessRequestDto {
    permission?: 0 | 1 | 2 | 3 | null;
    decisionReason?: string | null;
}

interface AssignBadgeRequest {
    assetKey: string;
    badge: string;
    assetVersion?: string | null;
    reason?: string | null;
}

interface AssignCaseRequest {
    version: number;
    owner: string;
    note?: string | null;
}

interface AuditLog {
    id: number;
    tenantId: string;
    userId?: number | null;
    actorType: string;
    actorId?: string | null;
    effectiveScopes?: string | null;
    action: string;
    resourceType?: string | null;
    resourceId?: string | null;
    timestamp: string;
    detail?: string | null;
    correlationId?: string | null;
    studioCapability?: string | null;
    outboxMessages: unknown[];
}

interface AuditLogDto {
    id: number;
    userId?: number | null;
    username?: string | null;
    action: string;
    resourceType?: string | null;
    resourceId?: string | null;
    timestamp: string;
    detail?: string | null;
    correlationId?: string | null;
    studioCapability?: string | null;
}

interface AuthProvidersResponse {
    local: boolean;
    oidcEnabled: boolean;
    oidcLoginUrl?: string | null;
}

interface BulkAddGroupMembersResponse {
    groupId: number;
    added: number;
    version: number;
    changedUserIds: number[];
    skippedUserIds: number[];
}

interface BulkDeleteGroupsRequest {
    groups?: VersionedResourceRequest[] | null;
    cascade: boolean;
    groupIds: number[];
}

interface BulkDeleteGroupsResponse {
    deleted: number;
    results: BulkMutationResult[];
}

interface BulkGroupMembershipRequest {
    userIds: number[];
}

interface BulkMutationResult {
    id: number;
    status: string;
    currentVersion?: number | null;
    error?: string | null;
}

interface BulkRemoveGroupMembersResponse {
    groupId: number;
    removed: number;
    version: number;
    changedUserIds: number[];
    skippedUserIds: number[];
}

interface BulkSubscriptionStatusRequest {
    subscriptions?: VersionedResourceRequest[] | null;
    isActive: boolean;
    subscriptionIds: number[];
}

interface BulkSubscriptionStatusResponse {
    updated: number;
    results: BulkMutationResult[];
}

interface BulkUserStatusResponse {
    updated: number;
    results: BulkMutationResult[];
}

interface CaseNoteRequest {
    version: number;
    note?: string | null;
}

interface CatalogLineageHistoryDto {
    id: number;
    runAt: string;
    jobName?: string | null;
    scriptPath?: string | null;
    targetTable: string;
    targetColumn?: string | null;
    sourceTables: string[];
    operation: string;
    tags: Record<string, string>;
    sourceFile?: string | null;
    line: number;
    sourceColumns: string[];
    transformationKind?: string | null;
    transformationExpression?: string | null;
    functionsApplied?: string[] | null;
    derivedFromDescriptions?: string | null;
    reportId?: number | null;
    reportName?: string | null;
    folderPath?: string | null;
}

interface ConnectionHelpDto {
    content: string;
}

interface ConnectorOptionDescriptor {
    name: string;
    type: 0 | 1 | 2 | 3 | 4 | 5;
    isMandatory: boolean;
    defaultValue?: string | null;
    category: string;
    description?: string | null;
    allowedValues?: string[] | null;
    mutuallyExclusiveGroup?: string | null;
}

interface ConnectorSchemaDescriptor {
    connectorType: string;
    aliases: string[];
    description: string;
    isFileBased: boolean;
    isDataWarehouse: boolean;
    commandTimeoutSeconds: number;
    options: ConnectorOptionDescriptor[];
}

interface ControlPlaneFleetOverview {
    totalTenants: number;
    activeTenants: number;
    provisioningTenants: number;
    maintenanceTenants: number;
    quarantinedTenants: number;
    deletingTenants: number;
    activeExecutions: number;
    queuedExecutions: number;
    connectedGateways: number;
    uniqueGatewayTenants: number;
    auditOutboxPending: number;
    auditOutboxFailed: number;
    environment: string;
    capturedAtUtc: string;
}

interface ControlPlaneTenantDto {
    tenantId: string;
    state: string;
    activeRelease: string;
    maxConcurrentJobs: number;
    maxStorageMb: number;
    maxReportSessions: number;
    fenceEpoch: number;
    createdAtUtc: string;
    updatedAtUtc: string;
    deletedAtUtc?: string | null;
    activeExecutions: number;
    queuedExecutions: number;
    connectedGateways: number;
    quotaUtilizationPercentage: number;
}

interface CreateFolderRequest {
    name: string;
    parentId?: number | null;
    ownerUsername?: string | null;
}

interface CreateGroupRequest {
    name: string;
    description?: string | null;
    provider?: string | null;
    adGroup?: string | null;
}

interface CreateNotificationRequest {
    name: string;
    connectionName: string;
    recipient?: string | null;
    isEnabled: boolean;
    displayName?: string | null;
    description?: string | null;
    options?: Record<string, string> | null;
}

interface CreateScheduleRequest {
    name: string;
    cron: string;
    timeZone?: string | null;
    isEnabled: boolean;
    displayName?: string | null;
    description?: string | null;
    options?: Record<string, string> | null;
}

interface CreateServiceAccountRequest {
    name: string;
    description?: string | null;
    ownerUserId: number;
    scopes: string[];
    roles: string[];
    expiresAt?: string | null;
    studioCapabilities?: string[] | null;
}

interface CreateStudioReportRequest {
    folderId: number;
    name: string;
    scriptText: string;
    description?: string | null;
    kind?: string | null;
}

interface CreateSubscriptionRequest {
    reportId: number;
    name?: string | null;
    schedule?: string | null;
    format: string;
    smtpAlias?: string | null;
    recipientEmail?: string | null;
    atTime?: string | null;
    parameters?: Record<string, string> | null;
}

interface CreateUserRequest {
    username: string;
    email: string;
    password?: string | null;
    role: string;
    firstName?: string | null;
    lastName?: string | null;
    provider?: string | null;
}

interface DataModelColumnDto {
    name: string;
    type?: string | null;
    isKey: boolean;
}

interface DataModelEntityDto {
    id: string;
    name: string;
    kind: string;
    connection?: string | null;
    line: number;
    detail?: string | null;
    columns: DataModelColumnDto[];
}

interface DataModelRelationshipDto {
    id: string;
    from: string;
    to: string;
    kind: string;
    cardinality: string;
    evidence: string;
    fromColumn?: string | null;
    toColumn?: string | null;
    joinType?: string | null;
    line: number;
}

interface DataModelRequest {
    script?: string | null;
    documentUri?: string | null;
}

interface DataModelResponse {
    parsed: boolean;
    error?: string | null;
    hasSchemaEvidence: boolean;
    entities: DataModelEntityDto[];
    relationships: DataModelRelationshipDto[];
}

interface DataQualityJobDto {
    name: string;
    displayName: string;
    script: string;
    description?: string | null;
}

interface DataQualityRuleDefinitionDto {
    targetTable: string;
    targetColumn?: string | null;
    ruleClause: string;
    rule: string;
    action: string;
    sourceFile?: string | null;
    line: number;
    jobName?: string | null;
}

interface DataQualityRuleFailureDto {
    column: string;
    rule: string;
    count: number;
    targetTable?: string | null;
    action?: string | null;
    owner?: string | null;
    countsOnly: boolean;
}

interface DataQualityRunDto {
    historyId: number;
    jobName: string;
    startTime: string;
    endTime?: string | null;
    status: string;
    rowsProcessed: number;
    rowsQuarantined: number;
    rowsWarned: number;
    quarantineRate?: number | null;
    warnRate?: number | null;
    ruleFailures: DataQualityRuleFailureDto[];
}

interface DataQualityTrendDto {
    jobName: string;
    runCount: number;
    totalRowsProcessed: number;
    totalRowsQuarantined: number;
    totalRowsWarned: number;
    averageQuarantineRate?: number | null;
    latestQuarantineRate?: number | null;
    quarantineRateDelta?: number | null;
    topRuleFailures: DataQualityRuleFailureDto[];
    runs: DataQualityRunDto[];
}

interface DatasetAclEntryDto {
    groupId: number;
    groupName: string;
    permission: string;
    principalKind: string;
    userId?: number | null;
    userName?: string | null;
}

interface DatasetColumnDto {
    name: string;
    type: string;
}

interface DatasetColumnFilterDto {
    col: string;
    op: string;
    val?: string | null;
    val2?: string | null;
}

interface DatasetColumnStatsDto {
    name: string;
    nullCount: number;
    min?: unknown;
    max?: unknown;
    avg?: number | null;
}

interface DatasetColumnValuesDto {
    values: (unknown)[];
    totalDistinct: number;
}

interface DatasetDto {
    id: number;
    name: string;
    folderPath: string;
    accessLevel: string;
    rowCount: number;
    isStale: boolean;
    lastRefresh?: string | null;
    ttl?: string | null;
    refreshInterval?: string | null;
    isEncrypted: boolean;
    createdAt: string;
    updatedAt: string;
    owningReportName?: string | null;
    owningReportId?: number | null;
    version: number;
}

interface DatasetRefreshAcceptedDto {
    triggered: boolean;
    jobId?: string | null;
    message?: string | null;
}

interface DatasetRowsDto {
    columns: DatasetColumnDto[];
    rows: Record<string, unknown>[];
    totalCount: number;
    filteredCount: number;
    page: number;
    pageSize: number;
}

interface DatasetVersionResponse {
    version: number;
}

interface DecideFindingRequest {
    decision: string;
    categoryValue?: string | null;
    reason: string;
    assetVersion: string;
}

interface DenyReportAccessRequestDto {
    decisionReason?: string | null;
}

interface DesignerBookmarkDto {
    id: string;
    name: string;
    title?: string | null;
    page?: string | null;
    isDefault: boolean;
    parameters?: DesignerBookmarkParameterDto[] | null;
    state?: DesignerBookmarkStateDto[] | null;
}

interface DesignerBookmarkParameterDto {
    name: string;
    value: string;
}

interface DesignerBookmarkStateDto {
    objectName: string;
    property: string;
    on: boolean;
}

interface DesignerConditionalFormattingRuleDto {
    condition: string;
    backgroundColor: string;
    fontColor?: string | null;
}

interface DesignerConnectionDto {
    name: string;
    text: string;
}

interface DesignerDatasetDto {
    id: string;
    name: string;
    query: string;
    ttl?: string | null;
}

interface DesignerFieldFormattingDto {
    format?: string | null;
    align?: string | null;
    displayName?: string | null;
    dataBar: boolean;
    dataBarColor?: string | null;
    colorScaleFrom?: string | null;
    colorScaleTo?: string | null;
}

interface DesignerLeaseResponse {
    acquired: boolean;
    owner: string;
    expiresAt: string;
}

interface DesignerPageDto {
    id: string;
    name: string;
    mode: string;
    visuals: DesignerVisualDto[];
    printLayout?: DesignerPageLayoutDto | null;
}

interface DesignerPageLayoutDto {
    pageSize?: string | null;
    orientation?: string | null;
    marginTop?: number | null;
    marginRight?: number | null;
    marginBottom?: number | null;
    marginLeft?: number | null;
    units?: string | null;
    overflow?: string | null;
    customWidth?: number | null;
    customHeight?: number | null;
}

interface DesignerParameterDto {
    name: string;
    dataType: string;
    initialValue?: string | null;
    isInput: boolean;
    isOutput: boolean;
    isRequired: boolean;
    isSensitive: boolean;
    isBlockScoped: boolean;
}

interface DesignerReportStyleDto {
    theme?: string | null;
    accent?: string | null;
    background?: string | null;
    surface?: string | null;
    text?: string | null;
}

interface DesignerSnapshotMetadata {
    isSampled: boolean;
    rlsEnforced: boolean;
    totalRows: number;
    returnedRows: number;
}

interface DesignerSnapshotResponse {
    reportName: string;
    builtAt: string;
    sampleRows: Record<string, (string | null)[][]>;
    columns: Record<string, string[]>;
    metadata: DesignerSnapshotMetadata;
}

interface DesignerStateDto {
    pages: DesignerPageDto[];
    datasets: DesignerDatasetDto[];
    reportStyle?: DesignerReportStyleDto | null;
    bookmarks?: DesignerBookmarkDto[] | null;
    parameters?: DesignerParameterDto[] | null;
    connections?: DesignerConnectionDto[] | null;
}

interface DesignerTextFormattingDto {
    text?: string | null;
    color?: string | null;
    font?: string | null;
    size?: string | null;
    weight?: string | null;
    align?: string | null;
}

interface DesignerVisualDto {
    id: string;
    name: string;
    type: string;
    gridCol: number;
    gridRow: number;
    gridColSpan: number;
    gridRowSpan: number;
    title?: string | null;
    dataset?: string | null;
    mappings: Record<string, string>;
    options: Record<string, string>;
    containerId?: string | null;
    formatting?: DesignerVisualFormattingDto | null;
}

interface DesignerVisualFormattingDto {
    title?: DesignerTextFormattingDto | null;
    subtitle?: DesignerTextFormattingDto | null;
    xAxis?: Record<string, string> | null;
    yAxis?: Record<string, string> | null;
    palette?: string[] | null;
    conditionalRules?: DesignerConditionalFormattingRuleDto[] | null;
    fields?: Record<string, DesignerFieldFormattingDto> | null;
}

interface DiagnosticReport {
    connection: string;
    connectorType: string;
    steps: DiagnosticStep[];
    succeeded: boolean;
}

interface DiagnosticStep {
    layer: string;
    status: "Ok" | "Failed" | "Skipped" | "Denied";
    detail: string;
    remedy?: string | null;
}

interface DiscoveredGatewayDto {
    id: string;
    name: string;
    isOnline: boolean;
    activeNodes: number;
    totalNodes: number;
    region?: string | null;
    status?: string | null;
    resources?: GatewayDiscoveredResourceDto[] | null;
}

interface DocumentationDocument {
    path: string;
    title: string;
    markdown: string;
}

interface DocumentationSearchResult {
    path: string;
    title: string;
    section: string;
    snippet: string;
    score: number;
}

interface DownstreamReportDto {
    reportId?: number | null;
    reportName?: string | null;
    folderPath?: string | null;
    runCount: number;
    lastSeen: string;
}

interface EvidenceRequest {
    version: number;
    note?: string | null;
    evidenceReference?: string | null;
}

interface FleetCompatibilityMetadata {
    metadataVersion: string;
    compatibilityWindow: string;
    rollingUpgradeSequence: string[];
    components: FleetComponentCompatibility[];
}

interface FleetComponentCompatibility {
    component: string;
    version: string;
    contract: string;
    minimumCompatibleVersion: string;
    requiredForUpgrade: boolean;
    status: string;
}

interface FleetEnvironmentStatus {
    environment: string;
    status: string;
    queueDepth: number;
    activeExecutions: number;
    failedRefreshes: number;
    auditOutboxPending: number;
    auditOutboxFailed: number;
    storage: string;
    capturedAtUtc: string;
    securityEvents?: SecurityEventDiagnostics | null;
    inventory?: FleetNodeInventory | null;
}

interface FleetMigrationState {
    state: string;
    ownerNodeId?: string | null;
    provider?: string | null;
    lockKind?: string | null;
    lockKey?: number | null;
    startedAtUtc?: string | null;
    acquiredAtUtc?: string | null;
    completedAtUtc?: string | null;
    updatedAtUtc: string;
    pendingMigrations?: number | null;
    error?: string | null;
}

interface FleetNodeInventory {
    environment: string;
    nodeId: string;
    installedVersion: string;
    schemaVersions: FleetSchemaVersions;
    policy: FleetPolicyInventory;
    providers: FleetRuntimeProviders;
    configurationFingerprint: string;
    upgradeReadiness: FleetUpgradeReadiness;
    compatibility?: FleetCompatibilityMetadata | null;
    migration?: FleetMigrationState | null;
}

interface FleetPolicyInventory {
    isEnrolled: boolean;
    isAvailable: boolean;
    status: string;
    policyVersion?: string | null;
    policyHash?: string | null;
    issuedAtUtc?: string | null;
    expiresAtUtc?: string | null;
    lastLoadedAtUtc?: string | null;
    policySigningKeyConfigured: boolean;
    clientCertificateConfigured: boolean;
    clientCertificateExpiresAtUtc?: string | null;
    governedKeyCount: number;
}

interface FleetRuntimeProviders {
    portalDatabase: string;
    artifactStorage: string;
}

interface FleetSchemaVersions {
    enrollment: string;
    policyEnvelope: string;
    policyPayload: string;
    securityEvent: number;
    lastAppliedPortalMigration?: string | null;
    appliedPortalMigrations: number;
    pendingPortalMigrations: number;
}

interface FleetUpgradeReadiness {
    ready: boolean;
    findings: string[];
}

interface FolderAclCatalogResponse {
    folderId: number;
    folderName: string;
    version: number;
    items: FolderAclDto[];
    permissions: FolderPermissionOptionDto[];
}

interface FolderAclDto {
    groupId: number;
    groupName: string;
    permission: 0 | 1 | 2 | 3;
}

interface FolderAclGrantResponse {
    folderId: number;
    groupId: number;
    permission: 0 | 1 | 2 | 3;
    version: number;
}

interface FolderAclRevokeResponse {
    folderId: number;
    groupId: number;
    version: number;
}

interface FolderDto {
    id: number;
    parentId?: number | null;
    name: string;
    path: string;
    children: FolderDto[];
    version: number;
}

interface FolderPermissionOptionDto {
    permission: 0 | 1 | 2 | 3;
    name: string;
    rank: number;
}

interface GatewayAmbiguousWriteCaseDto {
    id: number;
    operationId: string;
    tenantId: string;
    gatewayId: string;
    resourceId: string;
    correlationId: string;
    executedAtUtc: string;
    state: string;
    priority: string;
    owner?: string | null;
    resolution?: string | null;
    createdAtUtc: string;
    updatedAtUtc: string;
    version: number;
    events: GatewayAmbiguousWriteEventDto[];
}

interface GatewayAmbiguousWriteEventDto {
    id: number;
    eventType: string;
    actor: string;
    note?: string | null;
    evidenceReference?: string | null;
    resolution?: string | null;
    createdAtUtc: string;
}

interface GatewayDiscoveredResourceDto {
    resourceId: string;
    connectorType: string;
    allowedOperations: string;
    state: string;
    isOnline: boolean;
    lastSeenUtc?: string | null;
}

interface GatewayEnrollmentDto {
    enrollmentId: string;
    gatewayId: string;
    state: "Pending" | "Consumed" | "Revoked";
    createdUtc: string;
    expiresUtc: string;
    consumedUtc?: string | null;
    workloadPublicKeyThumbprint?: string | null;
    isOnline: boolean;
    activeNodes: number;
    totalNodes: number;
    nodes?: GatewaySessionInfo[] | null;
    publishedResources?: GatewayDiscoveredResourceDto[] | null;
}

interface GatewayResourceBinding {
    gatewayId: string;
    resourceId: string;
}

interface GatewayRevocationDto {
    success: boolean;
    gatewayId: string;
}

interface GatewaySessionInfo {
    tenantId: string;
    gatewayId: string;
    nodeId: string;
    workloadPublicKeyThumbprint: string;
    connectedUtc: string;
    isActive: boolean;
}

interface GovernanceAssetDto {
    assetKey: string;
    assetVersion: string;
    scriptPath?: string | null;
    owner?: string | null;
    steward?: string | null;
    domain?: string | null;
    classification?: string | null;
    score: number;
    governed: boolean;
    deductions: GovernanceDeductionDto[];
    automaticBadges: string[];
    assignedBadges: string[];
    reviewedAtUtc?: string | null;
    reviewedVersion?: string | null;
    findings: StewardshipFindingDto[];
}

interface GovernanceAssetReviewDto {
    assetKey: string;
    assetVersion: string;
    reviewedAtUtc: string;
}

interface GovernanceBadgeAssignmentDto {
    assetKey: string;
    badge: string;
    assignedAtUtc?: string | null;
}

interface GovernanceCategoryDto {
    id: number;
    value: string;
    label: string;
    color: string;
    expiryDays?: number | null;
    disabled: boolean;
}

interface GovernanceDashboardDto {
    summary: GovernanceSummaryDto;
    assets: GovernanceAssetDto[];
    lastScan?: StewardshipScanDto | null;
}

interface GovernanceDecisionDto {
    id: number;
    decision: string;
    categoryValue?: string | null;
    reason: string;
    assetVersion?: string | null;
    decidedAtUtc: string;
    decidedBy?: string | null;
}

interface GovernanceDeductionDto {
    ruleKey: string;
    points: number;
    reason: string;
}

interface GovernanceSummaryDto {
    totalAssets: number;
    governedAssets: number;
    belowThreshold: number;
    openFindings: number;
    ignoredFindings: number;
    acceptedRisks: number;
    targetScore: number;
}

interface GrantPermissionRequest {
    groupId: number;
    permission: 0 | 1 | 2 | 3;
}

interface GroupDto {
    id: number;
    name: string;
    description?: string | null;
    memberCount: number;
    provider?: string | null;
    adGroup?: string | null;
    version: number;
}

interface GroupMemberCatalogResponse {
    groupId: number;
    version: number;
    items: GroupMemberDto[];
    total: number;
    page: number;
    pageSize: number;
}

interface GroupMemberDto {
    id: number;
    username: string;
    email?: string | null;
    isActive: boolean;
}

interface GroupMembershipResponse {
    groupId: number;
    userId: number;
    version: number;
}

interface HourlyExecutionLoad {
    hourUtc: string;
    executions: number;
    failures: number;
    rowsProcessed: number;
    peakMemoryBytes: number;
}

interface ImpactConsumer {
    type: string;
    name: string;
    detail?: string | null;
    lastUsedAtUtc?: string | null;
    useCount?: number | null;
}

interface ImpactReport {
    reference: string;
    consumerCount: number;
    consumers: ImpactConsumer[];
}

interface IssueEnrollmentRequest {
    gatewayId: string;
    expirationMinutes?: number | null;
}

interface IssuedGatewayEnrollmentDto {
    enrollmentId: string;
    gatewayId: string;
    tenantId: string;
    oneTimeToken: string;
    expiresUtc: string;
}

interface JobDataQualityFailure {
    runId: number;
    jobName: string;
    startTime: string;
    endTime?: string | null;
    status: string;
    targetTable?: string | null;
    columnName: string;
    rule: string;
    action: string;
    failureCount: number;
    owner?: string | null;
}

interface JobDataQualityStatus {
    runId: string;
    jobName?: string | null;
    startTime: string;
    endTime?: string | null;
    status: string;
    rowsProcessed: number;
    rowsWarned: number;
    rowsQuarantined: number;
    failedRuleCount: number;
    freshestValueUtc?: string | null;
    freshnessState: string;
    errorSummary?: string | null;
}

interface JobDefinition {
    name: string;
    script: string;
    interval: number;
    unit: string;
    atTime?: string | null;
    lastRun?: string | null;
    nextRun?: string | null;
    isEnabled: boolean;
    maxRetries: number;
    retryDelaySeconds: number;
    scriptHash?: string | null;
    hashPolicy: string;
    version: number;
    jobType: 0 | 1;
    targetPath?: string | null;
    displayName?: string | null;
    description?: string | null;
    options?: string | null;
    createdBy?: string | null;
    modifiedBy?: string | null;
    tenantId?: string | null;
    id: string | null;
}

interface JobDefinitionDto {
    name: string;
    script: string;
    interval: number;
    unit: string;
    atTime?: string | null;
    lastRun?: string | null;
    nextRun?: string | null;
    isEnabled: boolean;
    maxRetries: number;
    retryDelaySeconds: number;
    scriptHash?: string | null;
    hashPolicy: string;
    version: number;
    tenantId?: string | null;
    createdBy?: string | null;
    modifiedBy?: string | null;
    displayName?: string | null;
    description?: string | null;
    options?: string | null;
    jobType: 0 | 1;
    targetPath?: string | null;
    id: string | null;
}

interface JobDependencyChainDto {
    jobName: string;
    nodes: JobDependencyNodeDto[];
    edges: JobDependencyEdgeDto[];
    upstream: string[];
    downstream: string[];
    sharedSchedules: string[];
}

interface JobDependencyEdgeDto {
    from: string;
    to: string;
    type: string;
    detail: string;
}

interface JobDependencyNodeDto {
    id: string;
    name: string;
    displayName?: string | null;
    isEnabled: boolean;
    lastRun?: string | null;
    nextRun?: string | null;
    isCurrent: boolean;
}

interface JobHistoryEntry {
    id: number;
    jobName: string;
    startTime: string;
    endTime?: string | null;
    status: string;
    errorMessage?: string | null;
    rowsProcessed: number;
    peakMemoryBytes: number;
    cpuTimeSeconds: number;
    scriptHashAtRunTime?: string | null;
    hashMatched?: boolean | null;
    rowsQuarantined: number;
    rowsWarned: number;
    dataQualityFailures?: string | null;
    checkpointLabel?: string | null;
    jobId: string | null;
    tenantId?: string | null;
    hasResumeSession: boolean;
}

interface JobHistoryEntryDto {
    id: number;
    jobName: string;
    startTime: string;
    endTime?: string | null;
    status: string;
    errorMessage?: string | null;
    rowsProcessed: number;
    peakMemoryBytes: number;
    cpuTimeSeconds: number;
    scriptHashAtRunTime?: string | null;
    hashMatched?: boolean | null;
    rowsQuarantined: number;
    rowsWarned: number;
    dataQualityFailures?: string | null;
    checkpointLabel?: string | null;
    hasResumeSession: boolean;
}

interface JobNotificationLinkDto {
    jobId: string | null;
    notificationId: string | null;
    trigger: string;
    jobName?: string | null;
    notificationName?: string | null;
}

interface JobScheduleLinkDto {
    jobId: string | null;
    scheduleId: string | null;
    lastRun?: string | null;
    nextRun?: string | null;
    jobName?: string | null;
    scheduleName?: string | null;
}

interface JobStateEntryDto {
    jobName: string;
    stateKey: string;
    stateValue?: string | null;
    updatedAt: string;
}

interface LineageImpactDto {
    request: LineageImpactRequestDto;
    summary: LineageImpactSummaryDto;
    tables: LineageImpactItemDto[];
    columns: LineageImpactItemDto[];
    reports: LineageImpactItemDto[];
    datasets: LineageImpactItemDto[];
    subscriptions: LineageImpactItemDto[];
    jobs: LineageImpactItemDto[];
    alerts: LineageImpactItemDto[];
    stewards: LineageImpactItemDto[];
}

interface LineageImpactItemDto {
    type: string;
    name: string;
    detail?: string | null;
    lastSeen?: string | null;
    count?: number | null;
}

interface LineageImpactRequestDto {
    kind: string;
    name: string;
    column?: string | null;
    direction: string;
    depth: number;
    limit: number;
}

interface LineageImpactSummaryDto {
    tables: number;
    columns: number;
    reports: number;
    datasets: number;
    subscriptions: number;
    jobs: number;
    alerts: number;
    stewards: number;
}

interface LoginResponse {
    token: string;
    refreshToken: string;
    expiresAt: string;
}

interface NavDestinationDto {
    id: string;
    visible: boolean;
    reason?: string | null;
}

interface NotificationDefinitionDto {
    name: string;
    connectionName: string;
    recipient?: string | null;
    isEnabled: boolean;
    displayName?: string | null;
    description?: string | null;
    options?: string | null;
    createdBy?: string | null;
    modifiedBy?: string | null;
    version: number;
    tenantId?: string | null;
    id: string | null;
}

interface NotificationDispatchResult {
    notificationName: string;
    delivered: boolean;
    skipped: boolean;
    message?: string | null;
}

interface ObjectGrantResponse {
    objectId: string;
    objectKind: string;
    principalKind: string;
    principalId: string;
    permission: string;
    grantedBy: string;
    version: number;
}

interface OperationalMetrics {
    activeExecutions: number;
    queuedExecutions: number;
    executionCap: number;
    perUserExecutionCap: number;
    topology: string;
    nodeId: string;
    affinityCookieName: string;
    recentExecutions: number;
    recentExecutionFailures: number;
    recentDeliveries: number;
    recentDeliveryFailures: number;
    datasetStorageBytes: number;
    snapshotStorageBytes: number;
    storageUsageSampleStale: boolean;
    storageUsageLastSuccessfulSampleUtc?: string | null;
    storageUsageLastFailureUtc?: string | null;
    staleSnapshots: number;
    staleDatasets: number;
    activePolicyVersionsExpiring: number;
    activePolicyVersionsExpired: number;
    activeSubscriptions: number;
    smtpConnections: number;
    appliedMigrations: number;
    pendingMigrations: number;
    lastAppliedMigration?: string | null;
    schemaUpToDate: boolean;
    auditOutboxPending: number;
    auditOutboxFailed: number;
    auditOutboxPendingBytes: number;
    auditOutboxOldestPendingAgeSeconds: number;
    securityEventPending: number;
    securityEventFailed: number;
    securityEventStoredBytes: number;
    securityEventDropped: number;
    securityEventOldestPendingAgeSeconds: number;
    securityEventCollectorConfigured: boolean;
    securityEventCollectorReachable?: boolean | null;
    databaseConnectivityHealthy: boolean;
    databasePoolExhaustionSuspected: boolean;
    policyAuthorityHealthy: boolean;
    clientCertificateExpiresAtUtc?: string | null;
    unhealthyFleetNodes: number;
    averageExecutionDurationMs: number;
    averageQueuedExecutionAgeSeconds: number;
    hourlyExecutionLoad: HourlyExecutionLoad[];
    generatedAt: string;
    windowHours: number;
}

interface OrchestratorConnectionStatusDto {
    online: boolean;
    status?: unknown;
}

interface OrchestratorNotificationDispatchRequest {
    sourceKind: string;
    title: string;
    text: string;
    trigger?: string | null;
    status?: string | null;
    jobName?: string | null;
    alertName?: string | null;
    reportId?: string | null;
    historyId?: number | null;
    rowsProcessed?: number | null;
    recipientOverride?: string | null;
    errorMessage?: string | null;
    attachmentPaths?: string[] | null;
}

interface OrchestratorSettingsDto {
    apiUrl?: string | null;
    hasApiKey: boolean;
}

interface OrchestratorStatusDto {
    status: string;
    uptimeSeconds: number;
    processId: number;
    startedAt: string;
    version: string;
}

interface PagedResult<T> {
    items: T[];
    total: number;
    page: number;
    pageSize: number;
}

interface ParseConnectionStringRequest {
    connectionString?: string | null;
    hintProvider?: string | null;
}

interface ParseDesignerResponse {
    designState: DesignerStateDto;
    error?: string | null;
}

interface ParsedConnectionStringResult {
    detectedProvider?: string | null;
    options: Record<string, string>;
    extractedCredential?: string | null;
    suggestedSecretKey?: string | null;
}

interface PendingAccessRequestDto {
    id: number;
    reportId: number;
    reportTitle: string;
    requesterUserId: number;
    requesterUserName: string;
    requesterEmail?: string | null;
    reason?: string | null;
    status: string;
    createdAt: string;
}

interface PipelineDependencyDto {
    id: string;
    condition: PipelineEdgeCondition;
    expression?: string | null;
}

interface PipelineRunCancelRequestedDto {
    runId: number;
    pipelineId: number;
}

interface PipelineRunDto {
    runId: number;
    pipelineId: number;
    status: string;
    isTerminal: boolean;
    startTime: string;
    endTime?: string | null;
    errorMessage?: string | null;
    rowsProcessed: number;
    scriptHash?: string | null;
    logTruncated: boolean;
    statements: PipelineRunStatementDto[];
}

interface PipelineRunPlanRequest {
    script?: string | null;
    id?: string | null;
}

interface PipelineRunRefusalDto {
    code: string;
    error: string;
    currentVersion?: number | null;
}

interface PipelineRunStatementDto {
    statement: string;
    durationMs: number;
    rowsProcessed: number;
    failed: boolean;
}

interface PipelineRunSubmittedDto {
    runId: number;
    pipelineId: number;
    version: number;
    scriptHash: string;
    jobName: string;
}

interface PipelineScheduleDto {
    pipelineId: number;
    scheduled: boolean;
    cron?: string | null;
    timeZone?: string | null;
    enabled: boolean;
    nextRun?: string | null;
    lastRun?: string | null;
    scheduledScriptHash?: string | null;
    scheduledRevisionIsSaved: boolean;
    savedVersion: number;
}

interface PipelineScopeRequest {
    script?: string | null;
    id?: string | null;
    line?: number | null;
}

interface PipelineTaskDto {
    id: string;
    kind: PipelineTaskKind;
    connection: string;
    body: string;
    line: number;
    dependsOn: PipelineDependencyDto[];
    guarded: boolean;
    container?: string | null;
    variable?: string | null;
    collection?: string | null;
    endLine: number;
    hasElse: boolean;
    fields?: Record<string, string> | null;
}

interface PipelineTaskResponse {
    applied: boolean;
    script: string;
    error?: string | null;
    tasks: PipelineTaskDto[];
    preview?: string | null;
}

interface PlatformAuditReceiptDto {
    operationId: string;
    tenantId: string;
    kind: string;
    status: string;
    phase: string;
    platformOperator: string;
    authorizationReference: string;
    reason: string;
    authorizationExpiresUtc: string;
    targetRelease?: string | null;
    targetMaxConcurrentJobs?: number | null;
    targetMaxStorageMb?: number | null;
    targetMaxReportSessions?: number | null;
    receiptHash: string;
}

interface PolicyActivateRequest {
    tenant: string;
    environment: string;
    policyVersion: string;
}

interface PolicyAuthorityStatusDto {
    configured: boolean;
    signingPublicKeyPem?: string | null;
    error?: string | null;
}

interface PolicyCanaryHaltRequest {
    tenant: string;
    environment: string;
    policyVersion: string;
    reviewer?: string | null;
}

interface PolicyCanaryPromoteRequest {
    tenant: string;
    environment: string;
    policyVersion: string;
}

interface PolicyCanaryPublishRequest {
    tenant: string;
    environment: string;
    policyVersion: string;
    policyJson: string;
    reviewer?: string | null;
    expiresAtUtc: string;
    canaryGroup?: string | null;
    canaryPercentage?: number | null;
}

interface PolicyMachineDto {
    machineId: string;
    enrollmentId: string;
    tenant: string;
    environment: string;
    requiresClientCertificate: boolean;
    revoked: boolean;
    revokedAtUtc?: string | null;
    revokedReason?: string | null;
    registeredAtUtc: string;
    lastSeenAtUtc?: string | null;
    canaryGroup?: string | null;
}

interface PolicyMachineRegisterRequest {
    machineId: string;
    enrollmentId: string;
    tenant: string;
    environment: string;
    clientCertificateThumbprint?: string | null;
    canaryGroup?: string | null;
}

interface PolicyMachineRevokeRequest {
    reason?: string | null;
}

interface PolicyPublishRequest {
    tenant: string;
    environment: string;
    policyVersion: string;
    policyJson: string;
    reviewer?: string | null;
    expiresAtUtc: string;
    staged: boolean;
}

interface PolicyRollbackRequest {
    tenant: string;
    environment: string;
    targetPolicyVersion: string;
    newPolicyVersion: string;
    reviewer?: string | null;
    expiresAtUtc: string;
}

interface PolicyValidateRequest {
    policyJson: string;
}

interface PolicyValidationDto {
    isValid: boolean;
    errors: string[];
}

interface PolicyVersionDto {
    tenant: string;
    environment: string;
    policyVersion: string;
    policyHash: string;
    issuedAtUtc: string;
    expiresAtUtc: string;
    author: string;
    reviewer?: string | null;
    supersededVersion?: string | null;
    rolloutState: string;
    publishedAtUtc: string;
    canaryGroup?: string | null;
    canaryPercentage?: number | null;
}

interface PortalBrandingDto {
    displayName?: string | null;
    footerText?: string | null;
    logoUrl?: string | null;
}

interface PortalSecretSummary {
    name: string;
    disabled: boolean;
    createdAtUtc: string;
    updatedAtUtc: string;
    createdByUserId?: number | null;
    updatedByUserId?: number | null;
    version: number;
}

interface PortalSharedConnectionDetail {
    summary: PortalSharedConnectionSummary;
    target?: string | null;
    options: Record<string, string>;
    sensitiveFields: string[];
    gateway?: GatewayResourceBinding | null;
}

interface PortalSharedConnectionExport {
    alias: string;
    connectorType: string;
    target?: string | null;
    options: Record<string, string>;
    environmentScope?: string | null;
    disabled: boolean;
    sensitiveFields?: string[] | null;
    gateway?: GatewayResourceBinding | null;
}

interface PortalSharedConnectionSummary {
    alias: string;
    connectorType: string;
    disabled: boolean;
    environmentScope?: string | null;
    createdAtUtc: string;
    updatedAtUtc: string;
    lastUsedAtUtc?: string | null;
    lastVerifiedAtUtc?: string | null;
    version: number;
    gateway?: GatewayResourceBinding | null;
}

interface PreviewAsRequest {
    label?: string | null;
    groups?: string[] | null;
    roles?: string[] | null;
}

interface ProtectedDataSuggestionEntry {
    id: number;
    runAt: string;
    jobName?: string | null;
    scriptPath?: string | null;
    targetTable: string;
    targetColumn?: string | null;
    sourceTables: string[];
    sourceColumns: string[];
    suggestedTag: string;
    suggestedValue: string;
    confidence: number;
    evidenceKind: string;
    evidence: string;
    reason: string;
    existingTags: Record<string, string>;
    sourceFile?: string | null;
    line: number;
}

interface ProtectedLineageHistoryEntry {
    id: number;
    runAt: string;
    jobName?: string | null;
    scriptPath?: string | null;
    targetTable: string;
    targetColumn?: string | null;
    sourceTables: string[];
    operation: string;
    tags: Record<string, string>;
    protectionTags: string[];
    protectionReason: string;
    owner?: string | null;
    steward?: string | null;
    contact?: string | null;
    domain?: string | null;
    classification?: string | null;
    quality?: string | null;
    sourceFile?: string | null;
    line: number;
}

interface ProvisionTenantAdminRequest {
    tenantId: string;
    activeRelease?: string | null;
    maxConcurrentJobs?: number | null;
    maxStorageMb?: number | null;
    maxReportSessions?: number | null;
    platformOperator: string;
    authorizationReference: string;
    reason: string;
}

interface PublishReportRequest {
    folderId: number;
    name: string;
    scriptPath: string;
    description?: string | null;
    owner?: string | null;
    contact?: string | null;
    tags?: string | null;
    category?: string | null;
    domain?: string | null;
    steward?: string | null;
    certification?: string | null;
    createdByUsername?: string | null;
    kind?: string | null;
}

interface QuarantineDispositionRequest {
    quarantineTarget: string;
    rowIds: string[];
    disposition: string;
    jobName?: string | null;
    changes?: Record<string, string | null> | null;
    note?: string | null;
    expectedStatus: string;
}

interface QuarantineDispositionResponse {
    jobId: string;
    dispositionStatement: string;
}

interface QuarantineQueueItemDto {
    jobName: string;
    scriptPath?: string | null;
    sectionLabel?: string | null;
    sourceTable: string;
    quarantineTarget: string;
    isReplayable: boolean;
    nonReplayableReason?: string | null;
    inputColumns: string[];
    inputSchemaFingerprint: string;
    updatedAtUtc: string;
    replayMode: string;
    probeSourceTable?: string | null;
    joinBuildTable?: string | null;
    joinObservedN1?: boolean | null;
    joinNonReplayableReason?: string | null;
    replayStatement: string;
    rowsReadable: boolean;
    rowsUnavailableReason?: string | null;
    reviewStatement: string;
}

interface QuarantineRowsResponse {
    quarantineTarget: string;
    status: string;
    columns: string[];
    rows: Record<string, unknown>[];
    capped: boolean;
}

interface QuarantineSubmissionStatusDto {
    jobId: string;
    kind: string;
    quarantineTarget: string;
    status: string;
    isTerminal: boolean;
    submittedAtUtc: string;
    statusUpdatedAtUtc: string;
    error?: string | null;
    unknownReason?: string | null;
}

interface ReplayQuarantineRequest {
    quarantineTarget: string;
    jobName?: string | null;
}

interface ReplayQuarantineResponse {
    jobId: string;
    replayStatement: string;
}

interface ReportDependencyAlertDto {
    id: number;
    name: string;
    visualName: string;
    operator: string;
    threshold: number;
    isActive: boolean;
    lastState?: string | null;
    lastEvaluatedAt?: string | null;
    notifications: ReportDependencyAlertNotificationDto[];
}

interface ReportDependencyAlertNotificationDto {
    orchestratorAlias: string;
    notificationName: string;
}

interface ReportDependencyDatasetDto {
    id: number;
    name: string;
    folderPath: string;
    accessLevel: string;
    rowCount: number;
    lastRefresh?: string | null;
    refreshInterval?: string | null;
    sources: ReportDependencySourceDto[];
}

interface ReportDependencyDto {
    report: ReportDependencyReportDto;
    snapshot?: ReportDependencySnapshotDto | null;
    manifestDatasets: ReportDependencyManifestDatasetDto[];
    registeredDatasets: ReportDependencyDatasetDto[];
    refreshJobs: ReportDependencyRefreshJobDto[];
    alerts: ReportDependencyAlertDto[];
    sources: ReportDependencySourceDto[];
    lineageEntries: ReportDependencyLineageDto[];
}

interface ReportDependencyLineageDto {
    target: string;
    targetColumn?: string | null;
    operation: string;
    sources: string[];
    sourceColumns: string[];
    tags: Record<string, string>;
    line: number;
    transformationKind?: string | null;
    transformationExpression?: string | null;
    functionsApplied?: string[] | null;
    derivedFromDescriptions?: string | null;
}

interface ReportDependencyManifestDatasetDto {
    tempTableName: string;
    refreshInterval?: string | null;
    ttl?: string | null;
    lastRefresh?: string | null;
    rowCount: number;
}

interface ReportDependencyRefreshJobDto {
    id: number;
    orchestratorJobName: string;
    orchestratorAlias?: string | null;
    refreshInterval: string;
    lastRefreshedAt?: string | null;
}

interface ReportDependencyReportDto {
    id: number;
    name: string;
    folderPath: string;
    scriptPath: string;
}

interface ReportDependencySnapshotDto {
    id: number;
    manifestPath: string;
    builtAt: string;
}

interface ReportDependencySourceDto {
    name: string;
    connection?: string | null;
    objectName?: string | null;
    kind: string;
}

interface ReportDto {
    id: number;
    folderId: number;
    folderPath: string;
    name: string;
    description?: string | null;
    owner?: string | null;
    contact?: string | null;
    tags?: string | null;
    category?: string | null;
    domain?: string | null;
    steward?: string | null;
    certification?: string | null;
    metadata: Record<string, string>;
    scriptPath: string;
    scriptLastModified: string;
    hasSnapshot: boolean;
    snapshotBuiltAt?: string | null;
    lastViewedAt?: string | null;
    lastRefreshStartedAt?: string | null;
    lastRefreshCompletedAt?: string | null;
    lastRefreshStatus?: string | null;
    lastRefreshError?: string | null;
    lastRefreshDurationMs?: number | null;
    isFavorite: boolean;
    isStale: boolean;
    scriptChanged: boolean;
    version: number;
}

interface ReportParameterDto {
    name: string;
    type: string;
    default?: string | null;
    required: boolean;
    description?: string | null;
}

interface ReportScriptValidationDto {
    isValid: boolean;
    scriptPath: string;
    hash?: string | null;
    lastModified?: string | null;
    metadata: Record<string, string>;
    parameters: ReportParameterDto[];
    errors: string[];
    impact?: ReportValidationImpactDto | null;
}

interface ReportValidationImpactDto {
    sources: ReportValidationImpactSourceDto[];
    reportCount: number;
    datasetCount: number;
    subscriptionCount: number;
    jobCount: number;
}

interface ReportValidationImpactSourceDto {
    source: string;
    summary: LineageImpactSummaryDto;
}

interface ResolveCaseRequest {
    version: number;
    resolution: string;
    note?: string | null;
    evidenceReference?: string | null;
}

interface ReviewAssetRequest {
    assetKey: string;
    assetVersion: string;
    note?: string | null;
}

interface RunDesignerRequest {
    script: string;
    selection?: string | null;
    connectionRef?: string | null;
    documentUri?: string | null;
    parameters?: Record<string, string> | null;
    previewAs?: PreviewAsRequest | null;
    clientRunId?: string | null;
}

interface RunDesignerResponse {
    columns: string[];
    rows: Record<string, unknown>[];
    rowCount: number;
    capped: boolean;
    elapsedMs: number;
    message: string;
    pipeline?: unknown;
    byteCapped: boolean;
    bytesReturned: number;
    runId?: string | null;
}

interface SaveDesignerResponse {
    version: number;
    sourceRevision?: string | null;
}

interface SaveGlossaryTermRequest {
    term: string;
    dataType: string;
    aliases: string;
    description: string;
    formula?: string | null;
    steward?: string | null;
    disabled: boolean;
}

interface SaveGovernanceCategoryRequest {
    value: string;
    label: string;
    color?: string | null;
    expiryDays?: number | null;
    disabled: boolean;
}

interface ScheduleDefinitionDto {
    name: string;
    cron: string;
    timeZone: string;
    isEnabled: boolean;
    displayName?: string | null;
    description?: string | null;
    options?: string | null;
    createdBy?: string | null;
    modifiedBy?: string | null;
    version: number;
    tenantId?: string | null;
    id: string | null;
}

interface SchedulePipelineRequest {
    version: number;
    cron: string;
    timeZone?: string | null;
}

interface ScheduledJobActivity {
    job: JobDefinition;
    runState: "Idle" | "Queued" | "Running";
    completedToday: boolean;
    failedToday: boolean;
}

interface ScheduledJobActivityCounts {
    active: number;
    queued: number;
    completedToday: number;
    failedToday: number;
}

interface ScheduledJobActivityPage {
    asOfUtc: string;
    dayStartUtc: string;
    counts: ScheduledJobActivityCounts;
    total: number;
    offset: number;
    limit: number;
    items: ScheduledJobActivity[];
}

interface ScriptContentResponse {
    scriptText: string;
    version: number;
    sourceRevision?: string | null;
    sourceControlEnabled: boolean;
}

interface ScriptDagDto {
    nodes: ScriptDagNodeDto[];
    edges: ScriptDagEdgeDto[];
}

interface ScriptDagEdgeDto {
    source: string;
    target: string;
    label?: string | null;
}

interface ScriptDagNodeDto {
    id: string;
    label: string;
    type: string;
    meta?: unknown;
}

interface ScriptDagProjection {
    parsed: boolean;
    dag: ScriptDagDto;
    error?: string | null;
}

interface SecretKeyRingCheckResult {
    secretCount: number;
    failedCount: number;
    firstFailedName?: string | null;
    firstFailureReason?: string | null;
}

interface SecurityEventDiagnostics {
    outboxConfigured: boolean;
    outboxReadable: boolean;
    pendingCount: number;
    failedCount: number;
    filteredCount: number;
    storedBytes: number;
    oldestPendingUtc?: string | null;
    lastDeliveredUtc?: string | null;
    droppedCount: number;
    collectorConfigured: boolean;
    collectorReachable?: boolean | null;
    lastCollectorAttemptUtc?: string | null;
    lastCollectorSuccessUtc?: string | null;
    lastCollectorFailureUtc?: string | null;
    lastCollectorError?: string | null;
}

interface ServiceAccountCreatedResponse {
    account: ServiceAccountDto;
    clientSecret: string;
}

interface ServiceAccountDto {
    id: string;
    clientId: string;
    name: string;
    description?: string | null;
    ownerUserId: number;
    scopes: string[];
    roles: string[];
    isEnabled: boolean;
    expiresAt?: string | null;
    revokedAt?: string | null;
    createdAt: string;
    updatedAt: string;
    lastUsedAt?: string | null;
    version: number;
    studioCapabilities?: string[] | null;
}

interface SetConnectionRequest {
    connectorType: string;
    target?: string | null;
    options?: Record<string, string> | null;
    environmentScope?: string | null;
    sensitiveFields?: string[] | null;
    gateway?: GatewayResourceBinding | null;
}

interface SharedConnectionDiagnosticDto {
    alias: string;
    succeeded: boolean;
    steps: SharedConnectionDiagnosticStepDto[];
}

interface SharedConnectionDiagnosticStepDto {
    layer: string;
    status: string;
    detail: string;
    remedy?: string | null;
}

interface SharedConnectionImportDto {
    created: number;
    updated: number;
}

interface SharedConnectionVerificationDto {
    alias: string;
    status: string;
    secretReferences: number;
}

interface StatementMetricsPayload {
    statement: string;
    duration_ms: number;
    rows_processed: number;
    cpu_time_ms: number;
    spilled_bytes: number;
    spill_read_bytes: number;
    partitions: number;
    queue_wait_ms: number;
    lock_wait_ms: number;
    index_used?: string | null;
    dq_rows_validated: number;
    dq_rows_quarantined: number;
    dq_rows_warned: number;
    dq_validation_ms: number;
    failed: boolean;
}

interface StewardshipAssetDto {
    targetTable: string;
    targetColumn?: string | null;
    runAt: string;
    jobName?: string | null;
    scriptPath?: string | null;
    sourceTables: string[];
    tags: Record<string, string>;
    missingTags: string[];
    isSensitive: boolean;
    isRestricted: boolean;
    isStale: boolean;
    staleReason: string;
    owner?: string | null;
    steward?: string | null;
    contact?: string | null;
    domain?: string | null;
    classification?: string | null;
    quality?: string | null;
    freshness?: string | null;
}

interface StewardshipCatalogDto {
    summary: StewardshipSummaryDto;
    stewards: StewardshipFacetDto[];
    domains: StewardshipFacetDto[];
    classifications: StewardshipFacetDto[];
    qualities: StewardshipFacetDto[];
    items: StewardshipAssetDto[];
}

interface StewardshipFacetDto {
    value: string;
    count: number;
}

interface StewardshipFindingDto {
    id: number;
    assetKey: string;
    ruleKey: string;
    assetVersion?: string | null;
    detail?: string | null;
    status: string;
    firstSeenUtc: string;
    lastSeenUtc: string;
    resolvedAtUtc?: string | null;
    suppressedUntilUtc?: string | null;
    decisions: GovernanceDecisionDto[];
}

interface StewardshipGlossaryTermDto {
    id: number;
    term: string;
    dataType: string;
    aliases: string;
    description: string;
    formula?: string | null;
    steward?: string | null;
    disabled: boolean;
    updatedAtUtc: string;
}

interface StewardshipScanDto {
    id: number;
    trigger: string;
    startedAtUtc: string;
    completedAtUtc?: string | null;
    status: string;
    error?: string | null;
    assetsScanned: number;
    findingsOpened: number;
    findingsResolved: number;
    findingsReopened: number;
}

interface StewardshipSettingsDto {
    targetScore: number;
    enableMetadataCheck: boolean;
    enableProtectedDataCheck: boolean;
    enableGlossaryCheck: boolean;
    enableStalenessCheck: boolean;
    deductMetadata: number;
    deductProtectedData: number;
    deductGlossary: number;
    deductStaleness: number;
    staleAfterDays: number;
    policyLevel: string;
    updatedAtUtc: string;
    version: number;
}

interface StewardshipSummaryDto {
    totalAssets: number;
    sensitiveAssets: number;
    missingMetadataAssets: number;
    staleAssets: number;
    stewardQueueAssets: number;
}

interface StudioFolderDto {
    id: number;
    path: string;
    name: string;
}

interface StudioRecoveryDraftDto {
    content: string;
    baseVersion?: number | null;
    baseSourceRevision?: string | null;
    updatedAt: string;
}

interface StudioReportDto {
    id: number;
    folderId: number;
    folderPath: string;
    name: string;
    description?: string | null;
    updatedAt: string;
    version: number;
    kind: string;
}

interface StudioSessionDto {
    mode: string;
    capabilities: string[];
    sourceControlEnabled: boolean;
    draftRecovery: boolean;
}

interface SubmitPipelineRunRequest {
    version: number;
}

interface SubscriptionDto {
    id: number;
    reportId: number;
    reportName: string;
    name?: string | null;
    schedule?: string | null;
    atTime?: string | null;
    deliverOnRefresh: boolean;
    format: string;
    smtpAlias: string;
    recipients: string;
    lastSentAt?: string | null;
    nextRunAt?: string | null;
    failCount: number;
    isActive: boolean;
    parameters?: Record<string, string> | null;
    parameterSummary?: string | null;
    version: number;
}

interface TestConnectionRequest {
    alias?: string | null;
    connectorType?: string | null;
    target?: string | null;
    options?: Record<string, string> | null;
    probeTimeoutSeconds: number;
}

interface TriageBoardDto {
    generatedAt: string;
    lookbackHours: number;
    failureCount: number;
    incidentCount: number;
    runningCount: number;
    missedCount: number;
    incidents: TriageIncidentDto[];
    running: TriageRunDto[];
    missed: TriageMissedJobDto[];
    truncated: boolean;
}

interface TriageIncidentDto {
    signature: string;
    sampleError: string;
    failureCount: number;
    jobNames: string[];
    firstSeen: string;
    lastSeen: string;
    runs: TriageRunDto[];
}

interface TriageMissedJobDto {
    jobName: string;
    displayName?: string | null;
    dueAt: string;
    overdueMinutes: number;
    lastRun?: string | null;
}

interface TriageRerunRequest {
    jobNames: string[];
}

interface TriageRerunResponseDto {
    requested: number;
    triggered: number;
    results: TriageRerunResultDto[];
}

interface TriageRerunResultDto {
    jobName: string;
    triggered: boolean;
    error?: string | null;
}

interface TriageRunDetailDto {
    run: TriageRunDto;
    statements: StatementMetricsPayload[];
    qualityFailures: JobDataQualityFailure[];
}

interface TriageRunDto {
    id: number;
    jobName: string;
    startTime: string;
    endTime?: string | null;
    status: string;
    errorMessage?: string | null;
    rowsProcessed: number;
    rowsQuarantined: number;
    rowsWarned: number;
    dataQualityFailures?: string | null;
    scriptHashAtRunTime?: string | null;
    hashMatched?: boolean | null;
}

interface UnownedObjectResponse {
    objectId: string;
    kind: string;
    name: string;
}

interface UpdateDatasetRequest {
    accessLevel?: string | null;
    ttl?: string | null;
}

interface UpdateFolderRequest {
    name?: string | null;
    parentId?: number | null;
}

interface UpdateGroupRequest {
    name?: string | null;
    description?: string | null;
    provider?: string | null;
    adGroup?: string | null;
}

interface UpdateNotificationRequest {
    connectionName?: string | null;
    recipient?: string | null;
    isEnabled?: boolean | null;
    displayName?: string | null;
    description?: string | null;
    options?: Record<string, string> | null;
}

interface UpdateOrchestratorSettingsRequest {
    apiUrl?: string | null;
    apiKey?: string | null;
}

interface UpdatePortalBrandingRequest {
    displayName?: string | null;
    footerText?: string | null;
    logoUrl?: string | null;
}

interface UpdateReportRequest {
    name?: string | null;
    description?: string | null;
    folderId?: number | null;
    scriptPath?: string | null;
    owner?: string | null;
    contact?: string | null;
    tags?: string | null;
    category?: string | null;
    domain?: string | null;
    steward?: string | null;
    certification?: string | null;
}

interface UpdateScheduleRequest {
    cron?: string | null;
    timeZone?: string | null;
    isEnabled?: boolean | null;
    displayName?: string | null;
    description?: string | null;
    options?: Record<string, string> | null;
}

interface UpdateServiceAccountRequest {
    isEnabled: boolean;
    expiresAt?: string | null;
    scopes: string[];
    studioCapabilities?: string[] | null;
}

interface UpdateStewardshipSettingsRequest {
    targetScore: number;
    enableMetadataCheck: boolean;
    enableProtectedDataCheck: boolean;
    enableGlossaryCheck: boolean;
    enableStalenessCheck: boolean;
    deductMetadata: number;
    deductProtectedData: number;
    deductGlossary: number;
    deductStaleness: number;
    staleAfterDays: number;
    policyLevel?: string | null;
}

interface UpdateSubscriptionRequest {
    name?: string | null;
    schedule?: string | null;
    atTime?: string | null;
    deliverOnRefresh?: boolean | null;
    format?: string | null;
    smtpAlias?: string | null;
    recipients?: string | null;
    isActive?: boolean | null;
    parameters?: Record<string, string> | null;
}

interface UpdateTenantQuotasAdminRequest {
    maxConcurrentJobs?: number | null;
    maxStorageMb?: number | null;
    maxReportSessions?: number | null;
    platformOperator: string;
    authorizationReference: string;
    reason: string;
}

interface UpdateTenantStateAdminRequest {
    state: string;
    platformOperator: string;
    authorizationReference: string;
    reason: string;
}

interface UpdateUserRequest {
    email?: string | null;
    firstName?: string | null;
    lastName?: string | null;
    role?: string | null;
    isActive?: boolean | null;
}

interface UserDto {
    id: number;
    username: string;
    email?: string | null;
    firstName?: string | null;
    lastName?: string | null;
    isActive: boolean;
    mustChangePassword: boolean;
    createdAt: string;
    roles: string[];
    groups: string[];
    provider?: string | null;
    version: number;
}

interface ValidateReportScriptRequest {
    scriptPath: string;
}

interface VersionedResourceRequest {
    id: number;
    version: number;
}
