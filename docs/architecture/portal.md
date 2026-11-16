# Architecture: ETL-SQL Portal

The Portal (`ETL-SQL-Portal`) is an ASP.NET Core 10 web application that exposes report execution, snapshot management, subscriptions, and user/group administration through a REST API and a static HTML/JS front-end. It sits at **Tier 5** of the dependency hierarchy, above the shared report hosting/runtime services it calls for execution.

---

## Contents

1. [Tier Placement & Dependencies](#1-tier-placement--dependencies)
2. [Data Model](#2-data-model)
3. [Authentication & Authorization](#3-authentication--authorization)
4. [Middleware Pipeline](#4-middleware-pipeline)
5. [Execution & Session Cache](#5-execution--session-cache)
6. [Subscriptions & Orchestrator Integration](#6-subscriptions--orchestrator-integration)
7. [Health Checks](#7-health-checks)
8. [Integration Testing Strategy](#8-integration-testing-strategy)
9. [API Reference](#9-api-reference)

---

## 1. Tier Placement & Dependencies

```
Tier 5 — ETL-SQL-Portal
            │
            ├── ETL-SQL.ReportHosting   (report sessions, parameters, selective refresh)
            │       ├── ETL-SQL.Reporting
            │       └── ETL-SQL.Engine
            │               └── ETL-SQL.Core
            └── ETL-SQL.Orchestrator  (job scheduling, SQLite history)
```

The portal does **not** reference `ETL-SQL.ReportPlayer`. Browser hosting remains in ReportPlayer; reusable execution/session behavior lives in `ETL-SQL.ReportHosting`.

---

## 2. Data Model

All portal state lives in a single SQLite database managed by EF Core with migration-based schema evolution.

### Entity Summary

```
PortalUser ──< UserGroup >── Group ──< FolderAcl >── Folder ──< Report ──< ReportSnapshot
                                                                         ├──< Subscription ──> PortalUser
                                                                         ├──< ReportFavorite ──> PortalUser
                                                                         ├──< ReportAcl ──> PortalUser / Group
                                                                         ├──< ReportShareLink
                                                                         ├──< ReportEmbedToken
                                                                         ├──< SavedReportView
                                                                         ├──< StudioRecoveryDraft ──> PortalUser (private unsaved Studio edits)
                                                                         ├──< ReportAlert
                                                                         └──< ReportJobLink
Dataset ──< DatasetAcl
PortalUser ──< RefreshToken
AuditLog        (append-oriented portal event table)
PortalExecutionJob (durable portal execution/refresh polling state)
PortalSecret            (encrypted secret store: SECRET:name for Portal-hosted execution)
PortalSharedConnection  (governed connection catalog: SHARED:alias; Target/OptionsJson encrypted at rest)
AdminServiceRun         (per-run ledger for the native admin background services)
ServiceAccount          (scoped machine identities, capped by their owner's authority)
WorkloadIdentityReplay  (tenant-scoped, hashed one-use assertion and approval IDs)
SharedIdentityAuthority (tenant-owned OIDC authority metadata; client secrets remain external)
PolicyVersionEntity / PolicyMachineEntity   (enterprise policy authority + machine registry)
GatewayEnrollmentEntity                     (gateway machine enrollment tokens and bootstrap metadata)
GatewayAmbiguousWriteCase / GatewayAmbiguousWriteEvent (on-premises ambiguous write incident tracking and resolution ledger)
SharedConnectionAcl / SharedConnectionUsage (per-connection use grants and consumers)
GroupStudioCapability   (deny-by-default Studio capabilities granted to a group)
DatasetUserAcl          (per-user dataset grant; a sibling table because the rolling-expand
                         migration contract forbids relaxing DatasetAcl.UserId to nullable)
AlertNotification       (delivery ledger for report alerts)
SubscriptionDelivery    (at-most-once subscription delivery ledger, keyed by trigger + recipient)
AuditOutboxMessage      (durable remote-audit outbox; fail-closed when delivery is required)
ReportAccessRequest     (access requests and their approval decisions)

Shared tenancy and lifecycle:
SharedTenantResource / SharedTenantLifecycle / SharedTenantLifecycleOperation

Stewardship workflow state — decisions and derived state, never the asset metadata itself, which
stays in .etlsql/.rptsql sources and the lineage catalog:
StewardshipSettings / StewardshipResolutionCategory / StewardshipGlossaryTerm
StewardshipFinding ──< StewardshipFindingDecision
StewardshipAssetBadge / StewardshipAssetReview / StewardshipScan

Authoring workflow:
ReportScriptDraft ──< ReportScriptDraftDecision   (draft → review → publish; opt-in)
StudioPrivateDraft ──< StudioPrivateDraftRevision (an owner's unpublished Studio document and its saves)
StudioPrivateDraft ──> PortalUser                  (owner; no one else can read it)
```

### Key Design Decisions

- **`PortalUser` extends `IdentityUser<int>`** — all password hashing, lockout counters, and token validation delegate to ASP.NET Identity. Custom columns (`IsActive`, `MustChangePassword`, `CreatedAt`) are added via the EF `OnModelCreating` override in `PortalDbContext`.

- **Soft-delete on `Report`** — `IsDeleted = true` hides records from all queries. Snapshots are preserved on disk after soft-deletion; hard deletion is a manual operation.

- **Alert delivery uses named notifications** — `ReportAlert` owns the condition and an
  `AlertNotification` collection naming Orchestrator notifications. v0.22.0 removes the obsolete
  inline recipient/SMTP columns from both catalog providers. The pre-deployment migration is a
  deliberate exception to rolling expansion: stop older Portal hosts before applying it.
  Mixed-version rolling operation across that migration is unsupported. Migration contract tests
  permit only those two column drops and real provider tests preserve definitions and attachments.

- **Folder and report ACLs** — `FolderAcl` remains group-based for broad catalog access.
  `ReportAcl` adds report-scoped user or group grants for self-service access approvals, so an owner
  can approve one restricted report without granting the entire folder. Effective report permission
  combines admin status, folder ownership, folder ACLs, report creator ownership, and report-level
  ACLs.

- **`Subscription.ScriptPath`** — the Orchestrator job script is generated at subscription-creation time and stored as a `.etlsql` file under `ScriptRootPath`. This path is recorded so the job file can be cleaned up on subscription deletion.

- **Shared connection credentials** — stored in the governed Portal shared-connection catalog with encrypted options or `SECRET:name` references. Secret values are never returned to clients in API responses.

- **Catalog and embedding records** — favorites, share links, embed tokens, saved views, alerts, dependencies, and usage metrics are first-class portal records exposed through the report/admin API and ETL-SQL portal scripting commands.

- **`PortalSecret` values are write-only** (v0.15.0) — encrypted by `PortalSecretStoreService` with the cluster-wide Data Protection key ring (purpose `ETL_SQL.Portal.SecretStore.v1`); no API returns a value after write. With `Governance:Secrets:Provider=PortalStore`, `PortalStoreSecretProvider` resolves `SECRET:name` for Portal-hosted script execution (registered as a resolve-time dispatcher over the `AddEtlSqlEngine` default). The `secret-store-keyring` health check decrypt-probes every stored secret so an HA node with the wrong key ring fails fast; `POST /api/admin/secrets/verify-all` is the backup/restore validation surface.

- **`PortalSharedConnection` holds references, never credential values** (v0.15.0) — `SharedConnectionValidator` (Core) rejects raw credentials at write time, and `Target`/`OptionsJson` are additionally encrypted at rest via the PII converter. With `Governance:ConnectionCatalog:Provider=Portal`, `PortalCatalogConnectionProvider` expands `SHARED:alias` in CREATE CONNECTION: the declared connector type must match the entry, script options cannot override cataloged credential fields, and the last-used touch is best-effort so audit fail-closed can never break execution.

- **`AdminServiceRun` is the native admin services ledger** (v0.15.0) — the failure-digest, backup-report, and capacity-report services (`AdminDigestServiceBase` subclasses, configured under `Portal:AdminServices`) each run behind an `IClusterLockStore` lease with a one-interval TTL (exactly one node per interval; the lock is deliberately not renewed — expiry re-enables the next run), retry delivery, send email through the shared `IAdminNotificationSender`, audit `ADMIN_SERVICE_RUN`, and prune their own history per `RunHistoryRetentionDays`.

---

## 3. Authentication & Authorization

### JWT Flow

```
Client                      Portal
  │── POST /api/auth/login ──>│
  │<── { token, refreshToken, expiresAt }│   (defaults: access 60 min, refresh 7 days)
  │                            │
  │── GET /api/... Bearer token│   (validated by JwtBearer middleware)
  │                            │
  │── POST /api/auth/refresh ──│   (issues new token + rolling refresh token)
  │<── { token, refreshToken, expiresAt }│
```

**Token validation** uses `Microsoft.AspNetCore.Authentication.JwtBearer`. The Portal validates the
issuer (`etl-sql-portal`), audience (`etl-sql-portal-api`), signing key, and lifetime, with a 30-second
clock skew. `TokenService` supplies those issuer/audience constants and the signing-key ring supplies
the validation keys.

The browser client is authored in `BrowserSources/api.ts`; its authentication and HTTP transport
live in `BrowserSources/portal-http.ts`. Their generated delivery URLs remain `/js/api.js` and
`/js/portal-http.js`. Mutation request types come from the C# browser declarations. JSON responses
remain `unknown` until validated; login and refresh responses use the same authentication validator.

The transport preserves native headers and request bodies and attaches stored credentials only to
same-origin API requests. Concurrent expired requests share a refresh; late old-token responses use
the already rotated token. Abort signals cancel individual refresh waiters without cancelling other
callers. Replacing or clearing a session invalidates pending requests, so a late refresh cannot restore
credentials or overwrite a new identity. Logout clears local credentials before server revocation.
If its access token has expired, revocation reuses a pending refresh or obtains credentials privately;
it never stores that pair, and it redirects only while the signed-out session is still current.

### JWT Secret Bootstrap Problem

The JWT signing key is read from `PortalConfig.Jwt.Secret` at startup — before EF migrations run and before `WebApplicationFactory` can inject test configuration. To avoid an early-exit `return 1` that would prevent test hosts from capturing the `IHost`, the validation is deferred to `JwtSecretValidationService`:

```csharp
// In Program.cs — placeholder so SymmetricSecurityKey ctor doesn't throw on empty bytes
var rawSecret = string.IsNullOrEmpty(portalConfig.Jwt.Secret)
    ? new byte[32]          // 32 zero bytes — replaced by PostConfigure in tests
    : Encoding.UTF8.GetBytes(portalConfig.Jwt.Secret);
```

`JwtSecretValidationService` (an `IHostedService`) runs after `Build()`, checks the secret length, and calls `IHostApplicationLifetime.StopApplication()` if it is missing or too short. In tests, `PostConfigure<JwtBearerOptions>` replaces the signing key before any request is handled.

### Role-Based Authorization

**Eight** ASP.NET Identity roles are seeded on first run:

| Role | Purpose |
| :--- | :--- |
| `Admin` | Full administration |
| `Publisher` | Create folders, publish reports, manage subscriptions |
| `Viewer` | Browse, run, and export accessible reports |
| `OrchestratorManager` | Scheduled jobs and execution history; no Admin panel |
| `OrchestratorViewer` | Read-only access to scheduled jobs, run history, and metrics; no creation/management |
| `FleetReader` | Scoped cross-environment fleet status reads |
| `StewardshipViewer` | Read the stewardship dashboard |
| `DataSteward` | Stewardship decisions and the data-quality quarantine queue |
| `StewardshipManager` | Stewardship configuration: thresholds, checks, glossary |


Authorization has **two independent axes**, and they are not interchangeable: a **role** decides
which class of operation a caller may perform at all, and an **ACL** decides which resources they
may perform it on. Controllers use `[Authorize(Roles = …)]` and named policies
(`GovernanceRead`/`GovernanceDecide`/`GovernanceConfigure`, `DataQualityStewardAccess`,
`OrchestratorAccess`); folder and report ACLs are resolved through `FolderPermissionService`.

Notably, `Manage` on a folder is authority over the **reports in it**, not over the folder itself —
reading or re-granting a folder ACL, creating a subfolder, and deleting a folder are Admin-role
acts. Without that split the strongest ACL grant would be self-propagating.

`FolderPermission` is `Read` < `Execute` < `Author` < `Manage` **by authority**, which is not its
declaration order: `Author` is stored as `3` and `Manage` as `2`, because inserting `Author` in its
rightful place would have renumbered `Manage` and silently reinterpreted every ACL row already in
the database. Always compare with `FolderPermissions.AtLeast()`/`Rank()`; a bare `>=` grants `Author`
everything `Manage` has. `FolderPermissionOrderingTests` fails the build on any ordinal comparison.

Report-opening, catalog, execution, refresh, and saved-view paths use effective report permission,
which layers report-level ACLs over folder ACLs. Access-request approval defaults to report-scoped
`Read` and may grant higher `FolderPermission` levels only when an owner or administrator chooses
that scope explicitly.

---

## 4. Middleware Pipeline

```
Request
  │
  ├── UseStaticFiles          (serve /wwwroot — HTML, CSS, JS)
  ├── UseAuthentication       (validate JWT, populate ClaimsPrincipal)
  ├── UseAuthorization        (enforce [Authorize] attributes)
  ├── MustChangePasswordMiddleware
  │       If authenticated + /api/* + not in allowlist + MustChangePassword == true
  │       → 403 { error, redirect: "/login.html?changePassword=true" }
  └── MapControllers          (route to controller actions)
```

**`MustChangePasswordMiddleware`** allowlist (requests that pass through even when password change is required):
- `POST /api/auth/change-password`
- `POST /api/auth/login`
- `POST /api/auth/logout`
- `POST /api/auth/refresh`

All other `POST /api/*` requests are blocked until the password is changed. `GET` requests to `/api/*` are not blocked — users can browse reports while the middleware is active, but they cannot execute, subscribe, or make any writes.

---

## 5. Execution & Session Cache

Report execution is asynchronous:

```
POST /api/reports/{id}/execute
  └── ExecutionJobService.EnqueueExecutionAsync()
        └── Runs ETL-SQL script via ReportHosting.DashboardService
              └── On completion: writes snapshot, notifies SessionCache

GET /api/jobs/{jobId}          — poll job status
GET /api/reports/{id}/snapshot — fetch completed snapshot JSON
```

**Supported topology:** one active Portal process per portal SQLite database and
script/snapshot/dataset storage root. Portal nodes are allowed to run concurrently when they share
the configured database and artifact storage roots. Startup-critical singleton work, such as EF
migrations, is serialized with the database-backed cluster lock instead of a node-local filesystem
lock.
The execution semaphore, interactive session cache, ASP.NET rate-limit partitions, and PDF quota
are therefore intentionally process-local.

**`ExecutionJobService`** is a singleton/hosted service with weighted interactive/refresh admission
limited by `MaxConcurrentReportExecutions`. Jobs that exceed the slot limit are queued. Every job is
also written to `PortalExecutionJobs`, so `GET /api/jobs/{jobId}` remains meaningful after a
restart. A filtered unique index permits only one `Pending`/`Running` refresh per report. Startup
marks abandoned jobs and report refresh status as `Cancelled` with an interruption reason; it
does not claim that vanished work completed successfully. While the process is live, the cluster
node heartbeat is also treated as a local-work lease: if heartbeat renewal fails long enough for
this node's registry lease to expire, `ExecutionJobService` cancels every locally running execution
before another node can safely take over work.

Shutdown refuses new submissions, cancels running and queued jobs, and waits for accepted requests,
terminal status writes and cancellation monitors before disposing execution gates. Both synchronous
and asynchronous disposal wait for this same drain, including hosts that remove the hosted-service
registration. Cancelling a `StopAsync` caller's wait leaves the shared drain running.

**`SessionCache`** is a singleton hosted service that holds in-memory execution sessions for
active result streaming. It evicts idle sessions after `SessionCacheTtlMinutes` and enforces a max
size of `SessionCacheMaxSize`. The cache is intentionally not persisted: after restart, the next
interaction creates a new session from the report script/current snapshot rather than reporting a
lost session as successful work. Load-balanced HA deployments must keep interactive report traffic
sticky to one Portal node; the portal emits the `ETLSQL_PORTAL_AFFINITY` cookie by default so the
load balancer can preserve affinity for those process-local sessions.

---

## 6. Subscriptions & Orchestrator Integration

When a subscription is created:

1. The portal generates an ETL-SQL job script (`.etlsql`) in `ScriptRootPath` that runs the report, formats the output, and sends the email via a `SEND EMAIL` statement.
2. The script path is stored in `Subscription.ScriptPath`.
3. The job is registered with the **ETL-SQL Orchestrator** using its SQLite job table.

The `OrchestratorPollerService` (hosted service) periodically checks whether the Orchestrator's SQLite database is reachable. The `OrchestratorDbLocator` service resolves the Orchestrator database path from the portal's configuration or from the default well-known paths.

If the Orchestrator is offline at subscription creation time, the record is saved in the portal database and the job will be registered the next time the poller finds the Orchestrator available.

---

## 7. Health Checks

Three `IHealthCheck` implementations registered with `AddHealthChecks()`:

| Name | Class | Failure Mode |
| :--- | :--- | :--- |
| `db` | `PortalDbHealthCheck` | `Unhealthy` if `db.Users.CountAsync()` throws |
| `orchestrator` | `OrchestratorHealthCheck` | `Degraded` if Orchestrator DB path not found |
| `execution` | `ExecutionCapacityHealthCheck` | Reports the single-instance topology, durable active execution count, configured cap, SMTP connections, and active subscriptions |

The `/health` endpoint uses a custom `ResponseWriter` that serializes the `HealthReport` to a structured JSON document. It is mapped with `.AllowAnonymous()` so monitoring tools do not need a JWT.

The `/healthz` endpoint is the lighter load-balancer probe. It bypasses the richer health-check
pipeline and returns only database, artifact-storage, and node-registry lease-store connectivity as
`ok`/failure values, with HTTP 503 when any of those required dependencies is unavailable.

---

## 8. Integration Testing Strategy

Tests live in `tests/ETL-SQL.Portal.Tests` and use `Microsoft.AspNetCore.Mvc.Testing`.

### PortalMarker

`WebApplicationFactory<T>` needs a type from the entry-point assembly. `PortalMarker` is a stable, uniquely-named type in `ETL-SQL-Portal` that avoids binding tests to any top-level `Program` class:

```csharp
public class PortalWebFactory : WebApplicationFactory<PortalMarker> { ... }
```

### Test Configuration Injection

`ConfigureWebHost` performs three overrides:

1. **`ConfigureAppConfiguration`** — injects an in-memory config dictionary with a temp-directory database path, script root, snapshot directory, and test JWT secret.
2. **`ConfigureServices`** — replaces `PortalDbContext` with the temp-path SQLite, replaces the `PortalConfig` singleton, and calls `PostConfigure<JwtBearerOptions>` to replace the signing key with the test secret.
3. **`RemoveAll<IHostedService>`** — removes `JwtSecretValidationService` (and all other hosted services) so the validation guard does not fire against the unset production secret.

### Hosted-Service Lane

Because ordinary API tests strip every hosted service, a separate lane exercises the real
`IHostedService` pipeline: `HostedPortalFactory` (subclass of `PortalWebFactory`) keeps all
hosted services against the same isolated temp-directory databases, defaults to a valid dataset
at-rest key and one-second poll/purge intervals (`Portal:Orchestrator:PollIntervalSeconds`,
`Portal:Jwt:RefreshTokenPurgeIntervalSeconds`), and accepts an injectable `TimeProvider`
(registered in `Program.cs`, default `TimeProvider.System`) so time-based maintenance decisions
are deterministic. `HostedServiceLaneTests` runs in the dedicated `portal-hosted` test process
(and as a separate invocation within the `portal`, `full`, and `release` lanes), so unrelated
Portal classes cannot consume its startup/shutdown budget or share background-service state. It
covers: full-pipeline startup health plus
instance-lock acquisition, the fatal JWT/dataset-key startup validators actually stopping the
host, the machine-fallback opt-in, and the in-host refresh-token purge honoring a pinned clock.

### Test Isolation

The SQLite database is shared across all tests in a single `IClassFixture<PortalWebFactory>` run. Key decisions to prevent cross-test interference:

- **Static `_adminToken`** — `GetAdminTokenAsync()` acquires a `SemaphoreSlim`, checks if a token is already cached, and returns it. Only the first caller performs the login + password-change flow.
- **`Login_WithNonExistentUser_Returns401`** — uses a non-existent username (`no_such_user_xyz`) rather than a wrong password for the admin account, avoiding incrementing the admin's lockout counter.
- **`MustChangePassword_BlocksApiUntilChanged`** — creates a dedicated test user rather than using the admin account, so the password-change flow does not affect other tests.

---

## 9. API Reference

All endpoints require a `Bearer` JWT token unless marked **Public**.

### Authentication

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| POST | `/api/auth/login` | Public | Login; returns `{ token, refreshToken, expiresAt }` |
| POST | `/api/auth/refresh` | Public | Refresh access token |
| POST | `/api/auth/change-password` | Any | Change own password |
| POST | `/api/auth/logout` | Any | Invalidate the caller's account sessions |

### Folders

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/folders` | Any | List all folders (filtered by ACL) |
| POST | `/api/folders` | Admin, Publisher | Create folder |
| GET | `/api/folders/{id}` | Any | Get folder by id |
| PUT | `/api/folders/{id}` | Admin, Publisher | Rename or move a folder with its opened version |
| DELETE | `/api/folders/{id}` | Admin | Delete an empty folder, or its descendants and reports with `cascade=true` |
| GET | `/api/folders/{id}/acl` | Admin | List ACL entries |
| GET | `/api/folders/{id}/acl/catalog` | Admin | Read ACL entries, folder version, and permission names/ranks from one snapshot |
| POST | `/api/folders/{id}/acl` | Admin | Grant a permission; receipt identifies the folder, group, permission, and new version |
| DELETE | `/api/folders/{id}/acl/{groupId}` | Admin | Revoke a permission; receipt identifies the folder, group, and new version |

The admin folders panel is owned by strict TypeScript controller, API, and response-validation modules
under `BrowserSources/admin-folders*.ts`. It validates and displays the whole returned hierarchy;
parent choices exclude the edited folder and its descendants. Folder editors retain their opened
versions, and ACL changes use the displayed snapshot version. Permission labels and authority order
come from the server: `Author` remains stored as `3`, while `Manage` remains stored as `2`.
Superseded reads cannot render under another folder, and delayed confirmations keep their original
target. Rejected writes preserve drafts; unconfirmed outcomes block further writes until explicit
refresh succeeds. After refresh confirms an unconfirmed grant, its retained picker can retry with the
confirmed snapshot version. Ordinary refresh does not silently upgrade an opened grant or folder edit.
HTTP 204 confirms folder deletion; grant and revoke require HTTP 200 and a matching versioned receipt.

**Ownership semantics:** `Folder.OwnerId` (the creator, or a transfer target) implies effective
`Manage`, resolved centrally in `FolderPermissionService` alongside group ACLs — the same rule
applies on every path that knows the caller (controllers, dataset permission evaluation,
subscription delivery reauthorization). Deleting a user requires explicitly reassigning their
folders, reports, and datasets via `DELETE /api/admin/users/{id}?reassignTo=<userId>` (409 with an
owned-resource inventory otherwise); personal artifacts — subscriptions (plus their Orchestrator
jobs/trigger scripts), alerts, saved views, favorites, share/embed capabilities, refresh tokens —
are deleted with the user.

### Reports

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/folders/{folderId}/reports` | Any | List reports in folder |
| POST | `/api/reports` | Manage + `ReportPublish` | Publish a report in SourceControlled mode |
| POST | `/api/reports/validate` | `ScriptPreview` | Validate a report script in SourceControlled mode |
| GET | `/api/reports/{id}` | Any | Get report metadata |
| GET | `/api/reports/{id}/dependencies` | Any | Get report dependencies |
| GET | `/api/reports/{id}/history` | Any | Get report history |
| PUT | `/api/reports/{id}` | Author | Update metadata; moving folders requires Manage, and changing the script path requires `ReportPublish` in SourceControlled mode |
| POST | `/api/reports/{id}/favorite` | Any | Mark report as favorite |
| DELETE | `/api/reports/{id}/favorite` | Any | Remove favorite |
| POST | `/api/reports/{id}/share-links` | Admin, Publisher | Create share link |
| GET | `/api/reports/{id}/share-links` | Admin, Publisher | List share links |
| DELETE | `/api/reports/{id}/share-links/{token}` | Admin, Publisher | Revoke share link by token |
| DELETE | `/api/reports/{id}/share-links/by-id/{linkId}` | Admin, Publisher | Revoke share link by id |
| GET | `/api/share/{token}` | Public | Resolve share link |
| POST | `/api/reports/{id}/embed-tokens` | Admin, Publisher | Create embed token |
| GET | `/api/reports/{id}/embed-tokens` | Admin, Publisher | List embed tokens |
| DELETE | `/api/reports/{id}/embed-tokens/{token}` | Admin, Publisher | Revoke embed token by token |
| DELETE | `/api/reports/{id}/embed-tokens/by-id/{tokenId}` | Admin, Publisher | Revoke embed token by id |
| GET | `/api/embed/{token}` | Public | Resolve embed token |
| GET | `/api/reports/{id}/saved-views` | Any | List saved views |
| POST | `/api/reports/{id}/saved-views` | Any | Create saved view |
| PUT | `/api/reports/{id}/saved-views/{viewId}` | Any | Update saved view |
| DELETE | `/api/reports/{id}/saved-views/{viewId}` | Any | Delete saved view |
| GET | `/api/reports/{id}/alerts` | Any | List report alerts |
| POST | `/api/reports/{id}/alerts` | Any | Create report alert |
| PUT | `/api/reports/{id}/alerts/{alertId}` | Any | Update report alert |
| DELETE | `/api/reports/{id}/alerts/{alertId}` | Any | Delete report alert |
| GET | `/api/reports/{id}/parameters` | Any | Get declared report parameters |
| DELETE | `/api/reports/{id}` | Manage | Soft-delete a report; `cascade=true` disables active subscriptions, while attached refresh jobs block deletion |
| POST | `/api/scripts/upload` | Admin, Publisher | Upload script file |
| GET | `/api/reports/available-scripts` | Authenticated + `ScriptRead` | List available script files in SourceControlled mode |
| GET | `/api/maps/custom` | Any | List custom map assets |

The admin reports/publish panel is owned by `BrowserSources/admin-reports.ts`, with typed API and
response-validation modules. The tenant-scoped admin catalog uses the reflected `AdminReportDto`;
script validation and publication inputs use the server's report declarations. Studio policy controls
source-path visibility and publication. CatalogOnly opens Studio; Disabled hides publication controls.
Catalog, folder, validation, and script-picker reads cannot replace newer requests or closed drafts.
The script picker preserves current filtering and supports keyboard selection and retry.

Editors retain their opened report version across refreshes; matching completed receipts confirm
HTTP 201 publication and HTTP 200 updates. Edits made while a publication completes retain its confirmed
identity for the next save. A late write cannot close or retarget a newer draft. Rejected writes preserve
input, and unconfirmed writes require successful explicit refresh. An unconfirmed publication must be
reopened from the catalog before editing so retry cannot create a duplicate. HTTP 204 confirms deletion
with `cascade=true`, which disables active subscriptions; attached refresh jobs still prevent deletion.

### Execution

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| POST | `/api/reports/{id}/execute` | Any | Start execution job |
| GET | `/api/jobs/{jobId}` | Any | Poll job status |
| GET | `/api/reports/{id}/snapshot` | Any | Get snapshot JSON |
| GET | `/api/reports/{id}/manifest` | Any | Get current manifest JSON. For a report that reads the viewer's identity this runs it in the caller's own session, under their identity, stores nothing, and is audited as `VIEW_REPORT_LIVE` |
| POST | `/api/reports/{id}/live/reset` | Any | Run the report again in the caller's own live session (204); 409 for a report that is not shown live. A service account needs `reports.execute` here and on the live manifest and export |
| GET | `/api/reports/{id}/snapshot/manifest` | Any | Get snapshot manifest |
| POST | `/api/reports/{id}/refresh` | Any | Rebuild snapshot |
| POST | `/api/reports/{id}/parameter` | Any | Set one parameter value |
| POST | `/api/reports/{id}/parameters` | Any | Set parameter values |
| POST | `/api/reports/{id}/drill` | Any | Drill in/up for one visual |
| POST | `/api/reports/{id}/refresh-visuals` | Any | Selectively refresh named visuals |

### Datasets

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/datasets` | Any | List accessible datasets |
| GET | `/api/datasets/{id}` | Dataset View | Get dataset metadata |
| GET | `/api/datasets/{id}/rows` | Dataset View | Get preview column metadata and registered row count |
| GET | `/api/datasets/{id}/data` | Dataset View | Get paged cache rows with search, sort, and column filters |
| GET | `/api/datasets/{id}/data/stats` | Dataset View | Get statistics for column-filtered cache rows |
| GET | `/api/datasets/{id}/column/{colName}/values` | Dataset View | Get distinct values with optional search and limit |
| GET | `/api/datasets/{id}/data/export` | Dataset View | Export filtered and sorted data as CSV or XLSX |
| POST | `/api/datasets/{id}/refresh` | Dataset Refresh | Queue refresh or mark an unowned dataset stale |
| GET | `/api/datasets/{id}/refresh-status` | Dataset View | Get dataset refresh status |
| PATCH | `/api/datasets/{id}` | Dataset Edit | Update TTL; changing access level also requires Manage |
| DELETE | `/api/datasets/{id}` | Dataset Manage | Delete dataset |
| GET | `/api/datasets/{id}/acl` | Dataset View | List group and user grants |
| POST | `/api/datasets/{id}/acl` | Dataset Manage | Add a group grant |
| DELETE | `/api/datasets/{id}/acl/{groupId}` | Dataset Manage | Remove a group grant |
| DELETE | `/api/datasets/{id}/acl/user/{userId}` | Dataset Manage | Remove a direct user grant |

All dataset endpoints require authentication and the Reporting module. `DatasetPermissionService`
resolves tenant-scoped access: any grant permits View, `Refresh` or higher permits refresh, `Editor`
or higher permits Edit, and `Owner` permits Manage. Administrators receive Owner within their tenant.

`BrowserSources/datasets-admin.ts` owns the administration controller, `datasets-view.ts` owns its
markup, and `dataset-responses.ts` validates untrusted results against C#-generated declarations.
The served `/js/datasets-admin.js` URL and `createDatasetsAdmin` export remain stable. Read generations,
abort signals, and disposal guard catalog, row, statistics, permissions, picker, and lineage reads.
Unavailable rows clear counts; incomplete statistics clear the footer. Statistics apply to column
filters; global search applies to displayed rows. Parquet character memory becomes scalar strings
before JSON serialization, preserving Unicode, empty strings, and nulls.
Numeric comparisons exclude null cells; the explicit `is_null` filter selects them. The same
filtering applies to rows, statistics, and exports. Password-encrypted reads share read-only input
access so concurrent viewer and statistics requests can read the same cache.

Saves use the version captured when the form opens. Blank TTL is sent as an empty string so the
server clears expiry; null TTL means leave the value unchanged in a partial API update. Save and ACL
receipts must advance the observed version; deletion requires HTTP 204 and refresh requires HTTP 202.
A queued refresh receipt confirms submission. An unconfirmed mutation blocks retries until inspection
and an explicit successful catalog refresh, including after changing administration tabs. Picker
focus stays within the viewer; Escape dismisses the picker first and restores its opener.

### Export

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/reports/{id}/export/csv` | Any | Export visual as CSV |
| GET | `/api/reports/{id}/export/pdf` | Any | Export snapshot as PDF |

### Subscriptions

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/subscriptions` | Any | List own subscriptions |
| GET | `/api/subscriptions/{id}` | Any | Get subscription |
| POST | `/api/subscriptions` | Any | Create subscription |
| PUT | `/api/subscriptions/{id}` | Any | Update subscription |
| DELETE | `/api/subscriptions/{id}` | Any | Delete subscription |
| GET | `/api/subscriptions/{id}/history` | Any | Scheduler trigger history |
| GET | `/api/smtp-aliases` | Any | List usable SMTP aliases |
| GET | `/api/admin/subscriptions/catalog` | Admin | Browse paged subscriptions with search and status filters |
| POST | `/api/admin/subscriptions/bulk-status` | Admin | Change selected subscription states using ID/version snapshots |

The admin subscription controller is owned by `BrowserSources/admin-subscriptions.ts`, with typed
API and response-validation modules. Parameter editing has its own owner in
`admin-subscription-parameters.ts`. Contracts come from reflected C# subscription and parameter DTOs,
`JobHistoryEntry`, and the named bulk-status receipt. Reads validate complete catalogs and definitions;
failed reads remain distinct from confirmed empty results. Dialog responses and write completion are
bound to the opened dialog and session, so late work cannot replace a newer draft or history view.

Parameter saves capture the original subscription ID, report ID, version, and all field values before
awaiting. They use the definitions already loaded for that editor. An empty dictionary clears overrides;
`null` leaves them unchanged. Conflicts preserve the draft and opened version. Unconfirmed changes block
further writes until explicit refresh reads the affected IDs, including IDs outside the current page or
filter. The parameter dialog includes this refresh action. Matching HTTP 200 receipts confirm updates,
HTTP 204 confirms deletion, and bulk receipts identify updated, conflicting, and missing targets.

History describes credential-free scheduler triggers, not delivery outcomes. A confirmed empty read
returns an empty list; a store read failure returns HTTP 503 with a sanitized message. Delivery state
continues to be owned by `SubscriptionDeliveryService`.

### Admin — Settings and page composition

`BrowserSources/pages/admin.ts` owns the admin page composition. It loads each tab once, retains its
controller and drafts while switching tabs, and disposes every mounted owner on `pagehide`.
`admin-settings.ts`, `admin-settings-api.ts`, and `admin-settings-responses.ts` own the independent
Orchestrator and branding forms. Complete reflected C# response contracts are validated before use;
failed reads preserve drafts and disable saving until a successful refresh.

Orchestrator reads expose the URL and `hasApiKey`, never the key. A null key in an update preserves
the current key; an empty string clears it. HTTP 204 confirms a save. Transport failures, HTTP 5xx,
and unexpected success statuses require explicit saved-settings refresh before another write.
Late reads and completed saves cannot replace newer edits or the other form's draft. Connection
testing uses the saved configuration and is disabled while the Orchestrator form has unsaved edits.
Failed or malformed status reads are unconfirmed, not a confirmed offline result. Accepted branding
invalidates earlier public branding reads before updating the header, title, and footer.

Both settings services initialize asynchronously at startup. Updates validate and atomically replace
their sidecar files before publishing an immutable runtime snapshot. Failed persistence leaves the
previous values active. Explicit Orchestrator clears override environment defaults after restart;
stored keys remain protected by Data Protection. The proxy captures its destination and key from
one snapshot for each request, including shared-tenant lifecycle commands.

### Admin — Users & Groups

Creation and updates resolve requested roles against the canonical Identity role records. Unknown
roles return HTTP 400 before account or permission changes. New accounts, their role assignments,
and the creation audit/outbox records share one transaction. Creation receipts name the roles actually
assigned. Shared tenancy selects users within their tenant and changes role links by user ID, preserving
equal usernames across tenants without invoking Identity's global username-update validation.

The users panel is owned by `BrowserSources/admin-users.ts`, with typed API and response-validation
modules. It loads role choices from the stored Identity role catalog, preserves an editor's opened
version and existing roles when no role change was selected, and validates mutation receipts before
reporting completion. Role-catalog reloads preserve editor drafts. Password resets require HTTP 204.
Unconfirmed writes block further mutations until an explicit user refresh succeeds; background
filtering does not clear that state.

The groups and membership panel is owned by `BrowserSources/admin-groups.ts`, with typed API and
response-validation modules. Group editors retain their opened version across catalog refreshes.
Member catalogs return a group ID and version from the same database snapshot as the displayed rows;
membership writes use that version. Named C# receipts identify the group and users actually changed,
including skipped users and blocked deletions. The user picker supports server pagination and preserves
its selection when a write is rejected. Superseded reads, late confirmations, and disposed work cannot
change another group's view. Explicit refresh reconciles unconfirmed outcomes, including a selected
group that was deleted, before enabling further writes.

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/roles` | Admin | List the stored role names for identity administration |
| GET | `/api/admin/users` | Admin | List users |
| POST | `/api/admin/users` | Admin | Create user |
| GET | `/api/admin/users/{id}` | Admin | Get user |
| PUT | `/api/admin/users/{id}` | Admin | Update user |
| DELETE | `/api/admin/users/{id}` | Admin | Delete user |
| POST | `/api/admin/users/{id}/reset-password` | Admin | Reset password |
| POST | `/api/admin/users/{id}/revoke-tokens` | Admin | Revoke all sessions |
| GET | `/api/admin/groups` | Admin | List groups |
| GET | `/api/admin/groups/catalog` | Admin | Page and filter groups |
| POST | `/api/admin/groups` | Admin | Create group |
| GET | `/api/admin/groups/{id}` | Admin | Get group |
| PUT | `/api/admin/groups/{id}` | Admin | Update group |
| DELETE | `/api/admin/groups/{id}` | Admin | Delete group |
| POST | `/api/admin/groups/bulk-delete` | Admin | Delete selected versioned groups with per-group results |
| GET | `/api/admin/groups/{id}/members` | Admin | List group members |
| GET | `/api/admin/groups/{id}/members/catalog` | Admin | Page members with their group ID and snapshot version |
| POST | `/api/admin/groups/{id}/members` | Admin | Add member |
| DELETE | `/api/admin/groups/{id}/members/{userId}` | Admin | Remove member |
| POST | `/api/admin/groups/{id}/members/bulk-add` | Admin | Add users and report changed and skipped IDs |
| POST | `/api/admin/groups/{id}/members/bulk-remove` | Admin | Remove users and report changed and skipped IDs |

### Admin — Shared Connections

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/connections` | Admin | List governed shared connections |
| PUT | `/api/admin/connections/{alias}` | Admin | Create or update a governed shared connection |
| DELETE | `/api/admin/connections/{alias}` | Admin | Delete a governed shared connection |

### Admin — Audit

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/audit` | Admin | Browse audit log (paginated) |
| GET | `/api/admin/audit/export/csv` | Admin | Download audit log as CSV |

The audit catalog returns `PagedResult<AuditLogDto>` with `items`, `total`, `page`, and `pageSize`.
The count and page share a database read transaction. Pages are clamped to the available range;
entries sort by timestamp and then event ID, both descending. Account lookup is tenant-scoped and
optional, so system events and events whose account is unavailable remain visible without exposing
another tenant's username. CSV export applies the action/user filters and returns the newest 10,000
matching entries at most; the admin page displays this limit.

`BrowserSources/admin-audit.ts`, `admin-audit-api.ts`, and `admin-audit-responses.ts` own the audit
controller, authenticated requests, and paged response validation. Browsing uses the shared Previous/Next
pager. Filter changes, disposal, or an identity change invalidate pending work. Export uses the Portal
authentication refresh path and requires HTTP 200, `text/csv`, and a nonempty body before creating a
download. Failed or malformed catalogs clear old rows and disable export until a successful read.

### Admin — Portal Operations

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/reports` | Admin | Browse reports across folders |
| GET | `/api/admin/permissions/effective/user/{userId}` | Admin | Inspect effective user permissions |
| GET | `/api/admin/permissions/effective/folder/{folderId}` | Admin | Inspect effective folder permissions |
| GET | `/api/admin/permissions/effective/report/{reportId}` | Admin | Inspect effective report permissions |
| GET | `/api/admin/metrics/usage` | Admin | Portal usage metrics |
| GET | `/api/admin/settings/orchestrator` | Admin | Get orchestrator settings |
| PUT | `/api/admin/settings/orchestrator` | Admin | Update orchestrator settings |

### Admin — Secrets (Portal secret store)

Values are write-only: no endpoint returns a secret value after write; every mutation is audited.

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/secrets` | Admin | List secret metadata (name, status, timestamps, version) |
| PUT | `/api/admin/secrets/{name}` | Admin | Set/rotate a secret (re-enables if disabled) |
| POST | `/api/admin/secrets/{name}/verify` | Admin | Prove the secret decrypts; never prints it |
| POST | `/api/admin/secrets/verify-all` | Admin | Decrypt-probe every secret (backup/restore + HA key validation) |
| POST | `/api/admin/secrets/{name}/disable` | Admin | Disable; `SECRET:name` fails until re-enabled |
| POST | `/api/admin/secrets/{name}/enable` | Admin | Re-enable; the stored value resolves again |
| DELETE | `/api/admin/secrets/{name}` | Admin | Permanently remove the secret |

### Admin — Shared Connections (connection catalog)

Entries hold `SECRET:name` references, never credential values; detail responses mask any non-reference credential value.

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/connections` | Admin | List entries (alias, type, scope, status, last-used/verified) |
| GET | `/api/admin/connections/{alias}` | Admin | Masked entry detail |
| PUT | `/api/admin/connections/{alias}` | Admin | Create/update an entry (raw credential values rejected) |
| POST | `/api/admin/connections/{alias}/verify` | Admin | Prove the entry and its `SECRET:` references resolve |
| POST | `/api/admin/connections/{alias}/test` | Admin | Governed layered reachability diagnostic |
| GET | `/api/admin/connections/{alias}/impact` | Admin | Referencing scripts and recorded consumers |
| GET | `/api/admin/connections/help/{connectorType}` | Admin | Connector Markdown documentation |
| POST | `/api/admin/connections/{alias}/disable` | Admin | Disable; `SHARED:alias` fails until re-enabled |
| POST | `/api/admin/connections/{alias}/enable` | Admin | Re-enable; the stored definition is retained |
| DELETE | `/api/admin/connections/{alias}` | Admin | Permanently remove the entry |
| GET | `/api/admin/connections/export` | Admin | Metadata-only export (references, never values) |
| POST | `/api/admin/connections/import` | Admin | Import exported entries (same write-side validation) |

The browser controller is `BrowserSources/connections-admin.ts`; `connections-view.ts` owns its
markup and rendering, and `connection-responses.ts` validates catalog, detail, verification,
diagnostic, impact, import/export, help, and parsed-string responses against C# declarations.
Catalog mutations require the endpoint's 204 acknowledgement; import totals must cover the submitted
definitions. Incomplete diagnostics cannot announce success. Gateway bindings survive detail edits;
masked display values require replacement before saving, and option errors do not echo their values.
Read generations protect refreshed lists, closed panels, current form edits, and connector help.
Disposal cancels work and removes listeners; duplicate writes are blocked, and unconfirmed writes
require explicit successful catalog refresh. The help dialog uses shared keyboard/focus handling.
Connection and gateway administration share `gateway-wizard.ts`, which validates connector, gateway,
and secret metadata before mounting the canonical wizard and guards its parsing/test/save callbacks.

### Admin — Native Admin Services

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/services` | Admin | Configuration + last run of each native admin service |
| GET | `/api/admin/services/{name}/history` | Admin | Run ledger for one service (`?limit=`) |

### Admin — Gateways

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/gateways` | Admin | Enrollment history with current cluster nodes and approved resources |
| POST | `/api/admin/gateways/enroll` | Admin | Issue one-time enrollment token for a Gateway instance |
| GET | `/api/admin/gateways/{gatewayId}` | Admin | Inspect Gateway enrollment state, timestamps, and workload key thumbprint |
| GET | `/api/admin/gateways/{gatewayId}/resources` | Admin | List published approved non-secret resource metadata from active gateway session |
| POST | `/api/admin/gateways/{gatewayId}/revoke` | Admin | Revoke matching enrollments and request active-session disconnection |

Gateway administration lives in `BrowserSources/gateways-admin.ts`, with fleet/node rendering in
`gateways-view.ts`, C#-backed response validation in `gateway-responses.ts`, and the authenticated
connection-wizard adapter in `gateway-wizard.ts`. The fleet endpoint returns enrollment history.
The browser groups by gateway ID and uses the durable store's consumed-first, latest-created selection
so history does not inflate cluster/node counts. Missing or incomplete metadata clears stale counts
and displays unavailable state. Reads have request generations; disposal cancels work and removes listeners.
Enrollment and revocation need matching receipts. An unconfirmed mutation requires explicit refresh
before retrying. Done, new enrollment, and disposal clear the one-time token and setup command;
the PowerShell command quotes each argument. Binding validates connector, gateway, and secret metadata
before mounting the shared wizard. Closing or disposing the adapter rejects late resource, diagnostic,
and save callbacks. The sandbox uses the actual controller and current response shapes.

### Admin — Policy Authority

Policy administration lives in `BrowserSources/policy-authority-admin.ts`, with rendering and local
expiry conversion in `policy-authority-view.ts` and response validation in `policy-authority-responses.ts`.
Browser declarations reflect the live C# signer-status, validation, version, machine, and mutation DTOs.
The private signing key remains behind `IPolicyEnvelopeSigner`; the browser does not collect or export it.

Rows and pending requests belong to their loaded tenant/environment. Scope edits clear stale rows;
request generations reject superseded reads, validation of older JSON, and actions confirmed after a
scope change or refresh. Mutation receipts must match the requested identity, scope, rollout state,
and applicable hash/cohort/expiry. Unconfirmed writes block retries until an explicit successful scope
refresh. Changing scope during a sent write preserves that scope's unconfirmed state until refresh.
Disposal cancels requests and removes listeners. Missing metadata shows unavailable state rather than
empty history or machine lists. Expiry defaults use local wall time; requests carry UTC timestamps.
The sandbox imports this controller directly. Real Portal browser journeys exercise staged activation,
percentage/group canaries, halt/reissue, promotion, rollback signature verification, and audited machine
registration/revocation. `DedicatedPolicyAuthorityGuard` remains the server's tenant boundary.

### Gateways — Enrollment Bootstrap

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| POST | `/api/gateway/enrollment/consume` | Anonymous (One-Time Token) | Consume a one-time enrollment token to bootstrap an on-premises Gateway daemon |

### Platform — Control Plane

Dedicated platform admin control plane for SaaS fleet observability, tenant operational capacity, lifecycle management, and platform audit receipts. Authenticates exclusively via the platform management key (`X-Portal-Platform-Key` header). Responses contain strictly operational metadata and counts (zero tenant script bodies or data rows).

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/platform/control-plane/overview` | Platform Key | Fleet aggregate metrics, capacity, and tenant state distribution |
| GET | `/api/platform/control-plane/tenants` | Platform Key | Tenant inventory with operational metrics, quota usage, and status |
| GET | `/api/platform/control-plane/tenants/{tenantId}/health` | Platform Key | Individual tenant operational health, storage, and quota consumption |
| GET | `/api/platform/control-plane/audit` | Platform Key | Latest lifecycle receipts with status, operator attribution, and server-computed receipt hashes |
| POST | `/api/platform/control-plane/tenants/provision` | Platform Key | Provision a new tenant boundary in the shared fleet |
| POST | `/api/platform/control-plane/tenants/{tenantId}/quotas` | Platform Key | Update tenant execution and storage quota limits |
| POST | `/api/platform/control-plane/tenants/{tenantId}/state` | Platform Key | Transition tenant state (`Active`, `Maintenance`, `Quarantined`) |

The page controller, view rendering, and response validators are authored in
`BrowserSources/control-plane.ts`, `control-plane-view.ts`, and `control-plane-responses.ts`.
Requests use the platform endpoints directly; the page does not collect or store a management key,
and tenant JWTs do not grant platform access. The deployment must supply platform authentication.
Disabled deployments and denied, failed, or malformed responses show unavailable state with Refresh.
Tenant and audit failures remain distinct from successful empty lists, and mutations stay disabled
until all required metadata is available. Superseded refreshes and disposed controllers cannot render.
The view preserves tenant filtering across refresh, uses keyboard tabs and shared dialog focus handling,
and keeps returned receipts visible. A mutation receipt must match the requested tenant, action,
operator, authorization reference, reason, and supplied quota values before completion is announced.
An unconfirmed mutation requires an explicit metadata refresh before another submission.
The fleet panel is a configuration reference; it does not attest live worker health or hardened isolation.

### Catalog

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/catalog/search` | Any | Search reports/catalog content |
| GET | `/api/catalog/recent` | Any | Recently viewed or updated reports |
| GET | `/api/catalog/favorites` | Any | Current user's favorites |
| GET | `/api/catalog/lineage/table` | Any | Target-table history, with optional column and UTC run window |
| GET | `/api/catalog/lineage/source` | Any | History using a source table |
| GET | `/api/catalog/lineage/source-file` | Any | History from a recorded source file |
| GET | `/api/catalog/lineage/tag` | Any | History carrying a tag key and optional value |
| GET | `/api/catalog/lineage/job` | Any | History for a recorded job |
| GET | `/api/catalog/stewardship` | Any | Latest asset metadata, facets, health, and steward queue |
| GET | `/api/catalog/protected-data` | Any | Protected lineage observations with classification reasons |
| GET | `/api/catalog/protected-data/suggestions` | Any | Classifier suggestions with confidence and recorded evidence |
| GET | `/api/catalog/impact` | Any | Target-bound upstream/downstream impact, including alerts |

Catalog endpoints require authentication and the Reporting module. Lineage lookups use the request's
tenant partition; impact applies report visibility and dataset permissions to those consumer lists.

`BrowserSources/lineage-catalog.ts` owns the explorer controller; `lineage-catalog-view.ts` owns its
markup, `lineage-catalog-types.ts` owns host interfaces, and `lineage-catalog-responses.ts` validates
the C#-generated history, stewardship, protected-data, suggestion, impact, and audit contracts.
Failed or malformed sources show unavailable state. Audit can retain available sections while
unavailable sources show no numeric claim. Protected counts describe returned observations, which
can include several runs of one asset. Stewardship asset totals cover the visible catalog inventory;
its queue count follows steward/domain scope and its item list also applies search and view filters.
Impact responses must match the requested target, direction, depth, and limit before rendering.

Request generations, cancellation, host ownership, and identity checks prevent late work from replacing
another mode or a disposed explorer. Editing query inputs invalidates prior results and CSV export.
Saved-view payloads are validated and stored per issuer, tenant, and subject; a late naming prompt
cannot write after disposal. UTC date filters include the last fractional second of the selected day.
Keyboard tabs select one visible mode form and keep focus on the selected tab. The audit mode is
exposed only when the host allows it; its admin endpoints still enforce server authorization.

### Governance

Three authority tiers, because these are three different authorities: reading is deliberately wide
(a steward blind to other stewards' work cannot cover for them), deciding is steward judgement, and
configuring changes what "governed" means estate-wide. Every mutation is audited.

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/governance/dashboard` | GovernanceRead | Estate posture with explained scores; `?scope=mine` filters to the caller |
| GET | `/api/governance/findings` | GovernanceRead | Findings with their decision trail |
| GET | `/api/governance/scans` | GovernanceRead | Scan history — distinguishes "no findings" from "never scanned" |
| GET | `/api/governance/settings` | GovernanceRead | Thresholds, enabled checks, policy level |
| GET | `/api/governance/categories` | GovernanceRead | Suppression categories |
| GET | `/api/governance/glossary` | GovernanceRead | Glossary terms |
| POST | `/api/governance/findings/{id}/decide` | GovernanceDecide | Ignore, accept risk, or reopen. Reason is required; ignore and accept-risk also require an asset version |
| POST | `/api/governance/assets/review` | GovernanceDecide | Mark an asset reviewed at a version |
| POST/DELETE | `/api/governance/assets/badges` | GovernanceDecide | Steward-assigned badges |
| POST | `/api/governance/scan` | GovernanceConfigure | Recompute findings across the estate |
| PUT | `/api/governance/settings` | GovernanceConfigure | Thresholds and enabled checks; audit records the value **before** as well as after |
| POST/DELETE | `/api/governance/categories[/{value}]` | GovernanceConfigure | Manage suppression categories (delete disables, so historical decisions keep a reason they can cite) |
| POST/DELETE | `/api/governance/glossary[/{term}]` | GovernanceConfigure | Manage glossary terms |

`BrowserSources/governance-portal.ts` owns the controller, `governance-view.ts` renders its markup,
and `governance-responses.ts` validates read responses and mutation receipts against reflected C# declarations. The
`/js/governance-portal.js` URL and `createGovernancePortal` export remain stable. Dashboard, finding,
settings, category, and glossary responses must satisfy their contracts before becoming visible.
Read generations, abort signals, host ownership, and identity checks reject superseded or disposed work.
Quality inventory failures clear rows and counts. A missing trend stays unavailable; it does not
become a recorded zero. Trend loading covers all jobs with at most five concurrent requests, and
the rate tile describes the mean of available job averages. A failed trend prevents a complete
estate rate claim. Trend dialogs validate the requested job and ignore responses after closure or replacement.

Mutations require HTTP 200 with a matching receipt; badge removal, category disable, and glossary
delete require HTTP 204. Review and badge receipt records retain the existing wire fields. Scan
acknowledgements must identify a newer completed manual scan; failed or running scans cannot announce
completion. Pending and unconfirmed writes are guarded by issuer, tenant, and subject across
controller disposal and remount. An unconfirmed outcome requires a successful explicit refresh.
Late confirmations and writes cannot act on a replaced view or overwrite a new draft. Glossary edits
retain stewardship and disabled state, and category edits retain disabled state. Dialogs trap Tab;
Cancel and Escape restore accessibility state and focus. Read redraws preserve settings and modal drafts.

Asset totals follow `scope=mine`, while finding totals cover the estate. Exceptions use the latest
500 estate findings and remain independent of asset scope. The page and controls accept the seeded
`StewardshipViewer`, `DataSteward`, and `StewardshipManager` roles and the governance aliases accepted
by the server policies. Client controls reflect those permissions; server authorization remains authoritative.

### Report Drafts (draft → review → publish)

Opt-in behind `Portal:Studio:RequireApprovalToPublish` (default **off**). Every mutation takes
`If-Match` with the draft's version. **An author can never approve their own draft**, whatever
capabilities or roles they hold, including Admin.

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/reports/{id}/draft` | Author | The open draft, with its decision trail |
| PUT | `/api/reports/{id}/draft` | Author + `ScriptSave` | Create or update the draft. Editing revokes any approval or review in progress |
| POST | `/api/reports/{id}/draft/submit` | Author | Submit for review |
| POST | `/api/reports/{id}/draft/approve` | Author + `ReportApprove` | Approve — refused for the draft's own author |
| POST | `/api/reports/{id}/draft/reject` | Author + `ReportApprove` | Reject; a reason is required |
| POST | `/api/reports/{id}/draft/publish` | Manage + `ReportPublish` | Publish an approved draft; refused if the live script moved past the draft's base |

### Data Quality

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/data-quality/quarantine` | DataQualityStewardAccess | Quarantine queue with per-target readability and reasons |
| GET | `/api/data-quality/quarantine/rows` | DataQualityStewardAccess **+ a grant on the target's shared connection** | Raw quarantined rows; gated by `Portal:DataQuality:AllowConnectionPreview` (default off) and audited as `READ_QUARANTINE_ROWS` |
| POST | `/api/data-quality/quarantine/replay` | DataQualityStewardAccess | Submit replay through `IJobChannel`; returns a 202 acknowledgement and durable submission ID |
| POST | `/api/data-quality/quarantine/disposition` | DataQualityStewardAccess | Submit an audited row disposition with an expected-status predicate |
| GET | `/api/data-quality/jobs/{jobId}` | DataQualityStewardAccess | Reconcile a durable submission with channel status; report `Unknown` when its live record is lost |
| GET | `/api/data-quality/jobs` | DataQualityStewardAccess | Named job definitions for quality lookup |
| GET | `/api/data-quality/trend` | DataQualityStewardAccess | Completed-run quality metrics and aggregate failure counts |
| GET | `/api/data-quality/rules` | DataQualityStewardAccess | Rule definitions for one job script |
| GET | `/api/data-quality/rules/all` | DataQualityStewardAccess | Rule inventory across saved job scripts |

`BrowserSources/data-quality-queue.ts` owns the controller; `data-quality-view.ts` renders its
queue and dialogs, `data-quality-responses.ts` validates responses, and `data-quality-state.ts`
owns client state. The served `/js/data-quality-queue.js` URL and `renderTopFailures` export remain
stable. Queue, trend, rule, row, and submission shapes come from C#-generated declarations.
Unavailable sources do not report empty results or retain queue counts. Read generations and
abort signals prevent old filters, closed panels, disposed controllers, and identity changes from
rendering late results. Dialog nodes and active input selections survive polling renders.
Queue refresh rechecks the open target's metadata. An unavailable or no-longer-readable target
clears its preview and cancels pending row reads; restoring access requires a new row load.
Row responses must contain each declared column as an own property. Edit and note dictionaries
support arbitrary row IDs and source column names without changing object prototypes.

Submission receipts require HTTP 202. Status responses must match the submitted job ID, kind, and
target and agree on terminal status. Session storage retains submission metadata under the issuer,
tenant, and subject; every restored job is revalidated, including previously completed jobs.
Missing status is not completion, and a terminal `Unknown` outcome requires inspection and explicit
queue refresh before resubmission. Pending/unconfirmed targets stay guarded across controller
replacement in the same page session. Polling retains row edits and notes; the steward reloads rows
to inspect a completed disposition.

`QuarantineDispositionRequest.ExpectedStatus` defaults to `quarantined`. The controller accepts
`quarantined` or `released` rows for release/discard and `replaying` rows for return to released or
marking replayed. The generated update includes row IDs and that expected status, so rows that have
changed state are left alone. Replay-claim recovery cannot edit captured source values. Existing
preview policy, connection grants, and audit boundaries still apply.

### Other API areas

Surfaces with their own controllers, listed so the inventory is complete; each is covered in depth
by the administration guides rather than restated here.

| Prefix | Auth | Area |
| :--- | :--- | :--- |
| `/api/branding` | Public | Deployment branding for the login page and shell |
| `/api/portal/navigation` | Any | Which top-level entry points to offer **this** caller. The shell applies the answer; it does not compute one. Two destinations cannot be decided from a token claim — Docs depends on the Documentation module, Studio on the `StudioAccess` capability — so pages that guessed offered a Docs link that 404s and a Studio link to everyone |
| `/api/auth/oidc` | Public | OIDC federation — the enterprise identity path |
| `/api/auth/service-token` | Public (client credentials) | Service-account token issue |
| `/api/auth/workload-token` | Public (signed assertion) | Short-lived, exact-resource workload token exchange |
| `/api/admin/service-accounts` | Admin | Scoped machine identities, capped by their owner's authority |
| `/api/admin/workload-identity/approvals` | Admin (non-owner) | Signed one-use approval for sensitive workload exchange |
| `/api/admin/identity/authorities` | Admin | Tenant-scoped shared-host OIDC authority registration, rotation metadata, enablement, and deletion |
| `/api/admin/policy-authority` | Admin | Enterprise policy publish, activate, canary, roll back, impact |
| `/api/admin/gateway-operations/ambiguous-writes` | Admin, OrchestratorManager | Ambiguous gateway write incident triage, review, and resolution ledger |
| `/api/policy-authority` | Machine | Policy distribution to enrolled machines |
| `/api/admin/configuration` | Admin | Configuration export and promotion between environments |
| `/api/studio` | Studio capabilities | Authoring surface; `GET /api/studio/session` is a **probe** reachable by any authenticated user |
| `/api/designer` | Designer module + Studio capabilities | Report designer parse/generate/schema and governed row preview. `POST /data-preview` requires `ScriptPreview`; connection sources pass through tenant-scoped catalog ACL/schema resolution, while `#temp` sources replay only their read-only materialization prefix. Both use bounded, redacted interactive execution. |
| `/api/designer/lease` | `ScriptSave` + report Author | Atomic five-minute edit-session acquire/renew and owner-only release. Lease metadata does not advance the report content version; shared-tenancy requests additionally match the report creator's stored tenant to the signed tenant claim. |
| `/api/docs` | Any | Embedded documentation, served from the `docs/` copied into the image |
| `/api/fleet` | FleetReader | Read-only cross-environment status; visibility, never authority |
| `/api/connectors` | Authenticated | Connector schemas, connection string parsing, layered zero-trust diagnostic probes, and tenant-scoped Gateway resource discovery |
| `/api/platform/support-bundle` | Anonymous (capability-bound) | Capability-only Managed Dedicated support surface |
| `/api/platform/shared-tenants` | Anonymous (management key + signed policy) | Platform-only Shared tenant lifecycle management |

### Orchestrator Proxy

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/orchestrator/status` | Admin | Orchestrator status |
| GET | `/api/orchestrator/metrics` | Admin | Orchestrator metrics |
| GET | `/api/orchestrator/jobs` | Admin | List jobs |
| POST | `/api/orchestrator/jobs` | Admin | Create job |
| PUT | `/api/orchestrator/jobs/{name}` | Admin | Update job |
| DELETE | `/api/orchestrator/jobs/{name}` | Admin | Delete job |
| GET | `/api/orchestrator/jobs/{name}/history` | Admin | Job history |
| POST | `/api/orchestrator/jobs/{name}/trigger` | Admin | Trigger job now |
| POST | `/api/orchestrator/jobs/{name}/kill` | Admin | Kill running job |
| GET | `/api/orchestrator/scripts` | Admin | List scripts |
| GET | `/api/orchestrator/scripts/content` | Admin | Read script content |
| POST | `/api/orchestrator/service/stop` | Admin | Request orchestrator service stop |

Triage rendering lives in `BrowserSources/triage-ui.ts`, with response validation in
`triage-responses.ts` and polling/interactions in `triage-controller.ts`. The Orchestrator page
supplies authenticated API calls and the single-job override dialog. Browser declarations reflect
`TriageBoardDto`, `TriageRunDetailDto`, quality failures, statement metrics, and per-job rerun outcomes.
Missing arrays, inconsistent counts, and evidence for another run produce unavailable state.
An empty failure list still displays running work. Incident signatures preserve selection and focus
across reordered polls; request generations and disposal guards reject stale reads. Batch outcomes
retain failed selections, prevent duplicate submissions, and require explicit refresh after an
unconfirmed result. The sandbox imports this controller directly.

### Studio private drafts

`StudioController.PrivateDrafts` keeps saved, unpublished work in `studio_private_drafts`, with its
kept saves in `studio_private_draft_revisions`. Policy and its reasons are in the
[private drafts decision](decisions/studio-private-drafts.md).

| Method | Path | Description |
| :--- | :--- | :--- |
| GET | `/api/studio/private-drafts` | The caller's drafts, without text |
| POST | `/api/studio/private-drafts` | Create a draft from `Name`, `Content` and `Kind` (`Report` or `Pipeline`) |
| GET | `/api/studio/private-drafts/{id}` | One draft with its saved text and version |
| PUT | `/api/studio/private-drafts/{id}` | Save `Content` against `BaseVersion`; answers the new version |
| DELETE | `/api/studio/private-drafts/{id}` | Delete the draft and its kept saves |
| GET | `/api/studio/private-drafts/{id}/revisions` | Kept saves, newest first |
| GET | `/api/studio/private-drafts/{id}/revisions/{version}` | The text of one kept save |
| GET | `/api/studio/private-drafts/{id}/unsaved` | Edits typed since the last save, and the version they started from |
| PUT | `/api/studio/private-drafts/{id}/unsaved` | Keep `Content` as unsaved edits against `BaseVersion` |
| DELETE | `/api/studio/private-drafts/{id}/unsaved` | Discard the unsaved edits |
| POST | `/api/studio/private-drafts/{id}/publish` | Create the catalog document from the draft's saved text and remove the draft |

Every route needs `StudioAccess` and `ScriptRead`. Every query carries the caller's tenant and user
id, so a draft that is not the caller's answers `404 DRAFT_NOT_FOUND`, the same as a missing one,
and a delete of one answers `204` and removes nothing.

Refusals return `{ code, error, currentVersion? }`:

| Code | Status | Meaning |
| :--- | :--- | :--- |
| `PRIVATE_DRAFTS_OFF` | 409 | `Portal:Studio:PrivateDrafts` is `false` |
| `DRAFT_LIMIT` | 409 | The caller already holds `PrivateDraftsPerUser` drafts |
| `DRAFT_NAME_TAKEN` | 409 | The caller already has a draft of that name |
| `DRAFT_CONFLICT` | 409 | `BaseVersion` is not the saved version; `currentVersion` names it |
| `PLAINTEXT_SECRET` | 422 | The text assigns a credential as a plain literal |
| `DRAFT_TOO_LARGE` | 413 | The text exceeds `DesignerLimits:MaxScriptCharacters` |
| `DRAFT_HAS_UNSAVED_EDITS` | 409 | Publish was asked while unsaved edits are kept for the draft |

Publish additionally needs `ScriptSave` and `ReportPublish`, takes `FolderId`, `Version` and an
optional `Name`, and runs `StudioController.CreateCatalogDocumentAsync`, the same method
`POST /api/studio/reports` runs: folder `Manage`, name uniqueness in the folder, source-control
validation, the script artifact write and the audit entry. The draft and its kept saves are deleted
inside that method's transaction, so the document and the removal commit together. Its refusals are
the catalog create's own (`403`, `404`, `409`), plus `DRAFT_CONFLICT` for a stale `Version`.

The draft's version is an EF concurrency token, and `(draft_id, version)` is unique among its kept
saves, so two saves racing from the same version cannot both succeed. Expiry is applied when a draft
is read and, tenant-wide, when any caller lists or creates drafts; there is no background sweep.

Unsaved edits are two columns on the draft, not a version. They follow the recovery-draft policy
(`DraftRecovery`, `DraftRetentionDays`), are refused with `DRAFT_RECOVERY_OFF` when it is off, and
are written with a set-based update so they never move the draft's version or its retention clock.
A save clears them. Studio's existing recovery callbacks address them for a private-draft document,
so the offer, the changed-since warning and the plaintext-credential refusal are the same code paths
a catalog report uses.

Studio reaches these through the `privateDrafts` host option. Save on an untitled document keeps it
as a draft, Studio Home lists the caller's drafts, and Save on an open draft writes the next version.

### Studio pipeline runs

A saved catalog pipeline runs in the Orchestrator, never in the Portal
([decision](decisions/portal-etl-documents.md)). `StudioPipelineRunsController` checks the document,
reads the saved revision, and hands it to the pipeline's Orchestrator job under the caller's own
identity assertion. The Orchestrator decides whether that caller may run the job.

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| POST | `/api/studio/pipelines/{id}/runs` | `ScriptRun` + document Execute | Submit the saved revision. The body names the version the author sees; a stale one is `409 stale_revision`. Returns `202` with the run ID |
| GET | `/api/studio/pipelines/{id}/runs/{runId}` | `ScriptRun` + document Execute | Status and bounded statement log of that run. A run of another pipeline's job is `404` |
| POST | `/api/studio/pipelines/{id}/runs/{runId}/cancel` | `ScriptRun` + document Execute | Ask the Orchestrator to stop that run. `202` means the request was accepted; the run reports `CANCELLED` when it has stopped |
| GET | `/api/studio/pipelines/{id}/schedule` | `ScriptRun` + document Execute | The pipeline's schedule, and whether the revision it runs is still the saved one |
| PUT | `/api/studio/pipelines/{id}/schedule` | `ScriptRun` + document Execute | Pin the named saved version in the job and attach a cron schedule. Needs `MANAGE` on the job |
| DELETE | `/api/studio/pipelines/{id}/schedule` | `ScriptRun` + document Execute | Remove the schedule. The job goes back to running only on request |

Each pipeline maps to one Orchestrator job named `PIPELINE:{id}` in the Portal's tenant. The job is
a run slot: enabled, unscheduled, and never due on its own. A caller with `Admin` or
`OrchestratorManager` creates it on first run and owns it. Anyone else needs an `EXECUTE` grant on
it, set from the Orchestrator's access panel or API.

Refusals carry a stable `code` so the author can tell whose decision it was:

| Code | Decided by | Meaning |
| :--- | :--- | :--- |
| `document_execute_denied` | Portal | No Execute on the pipeline document |
| `stale_revision` | Portal | The named version is not the saved one |
| `orchestrator_not_connected` | Portal | No Orchestrator is configured. Nothing runs in its place |
| `orchestrator_unreachable` | Portal | The configured Orchestrator did not answer |
| `job_not_provisioned` | Orchestrator | No job exists and the caller may not create one |
| `run_grant_denied` | Orchestrator | No `EXECUTE` grant on the job |
| `already_running` | Orchestrator | The job has a run in progress |
| `sandbox_identity_unsupported` | Orchestrator | The Orchestrator's sandbox executor cannot run text as its submitter |
| `cancel_denied` | Orchestrator | The caller did not submit the run and holds no `MANAGE` grant |
| `run_already_finished` | Orchestrator | Stop arrived after the run ended |
| `schedule_grant_denied` | Orchestrator | Scheduling needs `MANAGE` on the job; `EXECUTE` is not enough |
| `invalid_schedule` | Orchestrator | The cron expression or time zone is not usable |

A schedule runs the revision that was pinned when it was set, with a blocking hash policy. Saving
the pipeline later does not change what runs unattended; `scheduledRevisionIsSaved` turns false
until the author schedules again. A run on request still runs the latest save and does not consume
a scheduled occurrence. `PIPELINE_SCHEDULED` and `PIPELINE_UNSCHEDULED` are audited.

There is no Portal-side run table. The run ID is the Orchestrator's history ID, and a run is shown
under a pipeline only when the Orchestrator reports it against that pipeline's job. Submission and
stop requests are written to the Portal audit log as `PIPELINE_RUN_SUBMITTED` (with version, hash,
job and run ID) and `PIPELINE_RUN_CANCEL_REQUESTED`.

In Studio, **Run** on a saved pipeline calls these routes through the `pipelineRuns` host callbacks
in `BrowserSources/portal-studio-host.ts`. Unsaved edits are refused before any request. Run
selection and helper previews stay on the read-only `/api/designer/run` path.

Helper requests set `helperPreview` and may name `previewContext` with a one-based
`previewContextLine`. The shared validator permits only nonsensitive variable declarations and
read-only `#temp` staging above that line, rejecting unavailable context before execution. Native
helpers accept one read-only SELECT on a direct catalog SQLite connection whose target opens with
`Mode=ReadOnly`; the real caller's connection ACL still applies. Ordinary interactive runs retain
their existing allow-list. A lost interactive response is displayed as `Unknown`, with its run ID
kept in the original document and the Portal audit. Bounded host evidence:
[Studio execution scope](decisions/studio-execution-scope-evidence.md).

Studio Home uses `GET /api/studio/catalog?query=...&afterId=...`. Each request inspects at most 50
tenant-scoped candidate documents plus one lookahead row. Permissions are resolved in batches with
the same ownership, authorship, and ranked ACL rules as a single report check. The response returns
only visible summaries and a cursor past the inspected rows; pages can be empty when candidates
are inaccessible. Search, Next/Previous, and explicit Refresh update discovery without replacing
open documents. Direct links and new opens recheck `/api/studio/reports/{id}/summary`; deleted or
moved selections fail with an explanation. The older report-list endpoint remains available to its
other callers. This is request-driven discovery, not live catalog synchronization.

The session response advertises `maxScriptCharacters` from the designer configuration. Shared
Studio editor services pause above that UTF-16 character count while keeping the buffer editable;
desktop uses 200,000. Analysis 429 responses explain stale diagnostics and schedule the current
buffer after `Retry-After`, or a one-second delay when that header is absent. Retry timers are
disposed with the editor. See [delivery and document measurements](../benchmarks/studio-delivery-budgets.md).

Portal Studio session and initial catalog reads share a 15-second AbortSignal deadline. Failed
startup shows Retry Connection and mounts no workbench with fallback permissions. The user label
is populated only after session verification and any access-token refresh. A rejected CodeMirror
import can be retried independently in the shared editor; basic text editing remains available,
while canonical visual/helper writes refuse until the full editor recovers. Existing draft recovery
survives re-authentication. Real-host and sandbox evidence:
[Studio failed recovery](decisions/studio-failed-recovery-evidence.md).

The governance rail's **Run it on a schedule** section uses the `pipelineSchedule` host callbacks
for a catalog pipeline. It does not write `CREATE JOB` into the script there: a catalog document
has no path a job statement could name. Desktop Studio keeps the statement-writing form.

**Export and promotion.** The configuration export emits a pipeline with the same statement as a
report, marked by kind: `PUBLISH REPORT '<name>' FROM '<file>.etlsql' IN FOLDER '<path>' WITH
(CATALOG_OWNER = '<user>', KIND = 'PIPELINE')`. The script file travels by copy and is listed in the
manifest as `PipelineScript`. `POST /api/reports` accepts `Kind`; each kind refuses the other's file
extension. Publishing a pipeline that is already there with the same content answers `200` and
changes nothing, so a replay is idempotent. A document already holding the name with different
content, of either kind, is `409 document_collision` and is left untouched. The Orchestrator job, its
grants and owner, the schedule and the pinned revision belong to the source host and are not
exported; the export's summary says so, and the pipeline is run or scheduled again at the target.

### System

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/health` | Public | Health check (JSON) |

---

## 10. Related Subsystem Architecture

For detailed information about adjacent subsystems, refer to the following architecture references:
- **Reporting Engine:** [Reporting.md](reporting.md) documents the manifest builder, parameter mapping, and layout rendering details.
- **Portal UI & Designer:** [PortalUI.md](portal-ui.md) describes the client-side design canvas, editor, and API structures.
- **Orchestrator Scheduler:** [Orchestrator.md](orchestrator.md) details how the backend scheduler schedules and triggers catalog subscriptions.
