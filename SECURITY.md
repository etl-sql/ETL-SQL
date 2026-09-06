# ETL-SQL Security Model

ETL-SQL treats scripts as powerful automation inputs. A script can move data, call connectors, write files, send email, run scheduled jobs, and publish reports. That is useful, but it is also inherently risky.

This document describes the controls ETL-SQL provides, where they are enforced, what defaults are used, and what risks remain. It is intended for security reviewers, administrators, and AI agents generating or modifying ETL-SQL scripts.

ETL-SQL does not claim that a script sandbox eliminates all risk. The goal is narrower and more practical: reduce accidental damage, block common host escape paths, make dangerous actions visible, and give administrators clear places to harden deployments.

---

## Supported Releases

ETL-SQL is pre-1.0 software under active development. Security fixes are made on the latest released
minor line. As of this review, that is `0.18.x`. Older `0.x` lines do not receive routine security
backports unless a published advisory explicitly says otherwise.

| Version | Security updates |
| :--- | :--- |
| `0.18.x` | Supported |
| `< 0.18` | Upgrade to the latest release |
| Unreleased builds | Development only; not a supported production baseline |

The applicable deployment claim also matters. A feature present in source is not automatically
certified for every topology. See [Deployment Profiles](docs/architecture/deployment-profiles.md) and
the [v0.18.0 certification ledger](artifacts/release-evidence/0.18.0/deployment-profiles/claims-index.md).

---

## 1. Threat Model

ETL-SQL assumes that script content may be mistaken, over-broad, or hostile. The engine therefore applies guardrails around host filesystem access, network egress, environment variables, credentials, resource use, and script self-modification.

The primary risks addressed are:

- Reading sensitive host files such as SSH keys, cloud credentials, system files, build outputs, or session metadata.
- Writing executable or script files that could alter application behavior.
- Exfiltrating data through unapproved network destinations or environment variables.
- Accidentally running destructive operations without previewing the impact.
- Exhausting host resources through large file operations, recursive directory walks, large string results, regex work, or parallel execution.
- Leaking credentials through diagnostics, connection metadata, or generated output.

The primary risks not fully solved by ETL-SQL are:

- A trusted user intentionally writing a destructive script.
- A script operating inside a broad approved safe zone.
- Secrets intentionally embedded in string literals, email bodies, report text, or `PRINT` output.
- An administrator choosing permissive configuration such as unrestricted paths or wildcard network access.
- Compromise of the host OS, process memory, build environment, or deployment credentials.

---

## 2. Security Defaults

| Area | Default | Notes |
| :--- | :--- | :--- |
| Path protection | `Restricted` | Blocks known system, credential, IDE, build, and session metadata locations. |
| Connector host allowlist | Allow all hosts | Configure `Security:AllowedHosts` to enable strict outbound allowlisting. The infrastructure egress fence still applies. |
| Infrastructure egress fence | Enabled | Blocks cloud metadata, link-local, container-host, and cluster-service destinations unless an exact local exemption applies. Administrator-declared denied CIDRs cannot be exempted. |
| Environment variables | Allow three host facts | The shipped configuration permits `TEMP`, `USERDOMAIN`, and `PROCESSOR_ARCHITECTURE`; all other `ENV()` reads are denied. Use an empty list for deny-all. |
| File operation count | 100 per script | Configurable with `Security:MaxFileOperationsPerScript`. |
| SMTP email sends | 100 per script | Configurable with `Security:MaxSmtpEmailsPerScript`; scripts may lower or raise up to the configured ceiling with `SET MAX_SMTP_EMAILS_PER_SCRIPT = n`. |
| Recursive directory depth | 5 levels | Configurable with `Security:MaxRecursiveNestingDepth`. |
| Parallel degree ceiling | 32 | Configurable with `Security:MaxParallelDegree`; session-level overrides are validated. |
| String result ceiling | 100 MiB | Configurable with `Security:MaxStringResultSize`. |
| Regex timeout | 1000 ms | Configurable with `Security:RegexMatchTimeoutMs`. |
| Plaintext connection secrets | Blocked | `Engine:AllowPlaintextSecrets` defaults to `false`. Prefer `SECRET:name`; use `ENC:` for portable encrypted literals. |
| Disk spill encryption | Enabled | `Security:SpillEncryptionEnabled` defaults to `true`; standalone scripts can change it with `SET SPILL_ENCRYPTION`. |
| Script file writes | Blocked | `.etlsql`, `.rptsql`, `.sql`, `.py`, `.js`, `.sh`, `.bat`, `.cmd`, and `.etls` are write-blocked. |
| Dangerous file types | Blocked | Executables, libraries, installers, shell/batch files, system files, and cert/key containers such as `.pfx` / `.cer`. |
| Snapshot encryption at rest | AES-256-GCM | `.etlsnap` report packages are compressed and encrypted at rest with versioned key rotation. |

