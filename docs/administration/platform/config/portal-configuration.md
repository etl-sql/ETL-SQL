# Portal Configuration

> **Applies to:** Team · Enterprise · SaaS

Configure the Portal web server, database provider, shared storage, module flags, Studio boundaries, report player, session management, connectors, lineage, and identity providers.

ETL-SQL settings can be configured via `appsettings.json`, environment variables (replace `:` with `__`), or command-line parameters.

---

## Portal Database and Storage

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Portal:Database:Provider` | string | `Sqlite` | Database backing Portal state: `Sqlite` or `Postgres`. Use `Postgres` for HA multi-node deployments. |
| `Portal:Database:ConnectionString` | string | `""` | Connection details when `Postgres` provider is specified. |
| `Portal:DatabasePath` | string | `./portal.db` | Local SQLite database path. Used when provider is `Sqlite`. |
| `Portal:TenantId` | string | unset | Server-owned tenant identity for a host-fixed Managed Dedicated Portal. Required for SaaS portability export identity. |
| `Portal:Storage:Provider` | string | `Local` | Artifact provider: `Local`, `Smb`/`Unc`, `S3`, or `AzureBlob`. Object providers require Portal PostgreSQL and a pre-created bucket/container. |
| `Portal:Storage:KeyRingPath` | string | `null` | Folder storing Data Protection decryption keys. Must be shared across all nodes in HA deployments. |
| `Portal:Storage:ObjectPrefix` | string | `null` | Optional object-key prefix shared by all Portal nodes. |
| `Portal:Storage:Bucket` | string | `null` | S3 bucket name. |
| `Portal:Storage:Region` | string | `us-east-1` | S3 region when `ServiceUrl` is unset. |
| `Portal:Storage:ServiceUrl` | string | `null` | Optional S3-compatible endpoint. |
| `Portal:Storage:ForcePathStyle` | boolean | `false` | Use path-style S3 addressing, commonly required by compatible endpoints. |
| `Portal:Storage:AccessKey` | string | `null` | Optional S3 access key; omit with `SecretKey` to use the AWS credential chain. Supply through protected configuration. |
| `Portal:Storage:SecretKey` | string | `null` | Optional S3 secret key paired with `AccessKey`. Supply through protected configuration. |
| `Portal:Storage:AzureConnectionString` | string | `null` | Azure Blob connection string. Supply through protected configuration. |
| `Portal:Storage:Container` | string | `null` | Azure Blob container name. |
| `Portal:Storage:StagingRetentionHours` | integer | `24` | Minimum age before non-authoritative staging residue is garbage-collected. Values below 1 are clamped to 1. |
| `Portal:Storage:ReconciliationIntervalMinutes` | integer | `60` | Interval for content/hash reconciliation and staging collection. Values below 1 are clamped to 1. |
| `Portal:ScriptRootPath` | string | `./Reports` | Folder containing reports and dashboard scripts (`.rptsql`). |
| `Portal:SnapshotDirectory` | string | `./Snapshots` | Directory where PDF/CSV dashboard exports are saved. |
| `Portal:MapRootPath` | string | `./data/maps` | Base folder path storing GeoJSON map files. |
| `Portal:DatasetRootPath` | string | `./data/datasets` | Folder storing shared dataset Parquet archives. |

---

## Portal-to-Orchestrator Integration

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Portal:Orchestrator:ApiUrl` | string | `http://localhost:5001` | Base URL of the Orchestrator service. |
| `Portal:Orchestrator:ApiKey` | string | `""` | Service credential sent to the Orchestrator. Configure from a protected source. |
| `Portal:Orchestrator:IdentitySigningSecret` | string | `""` | Dedicated 32+ byte secret to sign short-lived caller assertions. Must match the Orchestrator's `IdentitySigningSecret`. |
| `Portal:Orchestrator:PollIntervalSeconds` | integer | `60` | How often the Portal polls Orchestrator job history for dataset-refresh and subscription completions. |
| `Portal:SubscriptionRetryDelaySeconds` | integer | `60` | Wait time to retry failed email subscription dispatches. |

---

