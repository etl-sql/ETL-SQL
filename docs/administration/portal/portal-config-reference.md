# Configuration Reference

All settings live under the `"Portal"` key in `appsettings.json`. Every key can be overridden with an environment variable using the double-underscore separator: `Portal__Jwt__Secret`.

```json
{
  "Portal": {
    "DatabasePath":    "./portal.db",
    "Database": {
      "Provider": "Sqlite",
      "ConnectionString": ""
    },
    "ScriptRootPath":  "./Reports",
    "SnapshotDirectory": "./Snapshots",
    "DatasetRootPath": "./data/datasets",
    "MapRootPath": "./data/maps",
    "Storage": {
      "Provider": "Local",
      "KeyRingPath": ""
    },
    "Modules": {
      "Reporting": true,
      "Designer": true,
      "ConnectionCatalog": true,
      "SecretStore": true,
      "Scheduling": true,
      "Operations": true,
      "Documentation": true
    },
    "Studio": {
      "Mode": "SourceControlled",
      "RequireApprovalToPublish": false,
      "RoleCapabilities": {
        "Admin": [ "StudioAccess", "ScriptRead", "ScriptPreview", "ScriptRun", "ScriptSave", "ReportPublish", "ReportApprove", "ScriptIngress", "SourceCommit", "SourcePush" ],
        "Publisher": [ "StudioAccess", "ScriptRead", "ScriptPreview", "ScriptRun", "ScriptSave", "ReportPublish", "ScriptIngress", "SourceCommit" ]
      }
    },
    "Resources": {
      "MaxConcurrentReportExecutions": 4,
      "MaxConcurrentExecutionsPerUser": 2,
      "MaxConcurrentExecutionsPerGroup": 0,
      "InteractiveExecutionWeight": 2,
      "RefreshExecutionWeight": 1,
      "ExecutionTimeoutSeconds":       300,
      "SessionCacheMaxSize":           50,
      "SessionCacheTtlMinutes":        30,
      "SnapshotRetentionPerReport":    20,
      "StorageUsageSampleIntervalSeconds": 30,
      "StorageUsageSampleTimeoutSeconds": 10,
      "StorageUsageSampleMaxFiles": 100000
    },
    "LoadBalancer": {
      "SessionAffinityEnabled":       true,
      "SessionAffinityCookieName":    "ETLSQL_PORTAL_AFFINITY",
      "SessionAffinityCookieMinutes": 480
    },
    "Topology": {
      "ExpectedMode":              "Auto",
      "MinLivePortalNodes":        1,
      "MinLiveOrchestratorNodes":  0,
      "RequirePostgresForHa":      true,
      "RequireSharedKeyRingForHa": true
    },
    "Jwt": {
      "Secret":            "",
      "ExpiryMinutes":     60,
      "RefreshExpiryDays": 7
    },
    "Identity": {
      "Provider": "Local",
      "Oidc": {
        "Enabled": false,
        "Authority": "",
        "ClientId": "",
        "ClientSecret": "",
        "TenantId": "",
        "Scopes": [ "openid", "profile", "email" ],
        "CallbackPath": "/api/auth/oidc/callback",
        "PostLoginRedirectPath": "/index.html",
        "GroupClaimTypes": [ "groups", "roles" ],
        "UsernameClaimType": "preferred_username",
        "EmailClaimType": "email",
        "AdditionalAudiences": [],
        "ClockSkewSeconds": 60
      },
      "Ldap": {
        "Enabled": false,
        "Server": "localhost",
        "Port": 389,
        "UseSsl": false,
        "AllowSelfSignedCertificates": false,
        "Domain": "",
        "BaseDn": "",
        "ServiceUser": "",
        "ServicePassword": "",
        "RoleMappings": {}
      }
    },
    "FirstRun": {
      "AdminUsername": "admin",
      "MustChangePassword": true
    },
    "Dataset": {
      "PreviewCacheMaxRows": 250000
    },
    "Engine": {
      "StartOfWeek": "Monday"
    },
    "Orchestrator": {
      "ApiUrl": null,
      "ApiKey": "",
      "SameHost": false,
      "DatabasePath": "../Orchestrator/etlsql.db"
    }
  }
}
```