These defaults are guardrails, not a replacement for least-privilege deployment. Production services should run under dedicated OS accounts with restricted filesystem, database, network, and SMTP permissions.

---

## 3. Enforcement Points

Most script-level filesystem checks flow through `SecurityService`:

- `ValidatePath()` normalizes paths, resolves symlinks, applies path protection mode, blocks protected directories, blocks drive/root paths, and protects session metadata.
- `ValidateFileType()` blocks dangerous extensions and denies unknown extensions unless a session override permits them.
- `ValidateWriteAccess()` blocks writes to script and source-code file types.
- `CheckRunawayProtection()` enforces operation-count and recursion-depth limits.
- `ValidateHost()` applies network egress allowlist checks.
- `ValidateEnvVar()` applies environment-variable allowlist checks.
- `ValidateStringSize()` applies large string-result controls.
- `ValidateThresholdOverride()` validates session-level threshold increases.

Engine and connector code must call `IExecutionContext.ResolvePath()` before local filesystem access. That is the boundary that routes paths through the security service. Bypassing it in connector or handler code is a security bug.

---

## 4. Filesystem Sandbox

### 4.1 Path Protection Modes

`Security:PathProtectionMode` supports three modes:

| Mode | Behavior |
| :--- | :--- |
| `Restricted` | Default. Blocks known system, credential, build, IDE, and session metadata paths. Allows ordinary data paths outside blocked areas. |
| `Defined` | Denies all filesystem access unless the resolved path is inside an approved safe zone. |
| `Unrestricted` | Disables path and extension validation. Use only in isolated development or disposable environments. |

### 4.2 Restricted Path Blocks

In `Restricted` mode, the engine blocks paths containing protected segments such as:

| Category | Examples |
| :--- | :--- |
| OS core | `Windows`, `System32`, `SysWOW64`, `etc`, `/bin`, `/sbin`, `/root`, `/usr`, `/var` |
| Credentials and identity | `.git`, `.ssh`, `.aws`, `.azure`, `.kube`, `.gnupg`, `.config`, `Users/Public` |
| IDE and build state | `.vscode`, `.idea`, `node_modules`, `bin`, `obj` |
| Windows system locations | `Program Files`, `Program Files (x86)`, `ProgramData`, `AppData`, `Documents and Settings`, `Config.msi`, `System Volume Information` |
| Linux system locations | `/boot`, `/dev`, `/lib`, `/lib32`, `/lib64`, `/libx32`, `/lost+found`, `/media`, `/mnt`, `/run`, `/srv`, `/sys` |