## Shared Tenancy (SaaS multi-tenant mode)

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Portal:SharedTenancy:Enabled` | boolean | `false` | Enables fail-closed Shared tenant context enforcement. |
| `Portal:SharedTenancy:LifecycleManagementKey` | string | unset | Separate 32+ character platform credential enabling Shared lifecycle APIs and request fencing. Supply from protected configuration; it is not a tenant credential. |
| `Portal:SharedTenancy:DefaultRelease` | string | `unversioned` | Server-owned initial Shared release assignment used by signed provisioning. |
| `Portal:SharedTenancy:DefaultMaxConcurrentJobs` | integer | `1` | Initial per-tenant scheduled-job concurrency assigned by provisioning. |
| `Portal:SharedTenancy:DefaultMaxStorageMb` | integer | `1024` | Initial per-tenant storage assignment; minimum `128`. |
| `Portal:SharedTenancy:DefaultMaxReportSessions` | integer | `1` | Initial per-tenant interactive report-session assignment. |

---

## Portal Modules

Portal modules enable or disable major feature areas and their API routes.

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Portal:Modules:Reporting` | boolean | `true` | Report catalog, report player, datasets, subscriptions, and reporting worker loops. Disabled routes return 404. |
| `Portal:Modules:Designer` | boolean | `true` | Browser report designer entry page and design-time APIs. Disabled routes return 404. |
| `Portal:Modules:ConnectionCatalog` | boolean | `true` | Shared connection catalog and diagnostics API routes. Disabled routes return 404. |
| `Portal:Modules:SecretStore` | boolean | `true` | Portal-managed secret vault APIs and secret resolution surface. Disabled routes return 404. |
| `Portal:Modules:Scheduling` | boolean | `true` | Refresh scheduling, Orchestrator polling, and scheduled work. Requires `Reporting` to be enabled. |
| `Portal:Modules:Operations` | boolean | `true` | Operational health, fleet status, audit, and administrative telemetry worker loops. |
| `Portal:Modules:Documentation` | boolean | `true` | Portal-hosted documentation surfaces. |

> [!NOTE]
> Security, identity, audit, database migration, and node heartbeat services remain active regardless of module flags — they protect the host itself.

Certified topology profiles:
- **Gateway node**: `Reporting=false`, `Designer=false`, `Scheduling=false`, `Operations=false`, `ConnectionCatalog=true`, `SecretStore=true`
- **Reporting node**: `Reporting=true`, `Designer=false`, `Scheduling=false`, `Operations=false`, `ConnectionCatalog=false`, `SecretStore=false`

---

## Portal Studio

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Portal:Studio:Mode` | enum | `CatalogOnly` | Studio boundary: `Disabled`, `CatalogOnly`, or `SourceControlled`. |
| `Portal:Studio:RoleCapabilities:<role>` | string array | `[]` | Explicit Studio capabilities granted to a role. Empty mappings deny authoring. |

Studio capability names: `StudioAccess`, `ScriptRead`, `ScriptPreview`, `ScriptRun`, `ScriptSave`, `ReportPublish`, `ReportApprove`, `ScriptIngress`, `SourceCommit`, `SourcePush`.

- `Mode=Disabled` removes the entry page and all authoring APIs.
- `Mode=CatalogOnly` permits only explicitly granted catalog read/preview/run/save operations.
- `Mode=SourceControlled` allows external/source operations only when individual capabilities are also granted.

---

## Topology & High Availability Validation

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Topology.ExpectedMode` / `Portal:Topology:ExpectedMode` | string | `SingleNode` | Expected deployment mode (`SingleNode`, `Enterprise`, `SaaS`). |
| `Topology.MinLivePortalNodes` / `Portal:Topology:MinLivePortalNodes` | integer | `1` | Minimum live Portal nodes required for healthy cluster. |
| `Topology.MinLiveOrchestratorNodes` / `Portal:Topology:MinLiveOrchestratorNodes` | integer | `1` | Minimum live Orchestrator nodes required for healthy cluster. |
| `Topology.RequirePostgresForHa` / `Portal:Topology:RequirePostgresForHa` | boolean | `true` | Requires PostgreSQL backend when running in HA topology. |
| `Topology.RequireSharedKeyRingForHa` / `Portal:Topology:RequireSharedKeyRingForHa` | boolean | `true` | Requires shared Data Protection key ring across nodes in HA topology. |

---

## Resource Controls

