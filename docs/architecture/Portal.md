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
SharedIdentityAuthority (tenant-owned OIDC authority metadata; client secrets remain external)
PolicyVersionEntity / PolicyMachineEntity   (enterprise policy authority + machine registry)
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
```

### Key Design Decisions

- **`PortalUser` extends `IdentityUser<int>`** — all password hashing, lockout counters, and token validation delegate to ASP.NET Identity. Custom columns (`IsActive`, `MustChangePassword`, `CreatedAt`) are added via the EF `OnModelCreating` override in `PortalDbContext`.

- **Soft-delete on `Report`** — `IsDeleted = true` hides records from all queries. Snapshots are preserved on disk after soft-deletion; hard deletion is a manual operation.

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
  │<── { token, refreshToken }│   (access: 60 min, refresh: 7 days)
  │                            │
  │── GET /api/... Bearer token│   (validated by JwtBearer middleware)
  │                            │
  │── POST /api/auth/refresh ──│   (issues new token + rolling refresh token)
  │<── { token, refreshToken } │
```

**Token validation** is handled entirely by `Microsoft.AspNetCore.Authentication.JwtBearer`. The portal sets `ValidateIssuer = false` and `ValidateAudience = false` since it is a single-tenant deployment; only `ValidateIssuerSigningKey` and `ValidateLifetime` are enforced.

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
  └── ExecutionJobService.EnqueueAsync()
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

**`ExecutionJobService`** is a singleton/hosted service that manages a `SemaphoreSlim` with
`MaxConcurrentReportExecutions` slots. Jobs that exceed the slot limit are queued. Every job is
also written to `PortalExecutionJobs`, so `GET /api/jobs/{jobId}` remains meaningful after a
restart. A filtered unique index permits only one `Pending`/`Running` refresh per report. Startup
marks abandoned jobs and report refresh status as `Cancelled` with an interruption reason; it
does not claim that vanished work completed successfully. While the process is live, the cluster
node heartbeat is also treated as a local-work lease: if heartbeat renewal fails long enough for
this node's registry lease to expire, `ExecutionJobService` cancels every locally running execution
before another node can safely take over work.

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
| POST | `/api/auth/login` | Public | Login; returns `{ token, refreshToken }` |
| POST | `/api/auth/refresh` | Public | Refresh access token |
| POST | `/api/auth/change-password` | Any | Change own password |
| POST | `/api/auth/logout` | Any | Revoke refresh token |