Access to a drive root or filesystem root, such as `C:\` or `/`, is blocked in `Restricted` mode.

Session metadata is also protected. Scripts cannot directly access `.etlsession` files, `.recovery.json` manifests, or direct `_temp` session storage paths unless the engine is performing an internal operation.

### 4.3 Approved Safe Zones

Administrators can configure `Security:ApprovedSafeZones` to identify directories where certain guarded operations are allowed.

Safe zones matter in two ways:

- In `Defined` mode, filesystem access is allowed only inside safe zones.
- In `Restricted` mode, a safe zone can authorize access that would otherwise hit standard restricted-directory checks.

If a safe-zone path contains sensitive path segments, the engine logs a warning when that access is authorized. Safe zones should therefore be narrow and data-specific. Do not safe-zone broad user profiles, repo roots, system directories, or cloud credential directories.

Important implementation note: `IsSystemPath()` exists and is used by some code paths and display surfaces, but configured safe zones are still an administrative trust boundary. Review safe-zone configuration directly.

### 4.4 File Type Controls

The engine blocks dangerous file types regardless of override flags:

```text
.dll .exe .bat .cmd .sh .msi .sys .com .pfx .cer
```

The data-file allowlist includes:

```text
.csv .json .parquet .avro .db .enc .pgp .asc .gpg .key .gz .7z
.txt .sql .log .xlsx .xml .yaml .yml .ini .md .zip .dat .tsv .psv .fixed
.etlds .etlsnap
```

Unknown extensions are blocked by default. A session can permit an unknown extension with:

```sql
SET ALLOW_FILE_TYPE_ACCESS = '.ext';
```

### 4.5 Script Immutability

Scripts cannot create, overwrite, move, or rename application logic files with these extensions:

```text
.etlsql .rptsql .sql .etls .py .js .sh .bat .cmd
```

This protects the control plane from script-driven self-modification. The engine's file-type allowlist
still permits reading `.sql` for linting and analysis, so write blocking must not be described as a
general read sandbox. Repository authoring policy is stricter: agents must not generate ETL-SQL that
reads `.sql`, `.etlsql`, or `.rptsql` files. `RUN SCRIPT`, report publication, and other host-owned
script-loading paths are separate, purpose-built operations.

---

## 5. Network and Environment Controls

### 5.1 Network Egress

By default, `AllowedHosts` contains `*`, so the configurable connector allowlist permits any host.
This is convenient for local development but permissive for production. It does not disable the
infrastructure egress fence.

To enforce egress allowlisting, configure a non-empty `Security:AllowedHosts` list. Once set, only exact host matches and leading-wildcard domains are allowed:

```json
{
  "Security": {
    "AllowedHosts": [
      "api.github.com",
      "*.microsoft.com",
      "sql-prod.internal.corp"
    ]
  }
}
```

`localhost`, `127.0.0.1`, and `::1` are always allowed by the configurable host allowlist for local
tooling and report hosting. Do not treat `AllowedHosts` as a loopback-denial control; use process and
network isolation if scripts must not reach local services.

Independently of `AllowedHosts`, the infrastructure egress fence blocks cloud metadata endpoints,
link-local node services, container-host aliases, and cluster-service discovery names. It is applied
at connection creation, on dynamic HTTP targets and redirects, and at socket connect after DNS
resolution. Exact entries in `Security:EgressFenceExemptions` can exempt built-in destinations.
`Security:DeniedEgressRanges` adds deployment-specific CIDRs with no exemption path. See
[Security Configuration](docs/administration/platform/config/security-configuration.md).

### 5.2 Environment Variables

`ENV('NAME')` is allowlist-only. A bare `SecurityService` starts with an empty list, while the shipped
`src/appsettings.json` permits `TEMP`, `USERDOMAIN`, and `PROCESSOR_ARCHITECTURE`. Configure an
explicit list for the deployed host:

```json
{
  "Security": {
    "AllowedEnvVars": ["APP_ENV", "BUILD_NUMBER", "DEPLOY_TARGET"]
  }
}
```

Use `*` only in trusted single-user environments. In shared environments, broad environment access can expose host secrets.

---

## 6. Resource Governance

The engine enforces configurable ceilings for operations that can destabilize a host:

| Control | Default | Session override |
| :--- | :--- | :--- |
| File operation count | 100 | `SET ALLOW_FILE_OPERATIONS = n` |
| Recursive directory depth | 5 | `SET ALLOW_RECURSIVE_LAYERS = n` |
| Parallel execution degree | 32 | `SET MAX_PARALLEL_DEGREE = n` |
| String result size | 100 MiB | `SET MAX_STRING_RESULT_SIZE = n` |
| Regex timeout | 1000 ms | `SET REGEX_MATCH_TIMEOUT = n` |

Overrides that increase risk are validated against safe-zone context where applicable and produce warning/audit entries. They should be treated as intentional exceptions, not normal script setup. `SET ALLOW_LARGE_STRING_RESULTS ON` is separate from the byte ceiling: it permits guarded oversized string results only when the current script/path is inside an approved safe zone.

Spill encryption and compression are enabled by default. In a standalone execution, script-level
`SET SPILL_ENCRYPTION OFF` and `SET SPILL_COMPRESSION OFF` can weaken those defaults. Enrolled
policy can bound spill volume, but it does not currently make the encryption/compression toggles
non-bypassable. Governed deployments must therefore reject those statements through script review
and admission policy when encrypted spill is mandatory. The worker scratch volume still needs
OS-level access control and lifecycle cleanup.

`SET WHAT_IF ON` provides dry-run behavior for many side-effecting statements, including DML, file operations, email, Docker, and remote execution paths that explicitly check `IsWhatIf`. It is a safety preview, not a full transaction simulator.

### 6.1 Enterprise Policy Enforcement

Standalone (unenrolled) installations use only local configuration and their built-in guardrails. Under **enterprise enrollment**, an authoritative, signed organization policy can enforce these ceilings — parallelism, file/recursion operations, spill volume, SMTP sends, and maximum materialized string bytes — across the fleet. Verified policy values take final configuration precedence over `appsettings.json`, environment variables, and command-line configuration, and operation-boundary checks prevent scripts from weakening governed ceilings. Enrollment is stored outside ordinary configuration so lower-authority sources cannot disable it, and fail-closed enrollment stops startup or execution when policy is missing, tampered, or expired. See [Authoritative organization policy](docs/administration/platform/organization-policy.md) and [Enterprise machine enrollment](docs/administration/platform/enterprise-enrollment.md).

---

## 7. Credential and Secret Handling

### 7.1 `ENC:` Values

ETL-SQL supports encrypted string values with the `ENC:` prefix. These are decrypted using the active session password set by:

```sql
USE PASSWORD = 'myMasterSecret';
```

New `ENC:` encryption uses the version-2 authenticated envelope:

| Property | Value |
| :--- | :--- |
| Algorithm | AES-256-GCM |
| Key derivation | PBKDF2-SHA256 |
| Iterations | 600,000 |
| Salt | 16 random bytes per operation |
| Nonce | 12 random bytes per operation |
| Authentication tag | 16 bytes |

The same plaintext encrypted twice produces different ciphertext because the salt and nonce are random.

The decryptor retains read compatibility for legacy version-1 AES-CBC `ENC:` payloads. Re-encrypt
legacy values to emit the authenticated v2 format. Password-based file encryption is a separate
versioned format: current files use AES-256-CBC with independent encryption/HMAC keys and an
authenticate-before-decrypt HMAC-SHA256 tag; legacy unauthenticated files remain readable for
compatibility.

### 7.2 Named Secret References

For shared and production scripts, prefer `SECRET:name` references over embedded values or `ENC:` literals. A named secret is resolved at execution time through the configured secret provider and is masked in diagnostics, logs, metadata displays, support bundles, and audit payloads.

Supported secret-provider paths include:

- `Environment` — read-only resolution from process environment variables.
- `OsSecretStore` — machine-scoped protected local storage for single-node and SME deployments.
- Portal encrypted store — Portal database-backed secret records protected with the Portal key material and governed by Portal RBAC/audit.
- `HttpsVault` — optional integration for organizations that already operate an external vault.

Canonical script form uses quoted secret references:

```sql
CREATE CONNECTION sales AS MSSQL(
  SERVER = 'sql01',
  DATABASE = 'Sales',
  USER = 'etl_worker',
  PASSWORD = 'SECRET:sales_db_password'
);
```

Secret references resolve only on credential fields by default, such as `PASSWORD`, `TOKEN`, `ACCESS_KEY`, `SECRET_KEY`, and similar connector options. Organization policy or catalog metadata can designate additional sensitive connection fields, such as `HOST`, `SERVER`, `DATABASE`, `BUCKET`, or `PATH`, as maskable and `SECRET:`-resolvable. Designating metadata as sensitive controls resolution and display masking; it does not automatically require every non-credential value to be stored as a secret.

Missing, disabled, or unavailable secret providers fail closed for the operation that requires the secret. Resolved secret values must not be written back into scripts, manifests, exports, diagnostics, or user-visible APIs.

### 7.3 Shared Connections and Sensitive Metadata

The Connection Catalog lets administrators define shared connection metadata once and expose it to scripts through approved aliases. Catalog entries store credential-bearing values as `SECRET:name` references, never resolved secret material. Local catalog entries are machine-scoped; Portal catalog entries are governed by Portal RBAC, audit, environment/tenant scope, and optional sensitive-field classification.

Catalog expansion happens at execution time under the current caller or service identity. After expansion, the connection still passes the normal connector, host allowlist, filesystem, policy, and audit checks. Audit and lineage records should include the alias, connector type, decision, and masked metadata, not resolved credentials.

### 7.4 Machine-Bound Protection

ETL-SQL has machine-bound protection utilities used by session and dataset-related storage paths.

There are three relevant implementations:

- `MachineBoundCrypto`: on Windows uses DPAPI `LocalMachine`; on Linux/macOS derives an AES-256 key from `/etc/machine-id` or hostname fallback.
- `CryptoUtils.Protect()`: on Windows uses DPAPI `CurrentUser`; on non-Windows stores or creates a random key under the user local application data directory (`etl-sql/machine.key`) and can mix optional entropy.
- `CryptoUtils.ProtectMachine()`: on Windows uses DPAPI `LocalMachine`; on non-Windows uses the `MACHINE:` AES-256-GCM protection path. It is used for administrator-written, service-read machine-scoped secrets and local shared connection catalog entries.

Security implication: machine-bound data is intended to stay local to the deployment context, but the exact portability boundary depends on which utility wrote it, OS behavior, service account, and host configuration.

### 7.5 File Encryption

ETL-SQL supports:

- Password-based file encryption using AES-256-CBC, PBKDF2-derived encryption/HMAC keys, and HMAC-SHA256 authentication for current-format files.
- SSH/RSA-wrapped AES file encryption for key-pair workflows.
- PGP encryption and decryption for partner/file-exchange workflows.

All key files and data files still need to pass path validation before script-level access.

### 7.6 Masking, Redaction, and Leak Prevention

To protect credentials from accidental exposure in logs, trace files, exception dumps, operator consoles, and audit trails, ETL-SQL applies system-wide sanitization via the `SecretRedactor` utility. 

Key features include:
- **Last-Mile Redaction**: Strips raw secrets, API keys, tokens, Bearer authorization headers, and encrypted values (prefixed with `ENC:`, `DPAPI:`, `MACHINE:`, or `SECRET:`) and replaces them with a uniform mask (`********`).
- **Exception Redaction**: Catches and wraps unhandled exceptions in a `RedactedException`, sanitizing credentials and sensitive values from both exception messages and raw stack traces before logging or displaying them.
- **Diagnostics Masking**: Automatically masks known connection credential fields in connection string parsing, database options, and metadata diagnostics.
- **Linter Warning Gates**: Checks script composition at compile time, warning on common leak patterns (e.g. referencing sensitive variables directly inside `PRINT` or `SEND EMAIL` bodies).

While ETL-SQL significantly reduces the risk of accidental secret leaks, a script author can still bypass these checks (e.g. by encoding secrets or obfuscating text). Security-sensitive deployments should combine these engine controls with repository access controls, script review, and least-privilege service account configurations.

---

## 8. Reporting and Portal Security

Report-SQL and the Portal add a web surface around script execution and report viewing.

Controls include:

- `ReportPlayer` binds to `127.0.0.1`; port selection is configurable with `ReportPlayer:Port`.
- `SnapshotStore` uses atomic write behavior and path-based async locks to reduce snapshot corruption during refreshes.
- Portal records report publish/update/delete, folder permission, subscription, saved view, share link, embed token, dataset, and admin actions in portal audit logs.
- Portal publish and update flows validate script paths against configured script roots.
- Production startup validates JWT, dataset-at-rest, OIDC, and HA key-ring configuration and fails closed on invalid required material.
- Security stamps and refresh-token revocation invalidate sessions after account, role, group, ACL, password, or directory-mapping changes.
- Local accounts enforce first-login password change, account lockout, and password hashing through ASP.NET Core Identity.
- Folder, report, dataset, Studio, and administrative permissions are enforced server-side. UI visibility is not an authorization boundary.
- Browser responses carry CSP with per-response script nonces, `nosniff`, referrer and permissions policies, and controlled `frame-ancestors` configuration.
- Authentication, anonymous-token, Designer, and metrics endpoints have configurable fixed-window rate limits.

### 8.1 Snapshot Packaging & At-Rest Encryption
Report snapshots (`.etlsnap`) are packaged as compressed ZIP streams containing the report layout, metadata, and optional binary data tables.
- **AES-256-GCM Encryption**: The compiled package is encrypted at rest using AES-256-GCM (Authenticated Encryption with Associated Data).
- **Key Derivation & Rotation**: Cryptographic keys are derived from the configured `Portal:Dataset:AtRestKey` and mixed with versioning headers. The `SnapshotPackageService` supports versioned key rotation, resolving legacy keys from a configured dictionary (`Portal:Dataset:PreviousAtRestKeys`) to decrypt older snapshots.

Production portals must configure `Portal:Dataset:AtRestKey` as a base64 value that decodes to at least 32 bytes, along with a non-secret `Portal:Dataset:AtRestKeyVersion`. Startup fails closed when the current key, previous-key map, or legacy-key version is missing, invalid, too short, duplicated, or internally inconsistent. The only supported exception is `Portal:Dataset:AllowMachineFallback=true`, which is for deliberate development or standalone use and creates host-bound caches that cannot be restored on another machine.

Backups must preserve the Portal database, Orchestrator database, `Portal:ScriptRootPath`, `Portal:SnapshotDirectory`, `Portal:DatasetRootPath`, Data Protection key ring, JWT secret, dataset at-rest key and versions, and Orchestrator API key as one coordinated set. Restoring dataset files or database rows without the matching key material makes cached datasets and snapshots unreadable.

### 8.2 Identity and Authentication

The Portal supports local accounts and federated OpenID Connect (OIDC). LDAP verification can be
enabled alongside the selected primary provider:

- **Token Validation**: Strictly validates OIDC signatures, issuer authority, and token audience.
- **Group Claim Synchronization**: Maps configured OIDC group claims to portal groups for folder, report, and dataset authorization. Membership is reconciled on login and token/session renewal according to the configured identity-provider and Portal JWT/refresh-token lifetimes; revocation is not an instantaneous push from the identity provider.
- **LDAP Lifecycle Boundary**: LDAP membership synchronization occurs at login. The Portal does not
  continuously poll the directory for disabled or removed accounts; offboarding must also disable
  the Portal account to revoke its sessions promptly.
- **Service Boundary**: The Orchestrator API key authenticates the Portal service. Human or service
  caller identity is carried separately in a short-lived signed assertion, and management routes
  require both where configured.

ETL-SQL delegates MFA, conditional access, device trust, and risk-based authentication to the configured identity provider. The Portal validates the resulting token and bridges it into its own JWT/refresh-token session. Keep Portal token lifetimes aligned with the organization's identity-provider reauthentication policy.

### 8.3 Payload and Memory Boundaries (Apache Arrow)

ETL-SQL uses the Apache Arrow columnar format to keep large payloads out of the initial browser manifest:

- **On-Demand Lazy Loading**: Visuals with 10,000 or more rows are serialized as binary Apache Arrow IPC streams inside the encrypted snapshot. The web dashboard initially loads a lightweight manifest and requests the row payload on demand. This reduces initial server/client memory pressure; it is not an authorization, download-prevention, or exfiltration control.
- **Engine Temp Table Spilling**: To protect the host from memory exhaustion, active `#temp` tables that exceed memory ceilings are automatically spilled to the host filesystem as columnar Apache Arrow IPC packages, subject to path protection rules.