| Key | Default | Description |
| :--- | :--- | :--- |
| `Portal:Resources:MaxConcurrentReportExecutions` | `4` | Max total active queries allowed. |
| `Portal:Resources:MaxConcurrentExecutionsPerUser` | `2` | Concurrency cap per user. |
| `Portal:Resources:MaxConcurrentExecutionsPerGroup` | `0` | Concurrency limit per AD group. `0` = unlimited. |
| `Portal:Resources:InteractiveExecutionWeight` | `2` | Concurrency cost weight for real-time user clicks. |
| `Portal:Resources:RefreshExecutionWeight` | `1` | Concurrency cost weight for background dashboard refreshes. |
| `Portal:Resources:ExecutionTimeoutSeconds` | `300` | Max runtime allowed for portal queries. |
| `Portal:Resources:SessionCacheMaxSize` | `50` | Max concurrent cached sessions. |
| `Portal:Resources:SessionCacheTtlMinutes` | `30` | TTL for cached interactive sessions. |
| `Portal:Resources:PersistAdHocInteractions` | `true` | Saves dashboard parameters on close. |
| `Portal:Resources:SnapshotRetentionPerReport` | `20` | Maximum history snapshots retained per visual. |
| `Portal:MaxPreviewRows` | `50000` | Maximum preview rows displayed in GUI tables. |

---

## Designer Limits

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Portal:DesignerLimits:MaxDataPreviewRows` | integer | `100` | Maximum rows returned by an interactive Studio run or preview. Range: 1–1000. |
| `Portal:DesignerLimits:MaxDataPreviewBytes` | integer | `262144` | Maximum serialized row payload for a Studio run or preview. Range: 1024–16777216. |
| `Portal:DesignerLimits:MaxDataPreviewSeconds` | integer | `15` | Wall-clock timeout for an interactive Studio run or preview. Range: 1–300. |

---

## Load Balancer

| Key | Default | Description |
| :--- | :--- | :--- |
| `LoadBalancer.SessionAffinityEnabled` / `Portal:LoadBalancer:SessionAffinityEnabled` | `true` | Emits a cookie indicating target node for sticky routing. |
| `LoadBalancer.SessionAffinityCookieName` / `Portal:LoadBalancer:SessionAffinityCookieName` | `ETLSQL_PORTAL_AFFINITY` | Cookie name load balancers use to route requests to the correct node. |
| `LoadBalancer.SessionAffinityCookieMinutes` / `Portal:LoadBalancer:SessionAffinityCookieMinutes` | `480` | Duration of the sticky session cookie. |

---

## JWT Secrets

| Key | Default | Description |
| :--- | :--- | :--- |
| `Portal:Jwt:Secret` | `""` | Signing secret for JWT authorization cookies. Must be 32+ characters in production. |
| `Portal:Jwt:PreviousSecrets` | `[]` | Older keys accepted during token rotation phases. |
| `Portal:Jwt:ExpiryMinutes` | `60` | Lifetime of access tokens. |
| `Portal:Jwt:RefreshExpiryDays` | `7` | Lifetime of refresh tokens. |

---

## Rate Limiting

| Key | Default | Description |
| :--- | :--- | :--- |
| `Portal:RateLimit:AuthPermitLimit` | `20` | Auth requests permitted in the rate window. |
| `Portal:RateLimit:AuthWindowSeconds` | `60` | Rate limit tracking duration for auth requests. |
| `Portal:RateLimit:AnonymousTokenPermitLimit` | `60` | Guest requests permitted per window. |
| `Portal:RateLimit:AnonymousTokenWindowSeconds` | `60` | Rate limit window for guest accounts. |

---

## Portal Security

| Key | Default | Description |
| :--- | :--- | :--- |
| `Portal:Security:FrameAncestors` | `[]` | URLs allowed to iframe the Portal (Content Security Policy). |

---

## Dataset Cryptography

Production portals require `Portal:Dataset:AtRestKey`, a base64 value decoding to at least 32 bytes.

| Key | Default | Description |
| :--- | :--- | :--- |
| `Portal:Dataset:AtRestKey` | `""` | Base64 32-byte key used to encrypt cached Parquet datasets. Required in production. |
| `Portal:Dataset:AtRestKeyVersion` | `v1` | Current encryption key version identifier. |
| `Portal:Dataset:PreviousAtRestKeys` | `{}` | Map of older key versions to decrypt existing historical datasets during rotation. |
| `Portal:Dataset:AllowMachineFallback` | `false` | Permits OS-level machine key fallbacks. Disable in multi-host HA clusters. |
| `Portal:Dataset:PreviewCacheMaxRows` | `250000` | Global row-weight budget for in-memory dataset preview cache entries. |

New Enterprise deployments should use `Portal:KeyManagement`, which keeps resolved material outside appsettings, portable exports, job payloads, and execution images:

```json
{
  "Portal": {
    "TenantId": "tenant-acme",
    "KeyManagement": {
      "Enabled": true,
      "Bindings": [
        { "Purpose": "Dataset",    "Version": "v1", "KeyId": "dataset-key",    "EnvironmentVariable": "ETLSQL_KEY_DATASET_V1",    "IsCurrent": true },
        { "Purpose": "Credential", "Version": "v1", "KeyId": "credential-key", "EnvironmentVariable": "ETLSQL_KEY_CREDENTIAL_V1", "IsCurrent": true },
        { "Purpose": "Artifact",   "Version": "v1", "KeyId": "artifact-key",   "EnvironmentVariable": "ETLSQL_KEY_ARTIFACT_V1",   "IsCurrent": true },
        { "Purpose": "Checkpoint", "Version": "v1", "KeyId": "checkpoint-key", "EnvironmentVariable": "ETLSQL_KEY_CHECKPOINT_V1", "IsCurrent": true }
      ]
    }
  }
}
```

In Shared (multi-tenant) mode, each binding requires a `Scope` naming its tenant. Every tenant must have independently resolvable current bindings for all four purposes.

---

## Report Player and Session

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ReportPlayer:Port` | integer | `5200` | Port listening for report preview sessions. |
| `ReportPlayer:ExecutionTimeoutSeconds` | integer | `300` | Max timeout for generating an interactive preview chart (5 minutes). |
| `Session:StaleSessionRetentionDays` | integer | `7` | Days to preserve inactive user session records. |
| `Session:PersistentSessionTTLHours` | integer | `24` | TTL for cookie session configurations. |
| `Session:PersistenceDefault` | boolean | `true` | Saves session histories by default. |
| `Session:Root` | string | `null` | Path to store user session cache objects. Defaults to temp/appdata. |

