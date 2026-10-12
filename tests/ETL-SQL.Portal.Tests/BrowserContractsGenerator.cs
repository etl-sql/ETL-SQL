using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Controllers;
using ETL_SQL.Portal.Models;
using DocumentationDocument = ETL_SQL.Portal.Services.DocumentationDocument;
using DocumentationSearchResult = ETL_SQL.Portal.Services.DocumentationSearchResult;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Emits the TypeScript declarations for the C# types the browser code talks to.
///
/// <para>Most of the defects that actually bite cross this boundary rather than living inside the
/// JavaScript: a route name, a DTO field name, a kind id. <c>endLine</c> was threaded through two
/// hosts and a mock by hand, and nothing would have caught a miss. Generating the declarations from
/// the types themselves means the browser sources are checked against what the server really sends,
/// and means the check is not another regex pulling literals out of a .js file.</para>
///
/// <para>Reflection, not parsing. The shapes come from the compiled records, so a renamed property
/// or a new enum member arrives here whether or not anyone remembered this file existed.</para>
/// </summary>
internal static class BrowserContractsGenerator
{
    /// <summary>
    /// The enums whose values cross the wire as strings.
    ///
    /// <para>Pipeline values use the lowercase form written by the canvas and palette. Dataset
    /// access levels and permissions use the enum names returned by their DTOs.</para>
    /// </summary>
    private static readonly Type[] LowercaseStringEnums = [typeof(PipelineTaskKind), typeof(PipelineEdgeCondition)];
    private static readonly Type[] StringEnums = [.. LowercaseStringEnums, typeof(DatasetPermission), typeof(DatasetAccessLevel)];