| Key | Default | Description |
| :--- | :--- | :--- |
| `DatabasePath` | `./portal.db` | Path to the SQLite database file. Relative to the app working directory. |
| `Database.Provider` | `Sqlite` | State provider. Use `Postgres` for HA deployments with multiple Portal nodes. |
| `Database.ConnectionString` | *(empty)* | PostgreSQL connection string when `Database.Provider = Postgres`. |
| `ScriptRootPath` | `./Reports` | Root directory for `.rptsql` script files. All script paths are validated to stay within this directory. |
| `SnapshotDirectory` | `./Snapshots` | Where report snapshot files are stored. |
| `DatasetRootPath` | `./data/datasets` | Root for Portal-managed cached dataset files. |
| `MaxPreviewRows` | `50000` | Maximum dataset preview rows loaded for an interactive table view. |
| `Dataset.PreviewCacheMaxRows` | `250000` | Global row-weight budget for in-memory dataset preview cache entries. Each cached preview is weighted by its loaded preview row count. |
| `MapRootPath` | `./data/maps` | Root for map assets used by reports. |
| `Storage.Provider` | `Local` | Artifact provider. Use `Smb`/`Unc` with UNC roots for shared HA storage. |
| `Storage.KeyRingPath` | `.portal-keys` beside the database | Data Protection key-ring path. In HA, every Portal node must share the same path. |
| `SourceControl.Enabled` | `false` | Enables source-control write-back for designer saves. |
| `SourceControl.Provider` | `None` | Set to `Git` to commit edited report scripts to a local git working tree. |
| `SourceControl.RepositoryRoot` | *(empty)* | Local git repository root. When `Provider = Git`, `ScriptRootPath` must be inside this directory. |
| `SourceControl.PushOnSave` | `false` | Pushes committed designer saves after each successful commit. Use only with a configured service credential on the host. |
| `SourceControl.ProtectedBranches` | *(empty)* | Branches a Portal-originated commit may not land on without an approved draft behind it. Exact names, or a prefix when the pattern ends in `*` (`release/*`). Empty means nothing is protected. Meant to be enabled together with `Studio.RequireApprovalToPublish`: protecting a branch without a review path only blocks people, and a review path without protection only asks nicely. |
| `Studio.RequireApprovalToPublish` | `false` | Saving a script produces a **draft** that someone other than its author must approve before it can be published. Default off, so an upgrade never interposes a review step into a workflow people depend on. An author can never approve their own draft, whatever roles or capabilities they hold — including Admin. |
| `Studio.RoleCapabilities` | *(empty)* | Deny-by-default Studio capabilities per role. Ten exist: `StudioAccess`, `ScriptRead`, `ScriptPreview`, `ScriptRun`, `ScriptSave`, `ReportPublish`, **`ReportApprove`**, `ScriptIngress`, `SourceCommit`, `SourcePush`. `ReportApprove` is separate from `ReportPublish` so reviewing a change and shipping it can be given to different people. |
| `DataQuality.AllowConnectionPreview` | `false` | Lets the quarantine row editor open the shared connection behind a capture and read its rows. Default off: turning it on lets the web tier open production connections. Even when on, a caller reads rows only for shared connections they are separately granted — steward access gates the feature, the connection grant gates the data. |
| `SourceControl.Remote` | `origin` | Git remote used when `PushOnSave = true`. |
| `SourceControl.Branch` | *(empty)* | Optional branch ref used for push. Empty uses git's current upstream/default push behavior. |
| `SourceControl.CommitterName` | `ETL-SQL Portal` | Git committer name for portal-generated commits. The author name is the logged-in portal user. |
| `SourceControl.CommitterEmail` | `portal@localhost` | Git author/committer email for portal-generated commits. |
| `Modules.Reporting` | `true` | Feature flag for the report library entry page, reporting APIs, session cache, execution worker, dataset key validation, snapshot migration, and reporting startup reconciliation. Disabled routes return 404. |
| `Modules.Designer` | `true` | Feature flag for the browser Studio home, report designer entry page, and design-time APIs. Disabled routes return 404. |
| `Studio.Mode` | `CatalogOnly` | Server-side authoring mode: `Disabled`, `CatalogOnly`, or `SourceControlled`. The checked-in host configuration explicitly uses `SourceControlled`; an omitted setting uses the safer `CatalogOnly` mode. |
| `Studio.RoleCapabilities` | *(empty)* | Case-insensitive role-to-capability grants. Empty mappings deny every Studio API even when the Designer module is enabled. |
| `Modules.ConnectionCatalog` | `true` | Feature flag for shared connection catalog APIs and diagnostics. Disabled routes return 404. |
| `Modules.SecretStore` | `true` | Feature flag for Portal-managed secret vault APIs and secret resolution. Disabled routes return 404. |
| `Modules.Scheduling` | `true` | Feature flag for refresh scheduling and orchestrator polling when Reporting is also enabled. |
| `Modules.Operations` | `true` | Feature flag for operational digest and native admin digest worker loops. Route fencing is a later modularization slice. |
| `Modules.Documentation` | `true` | Feature flag for the Portal-hosted searchable documentation hub at `/docs`. Disabled routes return 404. |