### 8.4 Native Vector Rendering

Server-side chart generation uses a managed C# Grammar of Graphics pipeline (`PlotPlan` $\to$ SVG
vector output). It does not embed V8, ClearScript, or a headless browser for server-side chart
rendering. This narrows that rendering attack surface; it does not eliminate RCE, SSRF, browser, or
connector risk elsewhere in the product.

Generated SVG is limited to vector geometry and escaped text. The renderer does not emit inline
`<script>` elements, external resource references, or HTML event handlers. The browser report runtime
still executes the shipped ETL-SQL JavaScript bundle and must remain protected by normal web controls.

Operational cautions:

- Treat `.rptsql` files as executable scripts, not passive dashboard definitions.
- Restrict who can publish, update, or execute report scripts.
- Do not expose ReportPlayer directly to untrusted networks without an authenticated reverse proxy or the Portal.
- Review share-link and embed-token lifetimes and revocation behavior before enabling external access.

### 8.5 Deployment and Tenant-Isolation Boundaries

Security claims are profile- and topology-specific:

- Solo and Team deployments rely on local process, OS account, filesystem, database, and network boundaries.
- Enterprise adds signed organization policy, provider-backed shared state, remote audit delivery, enrollment, and HA certification gates.
- Managed Dedicated SaaS uses disjoint per-tenant deployment boundaries and passed its v0.18.0 profile lane.
- Shared SaaS uses tenant-aware shared control planes with hardened per-run execution. Its hostile-isolation profile lane passed and is marked release-eligible in the v0.18.0 certification ledger.

