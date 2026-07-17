# ETL-SQL Security Model

ETL-SQL treats scripts as powerful automation inputs. A script can move data, call connectors, write files, send email, run scheduled jobs, and publish reports. That is useful, but it is also inherently risky.

This document describes the controls ETL-SQL provides, where they are enforced, what defaults are used, and what risks remain. It is intended for security reviewers, administrators, and AI agents generating or modifying ETL-SQL scripts.

ETL-SQL does not claim that a script sandbox eliminates all risk. The goal is narrower and more practical: reduce accidental damage, block common host escape paths, make dangerous actions visible, and give administrators clear places to harden deployments.

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
| Network egress | Allow all hosts | Configure `Security:AllowedHosts` to enable strict outbound allowlisting. |
| Environment variables | Deny all | Configure `Security:AllowedEnvVars` to permit specific `ENV()` reads. |
| File operation count | 100 per script | Configurable with `Security:MaxFileOperationsPerScript`. |
| SMTP email sends | 100 per script | Configurable with `Security:MaxSmtpEmailsPerScript`; scripts may lower or raise up to the configured ceiling with `SET MAX_SMTP_EMAILS_PER_SCRIPT = n`. |
| Recursive directory depth | 5 levels | Configurable with `Security:MaxRecursiveNestingDepth`. |
| Parallel degree ceiling | 32 | Configurable with `Security:MaxParallelDegree`; session-level overrides are validated. |
| String result ceiling | 100 MiB | Configurable with `Security:MaxStringResultSize`. |
| Regex timeout | 1000 ms | Configurable with `Security:RegexMatchTimeoutMs`. |
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

This protects the control plane from script-driven self-modification. Reading `.sql` files is allowed because linting and analysis scenarios need it; writing `.sql` files is blocked.

---

## 5. Network and Environment Controls

### 5.1 Network Egress

By default, `AllowedHosts` contains `*`, so network connectors can connect to any host. This is convenient for local development but permissive for production.

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

`localhost`, `127.0.0.1`, and `::1` are always allowed for local tooling and report hosting.

### 5.2 Environment Variables

`ENV('NAME')` is deny-by-default. Configure `Security:AllowedEnvVars` to allow specific variables:

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
| String result size | 100 MiB | `SET ALLOW_LARGE_STRING_RESULTS ON` for guarded large results |
| Regex timeout | 1000 ms | `SET REGEX_MATCH_TIMEOUT = n` |

Overrides that increase risk are validated against safe-zone context where applicable and produce warning/audit entries. They should be treated as intentional exceptions, not normal script setup.

`SET WHAT_IF ON` provides dry-run behavior for many side-effecting statements, including DML, file operations, email, Docker, and remote execution paths that explicitly check `IsWhatIf`. It is a safety preview, not a full transaction simulator.

---

## 7. Credential and Secret Handling

### 7.1 `ENC:` Values

ETL-SQL supports encrypted string values with the `ENC:` prefix. These are decrypted using the active session password set by:

```sql
USE PASSWORD = 'myMasterSecret';
```

Current `ENC:` encryption uses:

| Property | Value |
| :--- | :--- |
| Algorithm | AES-256-CBC |
| Key derivation | PBKDF2-SHA256 |
| Iterations | 600,000 |
| Salt | 16 random bytes per operation |
| IV | Random per operation |

The same plaintext encrypted twice produces different ciphertext because the salt and IV are random.

### 7.2 Machine-Bound Protection

ETL-SQL has machine-bound protection utilities used by session and dataset-related storage paths.

There are two relevant implementations:

- `MachineBoundCrypto`: on Windows uses DPAPI `LocalMachine`; on Linux/macOS derives an AES-256 key from `/etc/machine-id` or hostname fallback.
- `CryptoUtils.Protect()`: on Windows uses DPAPI `CurrentUser`; on non-Windows stores or creates a random key under the user local application data directory (`etl-sql/machine.key`) and can mix optional entropy.

Security implication: machine-bound data is intended to stay local to the deployment context, but the exact portability boundary depends on which utility wrote it, OS behavior, service account, and host configuration.

### 7.3 File Encryption

ETL-SQL supports:

- Password-based file encryption using AES-256-CBC with PBKDF2.
- SSH/RSA-wrapped AES file encryption for key-pair workflows.
- PGP encryption and decryption for partner/file-exchange workflows.

All key files and data files still need to pass path validation before script-level access.

### 7.4 Masking, Redaction, and Leak Prevention

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

- `ReportPlayer` serves local dashboards and defaults to local hosting; port selection is configurable with `ReportPlayer:Port`.
- `SnapshotStore` uses atomic write behavior and path-based async locks to reduce snapshot corruption during refreshes.
- Portal records report publish/update/delete, folder permission, subscription, saved view, share link, embed token, dataset, and admin actions in portal audit logs.
- Portal publish and update flows validate script paths against configured script roots.
- JWT secrets must be configured with sufficient length before production use.
- Folder, report, and dataset permissions are enforced in portal controllers.

### 8.1 Snapshot Packaging & At-Rest Encryption
Report snapshots (`.etlsnap`) are packaged as compressed ZIP streams containing the report layout, metadata, and optional binary data tables.
- **AES-256-GCM Encryption**: The compiled package is encrypted at rest using AES-256-GCM (Authenticated Encryption with Associated Data).
- **Key Derivation & Rotation**: Cryptographic keys are derived from the configured `Portal:Dataset:AtRestKey` and mixed with versioning headers. The `SnapshotPackageService` supports versioned key rotation, resolving legacy keys from a configured dictionary (`Portal:Dataset:PreviousAtRestKeys`) to decrypt older snapshots.

