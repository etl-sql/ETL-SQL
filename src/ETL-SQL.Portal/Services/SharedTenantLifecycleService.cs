using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record SharedTenantLifecycleAuthority(
    SharedTenantLifecycleKind Kind,
    TenantContext Tenant,
    string Operator,
    string AuthorizationReference,
    string Reason,
    DateTimeOffset ExpiresUtc,
    string? TargetRelease,
    int? MaxConcurrentJobs,
    int? MaxStorageMb,
    int? MaxReportSessions,
    DateTimeOffset? RetentionUntilUtc = null,
    bool LegalHoldCleared = false,
    SharedIdentityAuthorityDefinition? IdentityAuthority = null);

public sealed record SharedTenantLifecycleDto(
    string OperationId,
    string TenantId,
    string Kind,
    string Status,
    string Phase,
    string? ActiveRelease,
    int? MaxConcurrentJobs,
    int? MaxStorageMb,
    int? MaxReportSessions,
    long? FenceEpoch,
    string? FailureCode);

/// <summary>
/// Durable saga across the Shared Portal and Orchestrator control planes. Portal state is fenced
/// before the remote mutation. Any uncertain response leaves the tenant non-active and the exact
/// signed authorization reference replayable; it never rolls back into service by guessing.
/// </summary>
public sealed class SharedTenantLifecycleService(
    PortalDbContext db,
    PortalConfig config,
    ISharedTenantLifecycleOrchestratorClient orchestrator,
    AuditService? audit = null)
{
    public static SharedTenantLifecycleAuthority ResolveAuthority(
        SharedTenantLifecycleKind kind,
        string? assertedTenant,
        EffectiveEnterprisePolicy policy,
        PortalConfig config,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!config.SharedTenancy.Enabled)
            throw new InvalidOperationException("Shared tenancy is not enabled.");

        string? tenant;
        string? platformOperator;
        string? authorizationReference;
        string? reason;
        DateTimeOffset? expires;
        string? release = null;
        int? jobs = null, storage = null, reports = null;
        DateTimeOffset? retention = null;
        var legalCleared = false;
        SharedIdentityAuthorityDefinition? identityAuthority = null;

        if (!policy.IsAvailable || policy.Document is null)
            throw new UnauthorizedAccessException(
                "Shared lifecycle requires an active signed organization-policy authorization.");

        if (kind == SharedTenantLifecycleKind.Provision)
        {
            var value = policy.Document.SaasOnboarding;
            if (!value.Enabled) throw new UnauthorizedAccessException("Shared provisioning is not authorized.");
            tenant = value.TenantId;
            platformOperator = value.OperatorPrincipal;
            authorizationReference = value.AuthorizationReference;
            reason = value.Reason;
            expires = value.ExpiresUtc;
            release = config.SharedTenancy.DefaultRelease;
            jobs = config.SharedTenancy.DefaultMaxConcurrentJobs;
            storage = config.SharedTenancy.DefaultMaxStorageMb;
            reports = config.SharedTenancy.DefaultMaxReportSessions;
            if (string.IsNullOrWhiteSpace(value.PortalHost)
                || string.IsNullOrWhiteSpace(value.LoginDomain)
                || string.IsNullOrWhiteSpace(value.Issuer)
                || string.IsNullOrWhiteSpace(value.ClientId)
                || string.IsNullOrWhiteSpace(value.ClientSecretReference))
                throw new InvalidOperationException(
                    "Shared provisioning requires a complete signed identity-authority binding.");
            identityAuthority = new SharedIdentityAuthorityDefinition(
                SharedIdentityAuthorityService.NormalizeDomain(value.PortalHost, nameof(value.PortalHost)),
                SharedIdentityAuthorityService.NormalizeDomain(value.LoginDomain, nameof(value.LoginDomain)),
                SharedIdentityAuthorityService.NormalizeIssuer(value.Issuer),
                value.ClientId.Trim().Length <= 512
                    ? value.ClientId.Trim()
                    : throw new ArgumentException("Shared OIDC client id exceeds 512 characters."),
                SharedIdentityAuthorityService.NormalizeSecretReference(value.ClientSecretReference),
                Enabled: true);
        }
        else if (kind == SharedTenantLifecycleKind.Upgrade)
        {
            var value = policy.Document.SaasUpgrade;
            if (!value.Enabled) throw new UnauthorizedAccessException("Shared upgrade is not authorized.");
            tenant = value.TenantId;
            platformOperator = value.OperatorPrincipal;
            authorizationReference = value.AuthorizationReference;
            reason = value.Reason;
            expires = value.ExpiresUtc;
            release = value.TargetRelease;
            jobs = value.MaxConcurrentJobs;
            storage = value.MaxStorageMb;
            reports = value.MaxReportSessions;
        }
        else
        {
            var value = policy.Document.SaasDeletion;
            if (!value.Enabled) throw new UnauthorizedAccessException("Shared deletion is not authorized.");
            tenant = value.TenantId;
            platformOperator = value.OperatorPrincipal;
            authorizationReference = value.AuthorizationReference;
            reason = value.Reason;
            expires = value.ExpiresUtc;
            retention = value.RetentionUntilUtc;
            legalCleared = value.LegalHoldCleared;
            if (!legalCleared || retention is null || now < retention)
                throw new UnauthorizedAccessException("Retention or legal hold blocks Shared tenant deletion.");
        }

        if (expires is null || expires <= now)
            throw new UnauthorizedAccessException("Lifecycle authorization is missing or expired.");
        if (string.IsNullOrWhiteSpace(platformOperator) || platformOperator.Trim().Length > 256
            || string.IsNullOrWhiteSpace(authorizationReference) || authorizationReference.Trim().Length > 256
            || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new InvalidOperationException(
                "Lifecycle operator, authorization reference, or reason exceeds the durable receipt contract.");
        var grant = PlatformAccessGrant.Issue(
            tenant!, platformOperator!, authorizationReference!, reason!, expires.Value, now);
        var context = TenantContext.FromPlatformGrant(grant, now);
        context.RequireTenant(assertedTenant);
        if (kind != SharedTenantLifecycleKind.Delete
            && (string.IsNullOrWhiteSpace(release) || jobs is null or < 1
                || storage is null or < 128 || reports is null or < 1))
            throw new InvalidOperationException("Shared lifecycle capacity assignment is invalid.");
        return new(kind, context, grant.OperatorPrincipal, grant.AuthorizationReference, grant.Reason,
            grant.ExpiresUtc, release, jobs, storage, reports, retention, legalCleared,
            identityAuthority);
    }

    public async Task<SharedTenantLifecycleDto> ApplyAsync(
        SharedTenantLifecycleAuthority authority,
        bool execute,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!config.SharedTenancy.Enabled)
            throw new InvalidOperationException("Shared tenancy is not enabled.");
        authority.Tenant.RequireActivePlatformGrant(now);
        var tenant = authority.Tenant.Tenant.Value;
        var existingState = await db.SharedTenantLifecycles.SingleOrDefaultAsync(
            value => value.TenantId == tenant, cancellationToken);
        var assignment = ResolveAssignment(authority, existingState);
        var operationId = OperationId(authority.Kind, authority.AuthorizationReference);

        if (!execute)
            return new(operationId, tenant, authority.Kind.ToString(), "Preflight", "Validated",
                existingState?.ActiveRelease, existingState?.MaxConcurrentJobs,
                existingState?.MaxStorageMb, existingState?.MaxReportSessions,
                existingState?.FenceEpoch, null);

        var operation = await db.SharedTenantLifecycleOperations.SingleOrDefaultAsync(
            value => value.OperationId == operationId, cancellationToken);
        if (operation is null)
        {
            ValidateInitialState(authority.Kind, existingState, tenant);
            operation = NewOperation(operationId, authority, assignment, now);
            db.SharedTenantLifecycleOperations.Add(operation);
            audit?.Stage(
                null,
                $"SHARED_TENANT_{authority.Kind.ToString().ToUpperInvariant()}_STARTED",
                "SharedTenantLifecycle",
                tenant,
                $"authorizationReference={authority.AuthorizationReference}; execute=true",
                operationId,
                actorType: "PlatformOperator",
                actorId: authority.Operator,
                effectiveScopes: "SharedTenantLifecycle");
            if (existingState is null)
            {
                existingState = new SharedTenantLifecycle
                {
                    TenantId = tenant,
                    State = "Provisioning",
                    ActiveRelease = assignment.Release,
                    MaxConcurrentJobs = assignment.Jobs,
                    MaxStorageMb = assignment.Storage,
                    MaxReportSessions = assignment.Reports,
                    CreatedAtUtc = now.UtcDateTime,
                    UpdatedAtUtc = now.UtcDateTime
                };
                db.SharedTenantLifecycles.Add(existingState);
            }
            else
            {
                existingState.State = authority.Kind == SharedTenantLifecycleKind.Delete
                    ? "Deleting" : "Upgrading";
                existingState.FenceEpoch++;
                existingState.UpdatedAtUtc = now.UtcDateTime;
                existingState.Version++;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            ValidateReplay(operation, authority, assignment);
            if (operation.Status == "Completed")
                return ToDto(operation, existingState);
        }

        if (authority.Kind != SharedTenantLifecycleKind.Provision)
        {
            var activePortalWork = await db.PortalExecutionJobs.CountAsync(
                value => value.TenantId == tenant
                         && (value.Status == "Pending" || value.Status == "Running"),
                cancellationToken);
            if (activePortalWork > 0)
            {
                operation.Status = "Draining";
                operation.Phase = "PortalDrain";
                operation.UpdatedAtUtc = now.UtcDateTime;
                operation.Version++;
                await db.SaveChangesAsync(cancellationToken);
                return ToDto(operation, existingState);
            }
        }

        operation.Status = "Pending";
        operation.Phase = "Orchestrator";
        operation.UpdatedAtUtc = now.UtcDateTime;
        operation.FailureCode = null;
        operation.Version++;
        await db.SaveChangesAsync(cancellationToken);

        HttpResponseMessage? response;
        try
        {
            response = await orchestrator.ApplySharedTenantLifecycleAsync(
                authority.Tenant,
                new SharedTenantLifecycleCommand(
                    operationId, authority.Kind, authority.Operator,
                    authority.AuthorizationReference, assignment.Release, assignment.Jobs,
                    assignment.Storage, assignment.Reports, now),
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
                                   || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            operation.Status = "Pending";
            operation.Phase = "OrchestratorUncertain";
            operation.FailureCode = "OrchestratorUnavailable";
            operation.UpdatedAtUtc = DateTime.UtcNow;
            operation.Version++;
            await db.SaveChangesAsync(cancellationToken);
            return ToDto(operation, existingState);
        }

        if (response is null)
        {
            operation.Phase = "OrchestratorUncertain";
            operation.FailureCode = "OrchestratorUnavailable";
            operation.UpdatedAtUtc = DateTime.UtcNow;
            operation.Version++;
            await db.SaveChangesAsync(cancellationToken);
            return ToDto(operation, existingState);
        }
        using var responseLease = response;
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            operation.Status = "Draining";
            operation.Phase = "OrchestratorDrain";
            operation.UpdatedAtUtc = DateTime.UtcNow;
            operation.Version++;
            await db.SaveChangesAsync(cancellationToken);
            return ToDto(operation, existingState);
        }
        if (!response.IsSuccessStatusCode)
        {
            operation.Status = "Pending";
            operation.Phase = "OrchestratorRejected";
            operation.FailureCode = $"Orchestrator{(int)response.StatusCode}";
            operation.UpdatedAtUtc = DateTime.UtcNow;
            operation.Version++;
            await db.SaveChangesAsync(cancellationToken);
            return ToDto(operation, existingState);
        }

        var remote = await response.Content.ReadFromJsonAsync<SharedTenantLifecycleResult>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Orchestrator lifecycle response is empty.");
        if (remote.OperationId != operationId || remote.TenantId != tenant
            || remote.Kind != authority.Kind || remote.Status != "Completed")
            throw new InvalidDataException("Orchestrator lifecycle response does not match the fenced operation.");

        operation.Phase = "PortalCommit";
        operation.UpdatedAtUtc = DateTime.UtcNow;
        operation.Version++;
        if (authority.Kind == SharedTenantLifecycleKind.Delete)
            await PurgeTenantPartitionAsync(tenant, cancellationToken);
        else if (authority.Kind == SharedTenantLifecycleKind.Provision)
            ProvisionNamespaces(tenant, authority.IdentityAuthority, now.UtcDateTime);

        existingState = await db.SharedTenantLifecycles.SingleAsync(
            value => value.TenantId == tenant, cancellationToken);
        existingState.State = authority.Kind == SharedTenantLifecycleKind.Delete ? "Deleted" : "Active";
        existingState.ActiveRelease = assignment.Release;
        existingState.MaxConcurrentJobs = assignment.Jobs;
        existingState.MaxStorageMb = assignment.Storage;
        existingState.MaxReportSessions = assignment.Reports;
        existingState.UpdatedAtUtc = DateTime.UtcNow;
        existingState.DeletedAtUtc = authority.Kind == SharedTenantLifecycleKind.Delete
            ? DateTime.UtcNow : null;
        existingState.Version++;
        operation.Status = "Completed";
        operation.Phase = existingState.State;
        operation.CompletedAtUtc = DateTime.UtcNow;
        operation.FailureCode = null;
        audit?.Stage(
            null,
            $"SHARED_TENANT_{authority.Kind.ToString().ToUpperInvariant()}_COMPLETED",
            "SharedTenantLifecycle",
            tenant,
            $"authorizationReference={authority.AuthorizationReference}; state={existingState.State}",
            operationId,
            actorType: "PlatformOperator",
            actorId: authority.Operator,
            effectiveScopes: "SharedTenantLifecycle");
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(operation, existingState);
    }

    private async Task PurgeTenantPartitionAsync(string tenant, CancellationToken ct)
    {
        // Lifecycle rows and operation receipts are deliberately excluded: they are the external
        // tombstone proving what was erased. Every delete below carries an explicit tenant predicate
        // or is rooted in ids selected from that tenant partition.
        var userIds = db.Users.Where(x => x.TenantId == tenant).Select(x => x.Id);
        var groupIds = db.Groups.Where(x => x.TenantId == tenant).Select(x => x.Id);
        var reportIds = db.Reports.Where(x => x.TenantId == tenant).Select(x => x.Id);
        var folderIds = db.Folders.Where(x => userIds.Contains(x.OwnerId)).Select(x => x.Id);
        var datasetIds = db.Datasets.Where(x => x.TenantId == tenant).Select(x => x.Id);

        await db.PortalExecutionJobs.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.ReportScriptDraftDecisions.Where(x => db.ReportScriptDrafts
            .Where(d => reportIds.Contains(d.ReportId)).Select(d => d.Id).Contains(x.DraftId)).ExecuteDeleteAsync(ct);
        await db.ReportScriptDrafts.Where(x => reportIds.Contains(x.ReportId)).ExecuteDeleteAsync(ct);
        await db.DatasetUserAcls.Where(x => datasetIds.Contains(x.DatasetId)).ExecuteDeleteAsync(ct);
        await db.DatasetAcls.Where(x => datasetIds.Contains(x.DatasetId)).ExecuteDeleteAsync(ct);
        await db.Datasets.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.Reports.Where(x => reportIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.FolderAcls.Where(x => folderIds.Contains(x.FolderId) || groupIds.Contains(x.GroupId)).ExecuteDeleteAsync(ct);
        await db.Folders.Where(x => folderIds.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.ServiceAccounts.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.RefreshTokens.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.UserGroups.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.SharedConnectionUsages.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.SharedConnectionAcls.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.PortalSharedConnections.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.PortalSecrets.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.SharedTenantResources.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.SharedIdentityAuthorities.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.PolicyMachines.Where(x => x.Tenant == tenant).ExecuteDeleteAsync(ct);
        await db.PolicyVersions.Where(x => x.Tenant == tenant).ExecuteDeleteAsync(ct);
        await db.Users.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
        await db.Groups.Where(x => x.TenantId == tenant).ExecuteDeleteAsync(ct);
    }

    private void ProvisionNamespaces(
        string tenant, SharedIdentityAuthorityDefinition? identity, DateTime now)
    {
        if (identity is null)
            throw new InvalidOperationException(
                "Shared provisioning has no signed identity-authority binding.");
        var context = TenantContext.FromVerifiedCredential(tenant);
        foreach (var kind in new[] { "storage", "queue", "index" })
        {
            if (db.SharedTenantResources.Local.Any(x => x.TenantId == tenant && x.Kind == kind && x.LogicalId == "root")
                || db.SharedTenantResources.Any(x => x.TenantId == tenant && x.Kind == kind && x.LogicalId == "root"))
                continue;
            db.SharedTenantResources.Add(new SharedTenantResource
            {
                TenantId = tenant,
                Kind = kind,
                LogicalId = "root",
                ScopedId = context.ScopeKey($"{kind}/root"),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        var authorityId = "shared-" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(tenant)))[..48];
        var existing = db.SharedIdentityAuthorities.Local.SingleOrDefault(x => x.AuthorityId == authorityId)
            ?? db.SharedIdentityAuthorities.SingleOrDefault(x => x.AuthorityId == authorityId);
        if (existing is null)
        {
            db.SharedIdentityAuthorities.Add(new SharedIdentityAuthority
            {
                AuthorityId = authorityId,
                TenantId = tenant,
                PortalHost = identity.PortalHost,
                LoginDomain = identity.LoginDomain,
                Issuer = identity.Issuer,
                ClientId = identity.ClientId,
                ClientSecretReference = identity.ClientSecretReference,
                Enabled = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        else if (existing.TenantId != tenant
                 || existing.PortalHost != identity.PortalHost
                 || existing.LoginDomain != identity.LoginDomain
                 || existing.Issuer != identity.Issuer
                 || existing.ClientId != identity.ClientId
                 || existing.ClientSecretReference != identity.ClientSecretReference)
        {
            throw new InvalidOperationException(
                "The signed provisioning authority conflicts with an existing identity binding.");
        }
    }

    private static (string Release, int Jobs, int Storage, int Reports) ResolveAssignment(
        SharedTenantLifecycleAuthority authority, SharedTenantLifecycle? current)
    {
        if (authority.Kind == SharedTenantLifecycleKind.Delete)
        {
            if (current is null) throw new InvalidOperationException("Shared tenant is not provisioned.");
            return (current.ActiveRelease, current.MaxConcurrentJobs,
                current.MaxStorageMb, current.MaxReportSessions);
        }
        return (authority.TargetRelease!, authority.MaxConcurrentJobs!.Value,
            authority.MaxStorageMb!.Value, authority.MaxReportSessions!.Value);
    }

    private static void ValidateInitialState(
        SharedTenantLifecycleKind kind, SharedTenantLifecycle? state, string tenant)
    {
        if (kind == SharedTenantLifecycleKind.Provision && state is not null)
            throw new InvalidOperationException($"Shared tenant '{tenant}' already has lifecycle state '{state.State}'.");
        if (kind != SharedTenantLifecycleKind.Provision && (state is null || state.State != "Active"))
            throw new InvalidOperationException($"Shared tenant '{tenant}' is not active.");
    }

    private static SharedTenantLifecycleOperation NewOperation(
        string id, SharedTenantLifecycleAuthority authority,
        (string Release, int Jobs, int Storage, int Reports) assignment, DateTimeOffset now) => new()
        {
            OperationId = id,
            TenantId = authority.Tenant.Tenant.Value,
            Kind = authority.Kind.ToString(),
            Status = "Started",
            Phase = "PortalFence",
            PlatformOperator = authority.Operator,
            AuthorizationReference = authority.AuthorizationReference,
            Reason = authority.Reason,
            AuthorizationExpiresUtc = authority.ExpiresUtc.UtcDateTime,
            TargetRelease = assignment.Release,
            TargetMaxConcurrentJobs = assignment.Jobs,
            TargetMaxStorageMb = assignment.Storage,
            TargetMaxReportSessions = assignment.Reports,
            TargetPortalHost = authority.IdentityAuthority?.PortalHost,
            TargetLoginDomain = authority.IdentityAuthority?.LoginDomain,
            TargetIssuer = authority.IdentityAuthority?.Issuer,
            TargetClientId = authority.IdentityAuthority?.ClientId,
            TargetClientSecretReference = authority.IdentityAuthority?.ClientSecretReference,
            StartedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        };

    private static void ValidateReplay(
        SharedTenantLifecycleOperation operation, SharedTenantLifecycleAuthority authority,
        (string Release, int Jobs, int Storage, int Reports) assignment)
    {
        if (operation.TenantId != authority.Tenant.Tenant.Value
            || operation.Kind != authority.Kind.ToString()
            || operation.PlatformOperator != authority.Operator
            || operation.AuthorizationReference != authority.AuthorizationReference
            || operation.Reason != authority.Reason
            || operation.AuthorizationExpiresUtc != authority.ExpiresUtc.UtcDateTime
            || operation.TargetRelease != assignment.Release
            || operation.TargetMaxConcurrentJobs != assignment.Jobs
            || operation.TargetMaxStorageMb != assignment.Storage
            || operation.TargetMaxReportSessions != assignment.Reports
            || operation.TargetPortalHost != authority.IdentityAuthority?.PortalHost
            || operation.TargetLoginDomain != authority.IdentityAuthority?.LoginDomain
            || operation.TargetIssuer != authority.IdentityAuthority?.Issuer
            || operation.TargetClientId != authority.IdentityAuthority?.ClientId
            || operation.TargetClientSecretReference != authority.IdentityAuthority?.ClientSecretReference)
            throw new InvalidOperationException(
                "The authorization reference was already used for a different Shared lifecycle mutation.");
    }

    private static SharedTenantLifecycleDto ToDto(
        SharedTenantLifecycleOperation operation, SharedTenantLifecycle? state) => new(
            operation.OperationId, operation.TenantId, operation.Kind, operation.Status,
            operation.Phase, state?.ActiveRelease, state?.MaxConcurrentJobs,
            state?.MaxStorageMb, state?.MaxReportSessions, state?.FenceEpoch,
            operation.FailureCode);

    private static string OperationId(SharedTenantLifecycleKind kind, string authorizationReference)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}\0{authorizationReference}"));
        return Convert.ToHexStringLower(bytes);
    }
}