---

## Connectors

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Connectors:Retry:MaxAttempts` | integer | `3` | Number of times connection attempts are retried. |
| `Connectors:Retry:BaseDelaySeconds` | float | `1.0` | Initial delay between failed requests (exponential backoff). |
| `Connectors:DataWarehouse:DefaultCommandTimeoutSeconds` | integer | `1800` | Max duration of SQL commands on target database servers (30 minutes). |
| `Connectors:DataWarehouse:SchemaCacheTtlSeconds` | integer | `300` | Duration target table schemas are cached to skip re-verification. |
| `Connectors:DataWarehouse:SchemaSoftRefreshIntervalSeconds` | integer | `300` | Age at which cached metadata remains usable but triggers background refresh. |
| `Connectors:DataWarehouse:SchemaDiskCacheMaxAgeDays` | integer | `14` | Maximum age for on-disk schema cache files before they are ignored and pruned. |
| `Connectors:Ftp` | object | `{...}` | Connection settings for FTP connections. |
| `Connectors:Sftp` | object | `{...}` | Connection settings for SFTP connections. |
| `Connectors:AzureBlob` | object | `{...}` | Storage string and default container for Azure Blob connectivity. |

---

## Lineage

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Lineage:Namespace` | string | `etl-sql` | `SET LINEAGE_NAMESPACE = 'ns'` | Default namespace for lineage output. |
| `Lineage:OpenLineageFile` | string | `null` | — | Local target file path to append OpenLineage JSON events. |
| `Lineage:OpenLineageEndpoint` | string | `null` | — | Target collector URL for OpenLineage telemetry. |
| `Lineage:ImportCatalogMetadata` | boolean | `false` | `SET LINEAGE_IMPORT_CATALOG = ON\|OFF` | Queries database catalogs for schema comments on first table scans. |

---

## Snippets

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Snippets:UserSnippetsPath` | string | `""` | Directory containing user-defined editor autocomplete snippets. |

---

## ConnectionStrings

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ConnectionStrings:DefaultConnection` | string | `Data Source=portal.db` | Fallback connection configuration for database targets. |

---

## Identity Providers

| Key | Default | Description |
| :--- | :--- | :--- |
| `Portal:Identity:Provider` | `Local` | Main authentication provider: `Local`, `Oidc`. LDAP logins can be enabled alongside the selected provider when `Ldap:Enabled` is true. |

### OIDC (`Portal:Identity:Oidc`)