### 8.2 Identity and Authentication (OIDC)
The Portal supports federated identity via OpenID Connect (OIDC) to standardize access:
- **Token Validation**: Strictly validates OIDC signatures, issuer authority, and token audience.
- **Group Claim Synchronization**: Dynamically maps OIDC group claims to portal roles and ACL permissions (folder, report, and dataset authorization), ensuring membership revocation propagates automatically.

### 8.3 Data Minimization & Memory Safety (Apache Arrow)
ETL-SQL utilizes the Apache Arrow columnar format to govern large payloads and optimize resource safety:
- **On-Demand Lazy Loading**: Visuals with large row counts (exceeding 10,000 rows) are serialized as binary Apache Arrow IPC streams inside the encrypted snapshot. The web dashboard loads only a lightweight manifest; row segments are lazy-loaded on-demand, minimizing server and client memory overhead and reducing the risk of bulk memory extraction.
- **Engine Temp Table Spilling**: To protect the host from memory exhaustion, active `#temp` tables that exceed memory ceilings are automatically spilled to the host filesystem as columnar Apache Arrow IPC packages, subject to path protection rules.

Operational cautions:

- Treat `.rptsql` files as executable scripts, not passive dashboard definitions.
- Restrict who can publish, update, or execute report scripts.
- Do not expose ReportPlayer directly to untrusted networks without an authenticated reverse proxy or the Portal.
- Review share-link and embed-token lifetimes and revocation behavior before enabling external access.

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
- The audit table itself is mutable SQLite and is **not** tamper-proof. The supported enterprise posture is scheduled export/forwarding to external append-only storage; in-database tamper-evident hash chaining is an explicit non-goal for this release.

**Log hygiene (portal-wide rule):** credential material must never reach log output, persisted failure detail, or audit records — including when a downstream error echoes a secret back (e.g. an SMTP server returning the password in an authentication error). Credential-bearing error paths sanitize the secret out before it is logged, persisted, or audited; the subscription delivery executor is the canonical case and is enforced by an automated test that drives a failure whose error text contains the SMTP password and asserts it appears in neither the captured logs, the returned reason, the delivery ledger detail, nor the audit row. Operational metrics (`GET /api/admin/metrics/operational`) expose active/queued executions, recent execution/delivery failure rates, and dataset/snapshot disk usage without exposing any secret.

Recommended production posture: forward engine and portal logs to an external log sink with retention, access controls, and tamper-resistant storage.

---

## 10. Test and Development Mode

`SecurityService` has an `IsTestMode` flag used by tests and some development execution contexts. In test mode, paths under the application base directory, current directory, and temp directory are treated as safe-zone-like locations for development and CI practicality.

Critical caveat: test-mode detection is intentionally broad and may be active in `dotnet`-hosted development runs. Do not use development/test execution behavior as proof of production hardening. Validate production security settings in the deployed host process with explicit configuration.

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
| Audit logs are not inherently immutable. | Medium | Forward logs to protected external storage. |
| `SET ALLOW_...` overrides are script-controlled once safe-zone conditions are met. | Medium | Limit safe zones and review scripts before scheduling. |
| SMTP abuse can still occur within the configured send limit. | Medium | Keep `Security:MaxSmtpEmailsPerScript` conservative, restrict SMTP credentials, monitor send volume, and use provider-side throttles. |
| Secrets can still be intentionally written into output by a script author. | High | Use linting, review, least-privilege accounts, and restricted output sinks. |
| Development/test mode can be more permissive than production. | Medium | Verify production behavior in the real host process and configuration. |
| `SHOW_SECRETS` can unmask sensitive variables for an authorized session. | Medium | Restrict access to interactive sessions and logs. |
| Host compromise defeats process-level controls. | High | Use OS hardening, service accounts, patching, endpoint controls, and secret rotation. |

---

## 13. Dependency Vulnerability Management

Every CI run gates on known-vulnerable third-party packages; a finding fails the build.

- **NuGet:** `scripts/Test-VulnerablePackages.ps1` runs `dotnet list package --vulnerable --include-transitive` across the solution via the shared helpers in `scripts/lib/DependencyAudit.ps1` (solution-level audit with a per-project fallback for the .NET 10.0.300 SDK + CPM bug). It fails on any vulnerable package — direct or transitive — and also fails when no authoritative audit could run, so an unknown dependency posture is never certified silently.
- **npm (VS Code extension):** `npm audit` runs in `src/etl-sql-vscode` and `src/etl-sql-vscode/ui` and fails on any reported vulnerability.
- The pre-release gate (`scripts/Test-PreRelease.ps1`) additionally blocks on non-Legacy deprecated packages and reports outdated ones.

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

Where possible, include a minimal reproduction, the affected version, and an impact assessment. You will receive an acknowledgement, and any fix will be coordinated privately before public disclosure.

---

**Policy Version**: 0.15.0
**Last Review Date**: 2026-06-26
**Reference Standards**: NIST SP 800-132 for PBKDF2 parameter guidance, OWASP secure logging principles, and least-privilege service deployment practices.