Certified module profiles include a gateway node (`Reporting=false`, `Designer=false`,
`Scheduling=false`, `Operations=false`, `ConnectionCatalog=true`, `SecretStore=true`) and a
reporting node (`Reporting=true`, `Designer=false`, `Scheduling=false`, `Operations=false`,
`ConnectionCatalog=false`, `SecretStore=false`). Security, identity, audit, database migration, and
node heartbeat services remain active in both profiles.

## Studio deployment modes and capabilities

- **`Disabled`** — The Studio entry page and every authoring endpoint return 404.
- **`CatalogOnly`** — Catalog-backed read, preview, run, and save endpoints may be granted. External
  script enumeration/upload, path-based publication, commit, and push endpoints return 404 even if
  a role lists the corresponding capability. `/studio.html` lists only reports and folders where
  the caller has catalog `Manage` authority; creating an active catalog report requires both
  `ScriptSave` and `ReportPublish`, writes an internally generated script key, and never returns the
  storage path to the browser.
- **`SourceControlled`** — External script ingress and source-control operations may be granted in
  addition to catalog operations. Source push remains separate from source commit.

The server checks both mode and capability on every designer, script-content, ingress, publish, and
source-control action. `Admin` and `Publisher` role names do not bypass these checks. Available
capabilities are `StudioAccess`, `ScriptRead`, `ScriptPreview`, `ScriptRun`, `ScriptSave`,
`ReportPublish`, `ScriptIngress`, `SourceCommit`, and `SourcePush`. Keep `SourcePush` restricted to a
deployment identity or administrator role; when `SourceControl.PushOnSave=true`, a commit caller
must hold both `SourceCommit` and `SourcePush`.

The Studio navigation is hidden until `GET /api/studio/session` succeeds. The same server policy
returns 404 for `/studio.html`, `/designer.html`, and every authoring API in `Disabled` mode, so the
navigation state is not the security boundary. Code and Design are equal entry modes over the same
catalog report and optimistic-concurrency version.