    /// <summary>
    /// The records the browser receives or sends. Nested record types they reference are pulled in
    /// automatically, so this list only has to name the entry points.
    /// </summary>
    private static readonly Type[] Records =
    [
        typeof(ParseDesignerResponse),
        typeof(RunDesignerRequest),
        typeof(RunDesignerResponse),
        typeof(PipelineTaskDto),
        typeof(PipelineTaskResponse),
        typeof(PipelineDependencyDto),
        typeof(PipelineScopeRequest),
        typeof(PipelineRunPlanRequest),
        typeof(DataModelRequest),
        typeof(DataModelResponse),
        typeof(DataModelEntityDto),
        typeof(DataModelColumnDto),
        typeof(DataModelRelationshipDto),
        typeof(ScriptDagNodeDto),
        typeof(ScriptDagEdgeDto),
        typeof(ScriptDagDto),
        typeof(ScriptDagProjection),
        typeof(NavDestinationDto),
        typeof(PortalBrandingDto),
        typeof(OrchestratorSettingsDto),
        typeof(OrchestratorConnectionStatusDto),
        typeof(OrchestratorStatusDto),
        typeof(FolderDto),
        typeof(CreateFolderRequest),
        typeof(GrantPermissionRequest),
        typeof(FolderAclCatalogResponse),
        typeof(FolderAclGrantResponse),
        typeof(FolderAclRevokeResponse),
        typeof(DatasetAclEntryDto),
        typeof(DatasetDto),
        typeof(DatasetRowsDto),
        typeof(DatasetColumnFilterDto),
        typeof(DatasetColumnStatsDto),
        typeof(DatasetColumnValuesDto),
        typeof(DatasetRefreshAcceptedDto),
        typeof(DatasetVersionResponse),
        typeof(GroupDto),
        typeof(GroupMemberCatalogResponse),
        typeof(GroupMembershipResponse),
        typeof(BulkAddGroupMembersResponse),
        typeof(BulkRemoveGroupMembersResponse),
        typeof(BulkDeleteGroupsResponse),
        typeof(BulkGroupMembershipRequest),
        typeof(BulkDeleteGroupsRequest),
        typeof(LoginResponse),
        typeof(AuthProvidersResponse),
        typeof(StudioSessionDto),
        typeof(StudioReportDto),
        typeof(StudioFolderDto),
        typeof(StudioRecoveryDraftDto),
        typeof(StudioPrivateDraftSummaryDto),
        typeof(StudioPrivateDraftDto),
        typeof(CreateStudioReportRequest),
        typeof(SubmitPipelineRunRequest),
        typeof(PipelineRunSubmittedDto),
        typeof(PipelineRunRefusalDto),
        typeof(PipelineRunCancelRequestedDto),
        typeof(SchedulePipelineRequest),
        typeof(PipelineScheduleDto),
        typeof(PipelineRunStatementDto),
        typeof(PipelineRunDto),
        typeof(ScriptContentResponse),
        typeof(SaveDesignerResponse),
        typeof(ReportDto),
        typeof(AdminReportDto),
        typeof(ReportScriptValidationDto),
        typeof(ValidateReportScriptRequest),
        typeof(DesignerLeaseResponse),
        typeof(DesignerSnapshotResponse),
        typeof(DocumentationSearchResult),
        typeof(DocumentationDocument),
        typeof(PagedResult<>),
        typeof(JobHistoryEntry),
        typeof(CatalogLineageHistoryDto),
        typeof(StewardshipCatalogDto),
        typeof(GovernanceDashboardDto),
        typeof(StewardshipFindingDto),
        typeof(StewardshipScanDto),
        typeof(StewardshipSettingsDto),
        typeof(GovernanceCategoryDto),
        typeof(StewardshipGlossaryTermDto),
        typeof(GovernanceAssetReviewDto),
        typeof(GovernanceBadgeAssignmentDto),
        typeof(LineageImpactDto),
        typeof(ProtectedLineageHistoryEntry),
        typeof(ProtectedDataSuggestionEntry),
        typeof(DownstreamReportDto),
        typeof(ReportDependencyDto),
        typeof(JobDefinitionDto),
        typeof(ScheduledJobActivityPage),
        typeof(TriageBoardDto),
        typeof(GatewayEnrollmentController.GatewayEnrollmentDto),
        typeof(GatewayEnrollmentController.IssueEnrollmentRequest),
        typeof(GatewayEnrollmentController.IssuedGatewayEnrollmentDto),
        typeof(GatewayEnrollmentController.GatewayRevocationDto),
        typeof(ConnectorsController.DiscoveredGatewayDto),
        typeof(ConnectorsController.TestConnectionRequest),
        typeof(ETL_SQL.Data.ConnectorSchemaDescriptor),
        typeof(ETL_SQL.Core.Diagnostics.DiagnosticReport),
        typeof(TriageRunDetailDto),
        typeof(ETL_SQL.Core.Profiling.StatementMetricsPayload),
        typeof(TriageRerunRequest),
        typeof(TriageRerunResponseDto),
        typeof(ScheduleDefinitionDto),
        typeof(CreateScheduleRequest),
        typeof(UpdateScheduleRequest),
        typeof(NotificationDefinitionDto),
        typeof(CreateNotificationRequest),
        typeof(UpdateNotificationRequest),
        typeof(ETL_SQL.Portal.Services.OrchestratorNotificationDispatchRequest),
        typeof(ETL_SQL.Orchestrator.Scheduling.NotificationDispatchResult),
        typeof(JobStateEntryDto),
        typeof(JobScheduleLinkDto),
        typeof(JobNotificationLinkDto),
        typeof(JobHistoryEntryDto),
        typeof(JobDependencyChainDto),
        typeof(ETL_SQL.Core.Data.JobDataQualityStatus),
        typeof(ETL_SQL.Portal.Data.AuditLog),
        typeof(ETL_SQL.Orchestrator.Service.JobApiEndpoints.ObjectGrantResponse),
        typeof(ETL_SQL.Orchestrator.Service.JobApiEndpoints.UnownedObjectResponse),
        typeof(ETL_SQL.Portal.Services.PortalSecretSummary),
        typeof(ETL_SQL.Portal.Services.SecretKeyRingCheckResult),
        typeof(ETL_SQL.Portal.Services.ImpactReport),
        typeof(ETL_SQL.Portal.Services.ControlPlaneFleetOverview),
        typeof(ETL_SQL.Portal.Services.ControlPlaneTenantDto),
        typeof(ETL_SQL.Portal.Services.PlatformAuditReceiptDto),
        typeof(ETL_SQL.Portal.Services.ProvisionTenantAdminRequest),
        typeof(ETL_SQL.Portal.Services.UpdateTenantQuotasAdminRequest),
        typeof(ETL_SQL.Portal.Services.UpdateTenantStateAdminRequest),
        typeof(ETL_SQL.Portal.Services.OperationalMetricsService.OperationalMetrics),
        typeof(ETL_SQL.Portal.Services.FleetEnvironmentStatus),
        typeof(ETL_SQL.Portal.Services.GatewayAmbiguousWriteCaseDto),
        typeof(ServiceAccountDto),
        typeof(ServiceAccountCreatedResponse),
        typeof(CreateServiceAccountRequest),
        typeof(PendingAccessRequestDto),
        typeof(AnonymousReportAccessDto),
        typeof(UserDto),
        typeof(AdminRoleOptionDto),
        typeof(BulkUserStatusResponse),
        typeof(SubscriptionDto),
        typeof(BulkSubscriptionStatusRequest),
        typeof(BulkSubscriptionStatusResponse),
        typeof(AuditLogDto),
        typeof(AdminServiceStatus),
        typeof(ApproveReportAccessRequestDto),
        typeof(DenyReportAccessRequestDto),
        typeof(ETL_SQL.Portal.Controllers.GatewayAmbiguousWritesController.CaseNoteRequest),
        typeof(ETL_SQL.Portal.Controllers.GatewayAmbiguousWritesController.AssignCaseRequest),
        typeof(ETL_SQL.Portal.Controllers.GatewayAmbiguousWritesController.EvidenceRequest),
        typeof(ETL_SQL.Portal.Controllers.GatewayAmbiguousWritesController.ResolveCaseRequest),
        typeof(UpdateFolderRequest),
        typeof(PublishReportRequest),
        typeof(UpdateReportRequest),
        typeof(CreateSubscriptionRequest),
        typeof(UpdateSubscriptionRequest),
        typeof(UpdateDatasetRequest),
        typeof(CreateUserRequest),
        typeof(UpdateUserRequest),
        typeof(CreateGroupRequest),
        typeof(UpdateGroupRequest),
        typeof(VersionedResourceRequest),
        typeof(UpdateServiceAccountRequest),
        typeof(UpdateStewardshipSettingsRequest),
        typeof(SaveGovernanceCategoryRequest),
        typeof(SaveGlossaryTermRequest),
        typeof(DecideFindingRequest),
        typeof(ReviewAssetRequest),
        typeof(AssignBadgeRequest),
        typeof(QuarantineDispositionRequest),
        typeof(DataQualityJobDto),
        typeof(QuarantineQueueItemDto),
        typeof(QuarantineRowsResponse),
        typeof(ReplayQuarantineRequest),
        typeof(ReplayQuarantineResponse),
        typeof(QuarantineDispositionResponse),
        typeof(QuarantineSubmissionStatusDto),
        typeof(DataQualityTrendDto),
        typeof(DataQualityRuleDefinitionDto),
        typeof(PolicyPublishRequest),
        typeof(PolicyAuthorityStatusDto),
        typeof(PolicyValidationDto),
        typeof(PolicyVersionDto),
        typeof(PolicyMachineDto),
        typeof(PolicyValidateRequest),
        typeof(PolicyActivateRequest),
        typeof(PolicyCanaryPromoteRequest),
        typeof(PolicyCanaryHaltRequest),
        typeof(PolicyMachineRevokeRequest),
        typeof(PolicyRollbackRequest),
        typeof(PolicyCanaryPublishRequest),
        typeof(PolicyMachineRegisterRequest),
        typeof(UpdateOrchestratorSettingsRequest),
        typeof(UpdatePortalBrandingRequest),
        typeof(ETL_SQL.Portal.Controllers.ConnectionsAdminController.SetConnectionRequest),
        typeof(ETL_SQL.Portal.Services.PortalSharedConnectionExport),
        typeof(ETL_SQL.Portal.Services.PortalSharedConnectionSummary),
        typeof(ETL_SQL.Portal.Services.PortalSharedConnectionDetail),
        typeof(ETL_SQL.Portal.Controllers.ConnectionsAdminController.SharedConnectionVerificationDto),
        typeof(ETL_SQL.Portal.Controllers.ConnectionsAdminController.SharedConnectionDiagnosticDto),
        typeof(ETL_SQL.Portal.Controllers.ConnectionsAdminController.SharedConnectionImportDto),
        typeof(ETL_SQL.Portal.Controllers.ConnectionsAdminController.ConnectionHelpDto),
        typeof(ETL_SQL.Portal.Controllers.ConnectorsController.ParseConnectionStringRequest),
        typeof(ETL_SQL.Connectors.ParsedConnectionStringResult),
    ];