Certification remains lane-specific. The v0.18.0 Enterprise-to-SaaS, Solo-to-SaaS, SaaS exit, and
upgrade transition lanes name Managed Dedicated explicitly; the passing Shared SaaS profile lane does
not silently broaden those transition claims.

The Portal's environment/tenant identity must come from host routing and signed authority, not a
caller-selectable request value. Operators must isolate every environment's databases, artifact
roots, secrets, key rings, service keys, enrollment state, and security-event outbox. See
[SaaS Tenant Isolation](docs/architecture/saas-tenant-isolation.md) and
[Deployment Profile Certification](docs/administration/platform/deployment-profile-certification.md).

---

## 9. Audit and Observability

ETL-SQL produces security-relevant logs and tables, but these should not be described as immutable audit logs unless your deployment stores them in an append-only or externally protected log system.

Engine-level visibility includes:

- `SecurityException` messages for denied path, file type, write, host, environment-variable, and resource-threshold access.
- Warning logs for authorized sensitive safe-zone access and risky threshold overrides.
- Messages when `SET ALLOW_...` security overrides are used.
- `SHOW SAFE ZONES` for active safe-zone visibility.
- `SHOW JOB HISTORY` for job execution history, including rows processed, status, peak RAM, CPU time, script hash metadata where available, and error messages.