| `Resources.MaxConcurrentReportExecutions` | `4` | How many report execution jobs can run simultaneously. |
| `Resources.MaxConcurrentExecutionsPerUser` | `2` | Workload fairness: the most of the shared execution slots a single non-administrator may hold at once, so one user flooding the queue cannot starve everyone else. Keep it below `MaxConcurrentReportExecutions`; administrators are exempt. |
| `Resources.MaxConcurrentExecutionsPerGroup` | `0` | Optional per-group execution quota. `0` disables the group gate; administrators are exempt. |
| `Resources.InteractiveExecutionWeight` | `2` | Weighted queue admission for queued interactive executions. |
| `Resources.RefreshExecutionWeight` | `1` | Weighted queue admission for queued refresh executions. |
| `Resources.ExecutionTimeoutSeconds` | `300` | Per-execution timeout. Jobs exceeding this are cancelled. |
| `Resources.SessionCacheMaxSize` | `50` | Maximum number of in-memory execution sessions cached for result streaming. |
| `Resources.SessionCacheTtlMinutes` | `30` | How long an idle session is kept before eviction. |
| `Resources.SnapshotRetentionPerReport` | `20` | Newest snapshots kept per report. After each successful execution, older snapshot rows and their manifest files are pruned (minimum effective value is 1). |
| `Resources.StorageUsageSampleIntervalSeconds` | `30` | Background cadence for dataset/snapshot storage usage samples. Minimum effective value is 1 second. |
| `Resources.StorageUsageSampleTimeoutSeconds` | `10` | Per-sample timeout before the previous successful storage usage values are retained and failure telemetry is exposed. Minimum effective value is 1 second. |
| `Resources.StorageUsageSampleMaxFiles` | `100000` | Maximum files visited per storage root sample before the previous successful values are retained and failure telemetry is exposed. Minimum effective value is 1 file. |
| `LoadBalancer.SessionAffinityEnabled` | `true` | Emits a sticky-session cookie for load balancers. Keep enabled for multi-node deployments because interactive report sessions are process-local. |
| `LoadBalancer.SessionAffinityCookieName` | `ETLSQL_PORTAL_AFFINITY` | Cookie name load balancers should use for Portal node affinity. |
| `LoadBalancer.SessionAffinityCookieMinutes` | `480` | Sticky-session cookie lifetime in minutes. |
| `Topology.ExpectedMode` | `Auto` | Readiness policy for `/healthz`: `Auto`, `Standalone`, `Departmental`, or `HighAvailability`. **`Auto` infers `HighAvailability` from PostgreSQL *or* a configured `Storage.KeyRingPath`, and never infers `Departmental`.** A single-node install that merely moved its key ring off the default path is therefore treated as HA and, with the defaults below, `/healthz` returns 503 until it is given PostgreSQL. Set this explicitly on anything that is not a plain single-node install. |
| `Topology.MinLivePortalNodes` | `1` | Live Portal heartbeats required before an HA node reports ready. Keep at `1` while bootstrapping the first node, then raise it. Values below `1` are clamped to `1`. |
| `Topology.MinLiveOrchestratorNodes` | `0` | Live Orchestrator heartbeats required before an HA node reports ready. `0` never withholds readiness for Orchestrator availability. |
| `Topology.RequirePostgresForHa` | `true` | In HA mode, refuse readiness unless both the Portal and Orchestrator stores are PostgreSQL. Findings: `ha-requires-portal-postgres`, `ha-requires-orchestrator-postgres`. |
| `Topology.RequireSharedKeyRingForHa` | `true` | In HA mode, refuse readiness unless `Storage.KeyRingPath` is set. An unset path means each node protects data with its own key ring, so a cookie or secret written by one node is unreadable on the next. Finding: `ha-requires-shared-key-ring`. |
| `Jwt.Secret` | *(required)* | HMAC-SHA256 signing secret. **Must be at least 32 characters.** The portal will refuse to start without it. |
| `Jwt.ExpiryMinutes` | `60` | How long an access token is valid. |
| `Jwt.RefreshExpiryDays` | `7` | How long a refresh token is valid. |
| `Identity.Provider` | `Local` | Main authentication provider model (`Local` or `Oidc`). If LDAP is enabled, directory logins are supported alongside the selected main provider. |
| `Identity.Oidc.Enabled` | `false` | Master switch for federated OIDC login. When `true`, the portal validates the OIDC settings at startup and refuses to start if any are missing or unsafe. Local login keeps working alongside it. |
| `Identity.Oidc.Authority` | *(empty)* | OIDC issuer/discovery URL (must be **HTTPS**), for example `https://login.microsoftonline.com/<tenant-id>/v2.0`. The portal reads `/.well-known/openid-configuration` and JWKS from it. |
| `Identity.Oidc.ClientId` | *(empty)* | OIDC client/application id registered with the provider. |
| `Identity.Oidc.ClientSecret` | *(empty)* | Confidential client secret for the authorization-code exchange. Used verbatim — supply it via the `Portal__Identity__Oidc__ClientSecret` environment variable or a protected configuration source, not in committed files. |
| `Identity.Oidc.TenantId` | *(empty)* | Optional tenant identifier used by tenant-aware identity providers and deployment templates. |
| `Identity.Oidc.Scopes` | `openid`, `profile`, `email` | Scopes requested at authorization time. `openid` is required and added automatically. |
| `Identity.Oidc.CallbackPath` | `/api/auth/oidc/callback` | Absolute path the provider redirects back to. Register `https://<portal-host>/api/auth/oidc/callback` as a redirect URI with the provider. |
| `Identity.Oidc.PostLoginRedirectPath` | `/index.html` | App page the user lands on after a successful federated login. The callback renders a hand-off page that stores the session (tokens are never placed in the URL) and forwards here. |
| `Identity.Oidc.GroupClaimTypes` | `groups`, `roles` | Token claims (in priority order) mapped into portal groups for folder and dataset ACLs. Match a portal group by its `AdGroup` (or `Name`); membership is reconciled on every login. |
| `Identity.Oidc.RequiredClaims` | *(empty)* | Claim types that must be present in a validated id_token (beyond `sub`). Login fails closed if any is missing — use to mandate, for example, `email` or a tenant claim. |
| `Identity.Oidc.UsernameClaimType` | `preferred_username` | Claim used as the portal username (falls back to `preferred_username` then `sub`). |
| `Identity.Oidc.EmailClaimType` | `email` | Claim used as the user's email address. |
| `Identity.Oidc.AdditionalAudiences` | *(empty)* | Additional token audiences accepted during id_token validation beyond `ClientId`. |
| `Identity.Oidc.ClockSkewSeconds` | `60` | Allowed clock skew when validating id_token lifetime. |
| `Identity.Ldap.Enabled` | `false` | Set to `true` to enable LDAP and Active Directory integration. |
| `Identity.Ldap.Server` | `localhost` | The hostname or IP address of the LDAP/AD server. |
| `Identity.Ldap.Port` | `389` | The server connection port (usually 389 for plain/STARTTLS, 636 for LDAPS/SSL). |
| `Identity.Ldap.UseSsl` | `false` | Set to `true` to establish connections via SSL/TLS (LDAPS). |
| `Identity.Ldap.AllowSelfSignedCertificates` | `false` | Allows self-signed LDAP TLS certificates. Use only in isolated development/test directories; production should trust the issuing CA instead. |
| `Identity.Ldap.Domain` | *(empty)* | Default DNS or NetBIOS domain suffix used to qualify logins (e.g. `corp.local`). |
| `Identity.Ldap.BaseDn` | *(empty)* | LDAP directory base search path (e.g. `OU=Users,DC=corp,DC=local`). |
| `Identity.Ldap.ServiceUser` | *(empty)* | Optional service account distinguished name or UPN for searching. |
| `Identity.Ldap.ServicePassword` | *(empty)* | Optional password for the service account. |
| `Identity.Ldap.RoleMappings` | *(empty)* | Key-value pairs mapping Active Directory groups (full DNs or short CNs) to Portal Roles (`Admin`, `Publisher`, `Viewer`). |
| `FirstRun.AdminUsername` | `admin` | Username created on first start if no users exist yet. |
| `FirstRun.AdminPassword` | *(empty)* | Required bootstrap password for the first-run admin account when the Portal database has no users. Prefer setting it through `Portal__FirstRun__AdminPassword`; remove it after the initial password change. |
| `FirstRun.MustChangePassword` | `true` | Forces the seeded first-run admin to change the initial password before using API routes. Keep enabled for production; disposable automation topologies may set it to `false` only with a strong per-run bootstrap password. |
| `Engine.StartOfWeek` | `Monday` | Day used as the start of week when resolving `RELDATE` week-boundary expressions (`W`, `W-1`, etc.). |
| `Orchestrator.ApiUrl` | *(empty)* | Base URL of the Orchestrator Service HTTP API. |
| `Orchestrator.ApiKey` | *(empty)* | Shared secret API key used for authenticating Orchestrator HTTP calls. |
| `Orchestrator.SameHost` | `false` | Set to `true` to enable managing the Orchestrator Windows Service control locally from the portal. |
| `Orchestrator.DatabasePath` | `../Orchestrator/etlsql.db` | Location of the Orchestrator's SQLite DB from Portal context (used to query job status/history locally). |