    /// <summary>
    /// Properties the C# declares as <c>string</c> whose vocabulary is one of the enums above.
    ///
    /// <para>These cross the wire as strings because the DTO is the browser's shape and an unknown
    /// name has to be reportable rather than a deserialisation failure — but the set of legal names
    /// is the enum's, and both records say so in their own doc comments. Declaring them as the union
    /// is what lets the browser sources be checked against the vocabulary instead of against
    /// "any string at all", which is the whole reason this generator exists.</para>
    ///
    /// <para>Keyed by declaring type and property so a same-named property elsewhere is untouched.</para>
    /// </summary>
    private static readonly Dictionary<(Type Declaring, string Property), string> VocabularyOverrides = new()
    {
        [(typeof(PipelineTaskDto), nameof(PipelineTaskDto.Kind))] = nameof(PipelineTaskKind),
        [(typeof(PipelineDependencyDto), nameof(PipelineDependencyDto.Condition))] = nameof(PipelineEdgeCondition),
    };

    public static string Generate()
    {
        var builder = new StringBuilder();
        builder.Append("""
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


            """.ReplaceLineEndings("\n"));

        foreach (var type in StringEnums)
        {
            var members = Enum.GetNames(type).Select(name => $"'{(LowercaseStringEnums.Contains(type) ? name.ToLowerInvariant() : name)}'");
            builder.Append($"/** {type.Name}, as it crosses the wire. Matched case-insensitively on the way in. */\n");
            builder.Append($"type {type.Name} =\n    | {string.Join("\n    | ", members)};\n\n");
        }

        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(Records);
        var emitted = new SortedDictionary<string, string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.Add(type)) continue;

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                // EqualityContract is a record implementation detail and never serialised.
                .Where(p => p.Name != "EqualityContract")
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
                .ToList();