Portal-level visibility includes:

- Database-backed audit records for admin, report, folder, dataset, subscription, sharing, embedding, saved view, and alert actions.
- CSV export for portal audit review, including per-row correlation ids (HTTP request trace identifier or background operation id).

Portal audit guarantees and boundaries:

- **Transactional Audit Integrity**: Security-sensitive portal mutations (user/role/password/token lifecycle, ownership transfer, group membership, folder and dataset ACLs, SMTP definitions, capability revocations, subscription delivery outcomes) use transactional outbox patterns. The audit row commits in the same database transaction as the mutation—ensuring that a security policy modification cannot succeed without its corresponding audit record being durably written.
- Audit retention is opt-in (`Portal:Audit:RetentionDays`; default keeps rows forever).
- Local/shared audit tables in the application database, whether SQLite or PostgreSQL, are mutable database records and are **not** tamper-proof by themselves. The supported enterprise posture is scheduled export/forwarding to external append-only storage; in-database tamper-evident hash chaining is an explicit non-goal for this release.

**Log hygiene (portal-wide rule):** credential material must never reach log output, persisted failure detail, or audit records — including when a downstream error echoes a secret back (e.g. an SMTP server returning the password in an authentication error). Credential-bearing error paths sanitize the secret out before it is logged, persisted, or audited; the subscription delivery executor is the canonical case and is enforced by an automated test that drives a failure whose error text contains the SMTP password and asserts it appears in neither the captured logs, the returned reason, the delivery ledger detail, nor the audit row. Operational metrics (`GET /api/admin/metrics/operational`) expose active/queued executions, recent execution/delivery failure rates, and dataset/snapshot disk usage without exposing any secret.