### Folders

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/folders` | Any | List all folders (filtered by ACL) |
| POST | `/api/folders` | Admin, Publisher | Create folder |
| GET | `/api/folders/{id}` | Any | Get folder by id |
| DELETE | `/api/folders/{id}` | Admin | Delete folder (must be empty) |
| GET | `/api/folders/{id}/acl` | Admin | List ACL entries |
| POST | `/api/folders/{id}/acl` | Admin | Add ACL entry |
| DELETE | `/api/folders/{id}/acl/{groupId}` | Admin | Remove ACL entry |

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
| POST | `/api/reports` | Admin, Publisher | Publish report |
| POST | `/api/reports/validate` | Admin, Publisher | Validate a report script |
| GET | `/api/reports/{id}` | Any | Get report metadata |
| GET | `/api/reports/{id}/dependencies` | Any | Get report dependencies |
| GET | `/api/reports/{id}/history` | Any | Get report history |
| PUT | `/api/reports/{id}` | Admin, Publisher | Update report metadata |
| POST | `/api/reports/{id}/favorite` | Any | Mark report as favorite |
| DELETE | `/api/reports/{id}/favorite` | Any | Remove favorite |
| POST | `/api/reports/{id}/share-links` | Admin, Publisher | Create share link |
| GET | `/api/reports/{id}/share-links` | Admin, Publisher | List share links |
| DELETE | `/api/reports/{id}/share-links/{token}` | Admin, Publisher | Revoke share link |
| GET | `/api/share/{token}` | Public | Resolve share link |
| POST | `/api/reports/{id}/embed-tokens` | Admin, Publisher | Create embed token |
| GET | `/api/reports/{id}/embed-tokens` | Admin, Publisher | List embed tokens |
| DELETE | `/api/reports/{id}/embed-tokens/{token}` | Admin, Publisher | Revoke embed token |
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
| DELETE | `/api/reports/{id}` | Admin, Publisher | Soft-delete report |
| POST | `/api/scripts/upload` | Admin, Publisher | Upload script file |
| GET | `/api/reports/available-scripts` | Admin, Publisher | List available script files |
| GET | `/api/maps/custom` | Any | List custom map assets |

### Execution

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| POST | `/api/reports/{id}/execute` | Any | Start execution job |
| GET | `/api/jobs/{jobId}` | Any | Poll job status |
| GET | `/api/reports/{id}/snapshot` | Any | Get snapshot JSON |
| GET | `/api/reports/{id}/manifest` | Any | Get current manifest JSON |
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
| GET | `/api/datasets/{id}` | Any | Get dataset metadata |
| GET | `/api/datasets/{id}/rows` | Any | Get dataset rows |
| POST | `/api/datasets/{id}/refresh` | Admin, Publisher | Start dataset refresh |
| GET | `/api/datasets/{id}/refresh-status` | Any | Get dataset refresh status |
| PATCH | `/api/datasets/{id}` | Admin, Publisher | Update dataset metadata |
| DELETE | `/api/datasets/{id}` | Admin, Publisher | Delete dataset |
| GET | `/api/datasets/{id}/acl` | Admin | List dataset ACL entries |
| POST | `/api/datasets/{id}/acl` | Admin | Add dataset ACL entry |
| DELETE | `/api/datasets/{id}/acl/{groupId}` | Admin | Remove dataset ACL entry |

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
| GET | `/api/subscriptions/{id}/history` | Any | Delivery history |
| GET | `/api/smtp-aliases` | Any | List usable SMTP aliases |

### Admin — Users & Groups

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/users` | Admin | List users |
| POST | `/api/admin/users` | Admin | Create user |
| GET | `/api/admin/users/{id}` | Admin | Get user |
| PUT | `/api/admin/users/{id}` | Admin | Update user |
| DELETE | `/api/admin/users/{id}` | Admin | Delete user |
| POST | `/api/admin/users/{id}/reset-password` | Admin | Reset password |
| POST | `/api/admin/users/{id}/revoke-tokens` | Admin | Revoke all sessions |
| GET | `/api/admin/groups` | Admin | List groups |
| POST | `/api/admin/groups` | Admin | Create group |
| GET | `/api/admin/groups/{id}` | Admin | Get group |
| PUT | `/api/admin/groups/{id}` | Admin | Update group |
| DELETE | `/api/admin/groups/{id}` | Admin | Delete group |
| GET | `/api/admin/groups/{id}/members` | Admin | List group members |
| POST | `/api/admin/groups/{id}/members` | Admin | Add member |
| DELETE | `/api/admin/groups/{id}/members/{userId}` | Admin | Remove member |

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
| POST | `/api/admin/connections/{alias}/disable` | Admin | Disable; `SHARED:alias` fails until re-enabled |
| POST | `/api/admin/connections/{alias}/enable` | Admin | Re-enable; the stored definition is retained |
| DELETE | `/api/admin/connections/{alias}` | Admin | Permanently remove the entry |
| GET | `/api/admin/connections/export` | Admin | Metadata-only export (references, never values) |
| POST | `/api/admin/connections/import` | Admin | Import exported entries (same write-side validation) |

### Admin — Native Admin Services

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/services` | Admin | Configuration + last run of each native admin service |
| GET | `/api/admin/services/{name}/history` | Admin | Run ledger for one service (`?limit=`) |

### Admin — Gateways

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| POST | `/api/admin/gateways/enroll` | Admin | Issue one-time enrollment token for a Gateway instance |
| GET | `/api/admin/gateways/{gatewayId}` | Admin | Inspect Gateway enrollment state, timestamps, and workload key thumbprint |
| POST | `/api/admin/gateways/{gatewayId}/revoke` | Admin | Revoke Gateway enrollment token and unregister active session |

### Platform — Control Plane

Dedicated platform admin control plane for SaaS fleet observability, tenant operational capacity, lifecycle management, and platform audit receipts. Authenticates exclusively via the platform management key (`X-Portal-Platform-Key` header). Responses contain strictly operational metadata and counts (zero tenant script bodies or data rows).

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/platform/control-plane/overview` | Platform Key | Fleet aggregate metrics, capacity, and tenant state distribution |
| GET | `/api/platform/control-plane/tenants` | Platform Key | Tenant inventory with operational metrics, quota usage, and status |
| GET | `/api/platform/control-plane/tenants/{tenantId}/health` | Platform Key | Individual tenant operational health, storage, and quota consumption |
| GET | `/api/platform/control-plane/audit` | Platform Key | Platform administration audit log with cryptographic receipt verification |
| POST | `/api/platform/control-plane/tenants/provision` | Platform Key | Provision a new tenant boundary in the shared fleet |
| POST | `/api/platform/control-plane/tenants/{tenantId}/quotas` | Platform Key | Update tenant execution and storage quota limits |
| POST | `/api/platform/control-plane/tenants/{tenantId}/state` | Platform Key | Transition tenant state (`Active`, `Suspended`, `Archived`) |