> [!IMPORTANT]
> **`Jwt.Secret` must be set before production use.** Generate a strong random string of at least 32 characters and set it via an environment variable rather than storing it in the checked-in `appsettings.json`:
> ```
> Portal__Jwt__Secret=<your-secret-here>
> ```

---

## HA Configuration Summary

For a load-balanced Portal fleet:

- Set `Portal:Database:Provider = Postgres` and the same `Portal:Database:ConnectionString` on every
  Portal node.
- Set `Portal:Storage:Provider = Smb` or `Unc`, and point `ScriptRootPath`, `SnapshotDirectory`,
  `DatasetRootPath`, `MapRootPath`, and `Storage.KeyRingPath` at shared UNC paths.
- Keep `Portal:Jwt:Secret`, `Portal:Dataset:AtRestKey`, and `Portal:Orchestrator:ApiKey` identical
  across Portal nodes.
- Configure the load balancer for sticky sessions using `LoadBalancer.SessionAffinityCookieName`
  (`ETLSQL_PORTAL_AFFINITY` by default).
- Use `GET /healthz` for load-balancer probes. It fails closed with HTTP 503 when PostgreSQL,
  shared snapshot storage, or the node-registry/lease store is unavailable.

The full HA setup and SQLite-to-PostgreSQL migration procedure is in
[Practical High Availability Configuration](../platform/state-and-ha.md#practical-high-availability-configuration)
and [Migrating from SQLite to PostgreSQL](../platform/operator-cli.md#migrating-from-sqlite-to-postgresql--etl-sql-admin-migrate-database).