Recommended production posture: forward engine and portal logs to an external log sink with retention, access controls, and tamper-resistant storage.

### 9.1 Security-Event Delivery and Remote Audit Outbox

Beyond local logs, ETL-SQL emits a dedicated, versioned **security-event** stream (policy denials and boundary violations, carrying correlation/job/script/policy/tenant/node identifiers) to a configured SIEM collector over HTTPS, backed by a durable local outbox and deduplicated by event ID. Separately, Portal audit rows are written with a durable outbox row **in the same database transaction** as each protected mutation and can be forwarded to a remote collector under `Portal:Audit:*`. Both paths support **fail-closed** thresholds: when an enrolled organization's signed policy configures security-event thresholds, or when `Portal:Audit:RequireRemoteDelivery` is active (which turns on automatically for an enrolled deployment with a collector configured), security-sensitive mutations are blocked once remote delivery is judged unavailable rather than proceeding un-audited. A denial is always decided and enforced before reporting — an unavailable sink cannot turn a denial into an allow. See [Central security events and SIEM delivery](docs/administration/platform/security-events.md) and [Durable audit outbox and remote collectors](docs/administration/platform/audit-outbox.md).

---

## 10. Test and Development Mode

`SecurityService` has an `IsTestMode` flag used by tests and some development execution contexts. In test mode, paths under the application base directory, current directory, and temp directory are treated as safe-zone-like locations for development and CI practicality.

Automatic detection is limited to .NET test-host processes (`testhost`, `vstest.console`, xUnit, or
Microsoft Test Platform assemblies); ordinary `dotnet run` does not enable it. Tests and isolated
contexts can still set the flag explicitly. Do not use test-host behavior as proof of production
hardening. Validate the deployed host process with explicit production configuration.

Even in test mode, dangerous file extensions and script immutability checks still apply, but path access is more permissive for test directories and temp directories.

`IsInternalOperation` exists for engine-owned operations such as session metadata and snapshot management. It must not be exposed to script context or user-controlled API inputs.

---

## 11. AI Agent Rules

AI agents working in this repository or generating ETL-SQL scripts must follow these rules:

- Never generate scripts that print, email, concatenate, or log passwords, API keys, tokens, connection strings, or `ENC:` values.
- Never write scripts that mutate `.etlsql`, `.rptsql`, `.sql`, `.py`, `.js`, shell, batch, or executable files.
- Use absolute, intentional data paths and expect them to pass `ResolvePath()`.
- Use `SET WHAT_IF ON` before examples containing destructive DML or destructive file operations.
- Prefer staging cross-source data in `#temp` tables so filtering, lineage, masking, and validation happen in engine context.
- Do not rely on `Unrestricted` path mode, wildcard `AllowedHosts`, or wildcard `AllowedEnvVars` for production examples.
- When editing connector or handler code, route every local filesystem path through `IExecutionContext.ResolvePath()`.

---

## 12. Known Limitations and Residual Risks

| Risk | Severity | Current mitigation |
| :--- | :--- | :--- |
| Safe zones are administrative trust boundaries. A broad safe zone can authorize risky access. | High | Keep safe zones narrow and data-specific; inspect with `SHOW SAFE ZONES`. |
| Network egress is allow-all by default. | Medium | Configure `Security:AllowedHosts` in production. |
| The connector host allowlist always permits loopback. | Medium | Isolate local control-plane services from script processes; do not rely on `AllowedHosts` to deny loopback. |
| SaaS evidence is topology- and lane-specific. A passing profile lane does not certify unnamed transition, upgrade, or HA paths. | Medium | Check the release claims ledger and linked evidence bundle for the exact topology and operation being claimed. |
| Audit logs are not inherently immutable. | Medium | Forward logs to protected external storage. |
| `SET ALLOW_...` overrides are script-controlled once safe-zone conditions are met. | Medium | Limit safe zones and review scripts before scheduling. |
| Scripts can disable spill encryption and compression; signed policy currently bounds spill volume but not these toggles. | Medium | Reject the disabling statements through governed script review/admission when encryption is mandatory, and protect worker scratch storage at the OS/container layer. |
| SMTP abuse can still occur within the configured send limit. | Medium | Keep `Security:MaxSmtpEmailsPerScript` conservative, restrict SMTP credentials, monitor send volume, and use provider-side throttles. |
| Secrets can still be intentionally written into output by a script author. | High | Use linting, review, least-privilege accounts, and restricted output sinks. |
| The engine permits `.sql` reads for lint/analysis even though writes are blocked. | Medium | Follow the stricter repository authoring rule; expose script content only through purpose-built, authorized host operations. |
| The standalone security-event outbox path is machine-wide by default. | High in co-located environments | Set `ETLSQL_SECURITY_EVENT_OUTBOX_PATH` to a distinct persistent path per deployment or environment. |
| Development/test mode can be more permissive than production. | Medium | Verify production behavior in the real host process and configuration. |
| `SHOW_SECRETS` can unmask sensitive variables for an authorized session. | Medium | Restrict access to interactive sessions and logs. |
| Host compromise defeats process-level controls. | High | Use OS hardening, service accounts, patching, endpoint controls, and secret rotation. |