| Key | Default | Description |
| :--- | :--- | :--- |
| `Enabled` | `false` | Enables federated OIDC login and startup validation. |
| `Authority` | — | HTTPS identity authority/discovery endpoint URL. |
| `ClientId` | — | Client application ID. |
| `ClientSecret` | — | Confidential client secret. Provide through `Portal__Identity__Oidc__ClientSecret` or another protected configuration source. |
| `TenantId` | — | Optional target directory tenant ID. |
| `Scopes` | `["openid","profile","email"]` | Authorization scopes. `openid` is required. |
| `CallbackPath` | `/api/auth/oidc/callback` | Redirect path registered with the identity provider. |
| `PostLoginRedirectPath` | `/index.html` | Portal page loaded after the callback establishes the session. |
| `GroupClaimTypes` | — | Array of claims parsed to resolve user groups (e.g., `["groups","roles"]`). |
| `UsernameClaimType` | `preferred_username` | Claim used as the portal username. |
| `EmailClaimType` | `email` | Claim used as the user's email address. |
| `AdditionalAudiences` | `[]` | Extra accepted id_token audiences beyond `ClientId`. |
| `RequiredClaims` | `[]` | Claim types that must be present in the validated id_token. |
| `ClockSkewSeconds` | `60` | Allowed id_token lifetime clock skew. |

### Workload identity (`Portal:Identity:WorkloadIdentity`)

| Key | Default | Description |
| :--- | :--- | :--- |
| `Enabled` | `false` | Enables signed workload assertion exchange at `/api/auth/workload-token`. |
| `MaximumAssertionLifetimeSeconds` | `600` | Maximum external assertion lifetime. Effective range is 60–600 seconds. |
| `ClockSkewSeconds` | `30` | Assertion lifetime skew. Effective range is 0–120 seconds. |
| `Bindings` | `[]` | Exact federation policies described below. Wildcards are unsupported. |

Each binding contains `Id`, `Provider`, `ServiceAccountClientId`, `TenantId`, `Issuer`, `Subject`,
`Audience`, `Resource`, `Operations`, `Enabled`, and `RequireApproval`. `Provider` is `github`,
`gitlab`, `azure_devops`, or `private_key_jwt`. `Resource` is the exact Portal API path and every
operation must be an existing service-account scope. For `private_key_jwt`, also set `PublicKeyPem`
to the public key or certificate PEM; never place the private key in Portal configuration.

```json
{
  "Portal": {
    "Identity": {
      "WorkloadIdentity": {
        "Enabled": true,
        "MaximumAssertionLifetimeSeconds": 600,
        "Bindings": [
          {
            "Id": "github-main-report",
            "Provider": "github",
            "ServiceAccountClientId": "sa_0123456789abcdef0123456789abcdef",
            "TenantId": "production",
            "Issuer": "https://token.actions.githubusercontent.com",
            "Subject": "repo:etl-sql/ETL-SQL:ref:refs/heads/main",
            "Audience": "etl-sql-ci",
            "Resource": "/api/reports/42/execute",
            "Operations": ["reports.execute"],
            "RequireApproval": true
          }
        ]
      }
    }
  }
}
```

### LDAP (`Portal:Identity:Ldap`)

| Key | Default | Description |
| :--- | :--- | :--- |
| `Enabled` | `false` | Enables LDAP verification alongside the selected provider. |
| `Server` | `localhost` | Domain controller address. |
| `Port` | `389` | Target directory port. |
| `UseSsl` | `false` | Runs LDAP queries over a secure connection. |
| `AllowSelfSignedCertificates` | `false` | Allows self-signed LDAP TLS certificates for isolated dev/test directories. |
| `Domain` | `""` | Active Directory DNS or NetBIOS domain name. |
| `BaseDn` | — | Base Distinguished Name for scope searches. |
| `ServiceUser` / `ServicePassword` | — | LDAP bind service account credentials. |
| `RoleMappings` | — | JSON key-value map of LDAP groups to portal roles (e.g., `"GG-Admins": "Admin"`). |

---

## Related

- [Configuration Settings Reference](../appsettings-reference.md) — full config hub
- [Orchestrator Configuration](orchestrator-configuration.md) — Orchestrator API and job settings
- [State and HA](../state-and-ha.md) — HA topology and shared storage requirements
- [Portal Administration](../../portal/README.md)
- [Platform Administration](../README.md)
- [Workload Identity and Machine-to-Machine Security](../../../architecture/workload-identity.md)