            var body = new StringBuilder();
            var name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;
            var parameters = type.IsGenericTypeDefinition
                ? $"<{string.Join(", ", type.GetGenericArguments().Select(t => t.Name))}>" : "";
            body.Append($"interface {name}{parameters} {{\n");
            foreach (var property in properties)
            {
                var (declared, optional) = TypeScriptFor(property, queue);
                if (VocabularyOverrides.TryGetValue((type, property.Name), out var vocabulary))
                    declared = vocabulary;
                var field = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? Camel(property.Name);
                body.Append($"    {field}{(optional ? "?" : "")}: {declared};\n");
            }
            body.Append("}\n");
            emitted[name] = body.ToString();
        }

        builder.Append(string.Join("\n", emitted.Values));
        return builder.ToString();
    }

    /// <summary>The TypeScript for one property, queueing any record type it reaches.</summary>
    private static (string Declared, bool Optional) TypeScriptFor(PropertyInfo property, Queue<Type> queue)
    {
        var type = property.PropertyType;

        // Nullable values may be omitted or explicitly null in JSON. Both cases must be visible
        // to strict browser callers; optional alone only covers an absent property.
        var underlying = Nullable.GetUnderlyingType(type);
        var optional = underlying is not null || IsNullableReference(property);
        type = underlying ?? type;

        var declared = Declare(type, queue, new NullabilityInfoContext().Create(property), includeNull: false);
        return (optional && declared != "unknown" ? $"{declared} | null" : declared, optional);
    }

    private static string Declare(Type type, Queue<Type> queue, NullabilityInfo? nullability = null, bool includeNull = true)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var declared = DeclareNonNullable(underlying ?? type, queue, nullability);
        var nullable = underlying is not null || (!type.IsGenericParameter && nullability?.ReadState == NullabilityState.Nullable);
        return includeNull && nullable && declared != "unknown" ? $"{declared} | null" : declared;
    }

    private static string DeclareNonNullable(Type type, Queue<Type> queue, NullabilityInfo? nullability)
    {
        if (StringEnums.Contains(type)) return type.Name;
        if (type == typeof(string)) return "string";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid)) return "string";
        // These value objects have explicit converters: assigned IDs are strings and the empty
        // sentinel is null. Their CLR properties are not their browser representation.
        if (type == typeof(JobId) || type == typeof(ScheduleId) || type == typeof(NotificationId))
            return "string | null";
        if (type.IsGenericParameter) return type.Name;
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long) || type == typeof(double)
            || type == typeof(decimal) || type == typeof(float)) return "number";

        // `object?` on a DTO is a payload the browser is expected to inspect but the server does not
        // constrain — `unknown` says exactly that, and forces a check at the point of use.
        if (type == typeof(object)) return "unknown";

        if (type.IsGenericType && type.GetGenericArguments()[0] == typeof(string)
            && (type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                || type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
        {
            return $"Record<string, {Declare(type.GetGenericArguments()[1], queue, nullability?.GenericTypeArguments.ElementAtOrDefault(1))}>";
        }

        // Type-level converters are part of the wire contract too. Let the actual serializer
        // distinguish string enums from ordinary numeric enums instead of guessing their casing.
        if (type.IsEnum) return string.Join(" | ", Enum.GetValues(type).Cast<object>()
            .Select(value => JsonSerializer.Serialize(value, type, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .Distinct());

        var element = ElementTypeOf(type);
        if (element is not null)
        {
            var elementNullability = type.IsArray ? nullability?.ElementType
                : nullability?.GenericTypeArguments.ElementAtOrDefault(0);
            var item = Declare(element, queue, elementNullability);
            var union = Nullable.GetUnderlyingType(element) is not null
                || (!element.IsGenericParameter && elementNullability?.ReadState == NullabilityState.Nullable)
                || (element.IsEnum && !StringEnums.Contains(element));
            return union ? $"({item})[]" : $"{item}[]";
        }

        if (IsRecord(type) || Records.Contains(type))
        {
            if (type.IsGenericType)
            {
                queue.Enqueue(type.GetGenericTypeDefinition());
                var name = type.Name[..type.Name.IndexOf('`')];
                return $"{name}<{string.Join(", ", type.GetGenericArguments().Select((t, i) =>
                    Declare(t, queue, nullability?.GenericTypeArguments.ElementAtOrDefault(i))))}>";
            }
            queue.Enqueue(type);
            return type.Name;
        }

        return "unknown";
    }

    /// <summary>The item type of a list-shaped property, or null when the type is not one.</summary>
    private static Type? ElementTypeOf(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (!type.IsGenericType) return null;
        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IList<>) || definition == typeof(IEnumerable<>)
            || definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>))
        {
            return type.GetGenericArguments()[0];
        }
        return null;
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    /// <summary>
    /// Whether a reference-typed property is declared nullable.
    /// </summary>
    /// <remarks>
    /// Read from the compiler's nullable metadata rather than guessed, so `string?` becomes an
    /// optional field and `string` does not. Getting this backwards is the whole point of the
    /// exercise: an optional field the browser treats as always-present is exactly the defect class
    /// this file is meant to close.
    /// </remarks>
    private static bool IsNullableReference(PropertyInfo property)
    {
        if (property.PropertyType.IsValueType) return false;
        var context = new NullabilityInfoContext();
        return context.Create(property).ReadState == NullabilityState.Nullable;
    }

    private static string Camel(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