---

## 13. Supply-Chain and Dependency Security

Every CI run gates on known-vulnerable third-party packages; a finding fails the build.

- **NuGet:** `scripts/Test-VulnerablePackages.ps1` runs `dotnet list package --vulnerable --include-transitive` across the solution via the shared helpers in `scripts/lib/DependencyAudit.ps1` (solution-level audit with a per-project fallback for the .NET 10.0.300 SDK + CPM bug). It fails on any vulnerable package — direct or transitive — and also fails when no authoritative audit could run, so an unknown dependency posture is never certified silently.
- **npm (VS Code extension):** `npm audit` runs in `src/etl-sql-vscode` and `src/etl-sql-vscode/ui` and fails on any reported vulnerability.
- The pre-release gate (`scripts/Test-PreRelease.ps1`) additionally blocks on non-Legacy deprecated packages and reports outdated ones.
- Dependabot monitors NuGet, npm, and GitHub Actions dependencies. CodeQL analyzes C# and JavaScript/TypeScript on `main` pushes and on its scheduled workflow.
- The pre-release flow scans for committed secrets, generates a CycloneDX SBOM, checks third-party inventory drift, and publishes SHA-256 checksums. Release automation also creates keyless build-provenance attestations for published artifacts.

Reproduce locally with `dotnet restore ETL-SQL.slnx` followed by `./scripts/Test-VulnerablePackages.ps1`, and `npm audit` in the two extension roots.

### 13.1 Response Procedure When the Gate Blocks a Build

1. Read the advisory URL printed in the failure. Identify the affected package, the fixed version, and whether the reference is direct or transitive (the finding is labeled `top-level` or `transitive`).
2. **Direct NuGet package:** update its version in `Directory.Packages.props` (central package management) to a fixed release.
3. **Transitive NuGet package:** prefer updating the direct parent to a release that no longer pulls the vulnerable version; otherwise pin the transitive package centrally with a `PackageVersion` entry in `Directory.Packages.props` at a fixed version.
4. **npm package:** run `npm audit fix` in the affected root, or update the offending dependency in `package.json`; commit the updated `package-lock.json`.
5. After any dependency change, regenerate the inventory (`node scripts/generate-third-party-inventory.js`) and review `THIRD-PARTY-NOTICES.md` so license compliance moves with the version change.
6. Re-run the gates locally and commit the dependency, lockfile, and inventory updates together.
7. **No fixed release exists:** the gate has no suppression list by design. Either replace or remove the dependency, or make an explicit, reviewed risk-acceptance decision — record the advisory ID, affected component, exploitability assessment, and a re-check date in `TODO.md`, and adjust the gate deliberately in that same change. Never leave the gate red or bypass it silently.

---

## 14. Security Contact

To report a security vulnerability in ETL-SQL, please use one of these **private** channels — do **not** open a public issue or include vulnerability details in public discussions:

- **GitHub private vulnerability reporting:** open a private report from the repository's **Security → Report a vulnerability** tab (GitHub Security Advisories).
- **Email:** [etlsqlsoftware@gmail.com](mailto:etlsqlsoftware@gmail.com).

Include, where possible:

- The affected release, component, deployment profile, and operating system.
- A minimal reproduction or proof of concept with sensitive values removed.
- The expected and observed behavior, prerequisites, and impact.
- Whether the issue is already public or has a disclosure deadline.
- A safe way to contact you for follow-up.

Do not include production credentials, customer data, private keys, or access tokens. Do not test
against systems or data you do not own or have explicit permission to assess. The maintainers will
acknowledge receipt, validate scope and severity, coordinate a fix and release when needed, and agree
on disclosure timing before publishing an advisory. Exact response and remediation times depend on
severity, reproducibility, and release impact; this policy does not promise a fixed SLA.

Security questions without a suspected vulnerability can use GitHub Discussions or the contact
email. Public issues are appropriate after coordinated disclosure or when they contain no sensitive
security detail.

---

**Policy Version**: 0.19.0
**Last Review Date**: 2026-08-29
**Reference Standards**: NIST SP 800-132 for PBKDF2 parameter guidance, OWASP secure logging principles, and least-privilege service deployment practices.
