# Architecture: ETL-SQL Report Portal

The Report Portal (`ETL-SQL-Portal`) is an ASP.NET Core 10 web application that exposes report execution, snapshot management, subscriptions, and user/group administration through a REST API and a static HTML/JS front-end. It sits at **Tier 5** of the dependency hierarchy, above the shared report hosting/runtime services it calls for execution.

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
                                                                         ──< Subscription ──> PortalUser
                                                                         ──< DatasetJob
PortalUser ──< RefreshToken
SmtpConnection  (standalone)
AuditLog        (append-only)
```

### Key Design Decisions

- **`PortalUser` extends `IdentityUser<int>`** — all password hashing, lockout counters, and token validation delegate to ASP.NET Identity. Custom columns (`IsActive`, `MustChangePassword`, `CreatedAt`) are added via the EF `OnModelCreating` override in `PortalDbContext`.

- **Soft-delete on `Report`** — `IsDeleted = true` hides records from all queries. Snapshots are preserved on disk after soft-deletion; hard deletion is a manual operation.

- **`FolderAcl` is group-based only** — individual user permissions are not supported. All access control flows through group membership, which simplifies bulk permission changes (change the group, not every user).

- **`Subscription.ScriptPath`** — the Orchestrator job script is generated at subscription-creation time and stored as a `.etlsql` file under `ScriptRootPath`. This path is recorded so the job file can be cleaned up on subscription deletion.

- **`SmtpConnection.EncryptedPassword`** — stored via .NET Data Protection API. The `SmtpPasswordProtector` service wraps `IDataProtector`. The password is never returned to clients in any API response.

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

Three ASP.NET Identity roles — `Admin`, `Publisher`, `Viewer` — are seeded on first run. Controllers use `[Authorize(Roles = "...")]` attributes. Folder-level ACLs are checked inline in controller actions via `GetEffectivePermissionAsync()`, which walks the `FolderAcl` table for groups the current user belongs to.

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

**`ExecutionJobService`** is a singleton that manages a `SemaphoreSlim` with `MaxConcurrentReportExecutions` slots. Jobs that exceed the slot limit are queued.

**`SessionCache`** is a singleton hosted service that holds in-memory execution sessions for active result streaming. It evicts idle sessions after `SessionCacheTtlMinutes` and enforces a max size of `SessionCacheMaxSize`. The cache is not persisted — a portal restart clears all in-progress sessions.

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
| `execution` | `ExecutionCapacityHealthCheck` | `Degraded` if execution slots nearing cap |

The `/health` endpoint uses a custom `ResponseWriter` that serializes the `HealthReport` to a structured JSON document. It is mapped with `.AllowAnonymous()` so monitoring tools do not need a JWT.

---

## 8. Integration Testing Strategy

Tests live in `tests/ETL-SQL.ReportPortal.Tests` and use `Microsoft.AspNetCore.Mvc.Testing`.

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

### Reports

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/folders/{folderId}/reports` | Any | List reports in folder |
| POST | `/api/reports` | Admin, Publisher | Publish report |
| GET | `/api/reports/{id}` | Any | Get report metadata |
| PUT | `/api/reports/{id}` | Admin, Publisher | Update report metadata |
| DELETE | `/api/reports/{id}` | Admin, Publisher | Soft-delete report |

### Execution

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| POST | `/api/reports/{id}/execute` | Any | Start execution job |
| GET | `/api/jobs/{jobId}` | Any | Poll job status |
| GET | `/api/reports/{id}/snapshot` | Any | Get snapshot JSON |
| GET | `/api/reports/{id}/snapshot/manifest` | Any | Get snapshot manifest |
| POST | `/api/reports/{id}/refresh` | Any | Rebuild snapshot |
| POST | `/api/reports/{id}/parameters` | Any | Set parameter values |

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
| DELETE | `/api/subscriptions/{id}` | Any | Delete subscription |
| GET | `/api/subscriptions/{id}/history` | Any | Delivery history |

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
| DELETE | `/api/admin/groups/{id}` | Admin | Delete group |
| GET | `/api/admin/groups/{id}/members` | Admin | List group members |
| POST | `/api/admin/groups/{id}/members` | Admin | Add member |
| DELETE | `/api/admin/groups/{id}/members/{userId}` | Admin | Remove member |

### Admin — SMTP

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/smtp` | Admin | List SMTP connections |
| POST | `/api/admin/smtp` | Admin | Create SMTP connection |
| PUT | `/api/admin/smtp/{id}` | Admin | Update SMTP connection |
| DELETE | `/api/admin/smtp/{id}` | Admin | Delete SMTP connection |

### Admin — Audit

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/api/admin/audit` | Admin | Browse audit log (paginated) |
| GET | `/api/admin/audit/export/csv` | Admin | Download audit log as CSV |

### System

| Method | Path | Auth | Description |
| :--- | :--- | :--- | :--- |
| GET | `/health` | Public | Health check (JSON) |