### Catalog

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/catalog/search` | Any | Search reports/catalog content |
| GET | `/api/catalog/recent` | Any | Recently viewed or updated reports |
| GET | `/api/catalog/favorites` | Any | Current user's favorites |

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
| POST | `/api/governance/findings/{id}/decide` | GovernanceDecide | Ignore, accept risk, or reopen. Reason and asset version are both required |
| POST | `/api/governance/assets/review` | GovernanceDecide | Mark an asset reviewed at a version |
| POST/DELETE | `/api/governance/assets/badges` | GovernanceDecide | Steward-assigned badges |
| POST | `/api/governance/scan` | GovernanceConfigure | Recompute findings across the estate |
| PUT | `/api/governance/settings` | GovernanceConfigure | Thresholds and enabled checks; audit records the value **before** as well as after |
| POST/DELETE | `/api/governance/categories[/{value}]` | GovernanceConfigure | Manage suppression categories (delete disables, so historical decisions keep a reason they can cite) |
| POST/DELETE | `/api/governance/glossary[/{term}]` | GovernanceConfigure | Manage glossary terms |

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

### Other API areas

Surfaces with their own controllers, listed so the inventory is complete; each is covered in depth
by the administration guides rather than restated here.

| Prefix | Auth | Area |
| :--- | :--- | :--- |
| `/api/branding` | Public | Deployment branding for the login page and shell |
| `/api/portal/navigation` | Any | Which top-level entry points to offer **this** caller. The shell applies the answer; it does not compute one. Two destinations cannot be decided from a token claim — Docs depends on the Documentation module, Studio on the `StudioAccess` capability — so pages that guessed offered a Docs link that 404s and a Studio link to everyone |
| `/api/auth/oidc` | Public | OIDC federation — the enterprise identity path |
| `/api/auth/service-token` | Public (client credentials) | Service-account token issue |
| `/api/admin/service-accounts` | Admin | Scoped machine identities, capped by their owner's authority |
| `/api/admin/identity/authorities` | Admin | Tenant-scoped shared-host OIDC authority registration, rotation metadata, enablement, and deletion |
| `/api/admin/policy-authority` | Admin | Enterprise policy publish, activate, canary, roll back, impact |
| `/api/policy-authority` | Machine | Policy distribution to enrolled machines |
| `/api/admin/configuration` | Admin | Configuration export and promotion between environments |
| `/api/studio` | Studio capabilities | Authoring surface; `GET /api/studio/session` is a **probe** reachable by any authenticated user |
| `/api/designer` | Designer module + Studio capabilities | Report designer parse/generate/schema and governed row preview. `POST /data-preview` requires `ScriptPreview`; connection sources pass through tenant-scoped catalog ACL/schema resolution, while `#temp` sources replay only their read-only materialization prefix. Both use bounded, redacted interactive execution. |
| `/api/designer/lease` | `ScriptSave` + report Author | Atomic five-minute edit-session acquire/renew and owner-only release. Lease metadata does not advance the report content version; shared-tenancy requests additionally match the report creator's stored tenant to the signed tenant claim. |
| `/api/docs` | Any | Embedded documentation, served from the `docs/` copied into the image |
| `/api/fleet` | FleetReader | Read-only cross-environment status; visibility, never authority |
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

### System

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/health` | Public | Health check (JSON) |

---

## 10. Related Subsystem Architecture

For detailed information about adjacent subsystems, refer to the following architecture references:
- **Reporting Engine:** [Reporting.md](Reporting.md) documents the manifest builder, parameter mapping, and layout rendering details.
- **Portal UI & Designer:** [PortalUI.md](PortalUI.md) describes the client-side design canvas, editor, and API structures.
- **Orchestrator Scheduler:** [Orchestrator.md](Orchestrator.md) details how the backend scheduler schedules and triggers catalog subscriptions.
