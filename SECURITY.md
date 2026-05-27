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

### 7.4 Masking and Leak Prevention

The engine masks known credential fields in connection metadata and diagnostics, including common password/token option names and `ENC:` values. The linter also warns on common credential leak patterns in `PRINT`, `SEND EMAIL`, and related output paths.

ETL-SQL reduces common accidental secret exposure, but a script author can intentionally place secrets in string literals, email bodies, report text, filenames, or generated rows. Security-sensitive deployments should combine ETL-SQL controls with code review, linting, limited service-account permissions, and log retention rules.

---

## 8. Reporting and Portal Security

Report-SQL and the Report Portal add a web surface around script execution and report viewing.

Controls include:

- `ReportPlayer` serves local dashboards and defaults to local hosting; port selection is configurable with `ReportPlayer:Port`.
- `SnapshotStore` uses atomic write behavior and path-based async locks to reduce snapshot corruption during refreshes.
- Report Portal records report publish/update/delete, folder permission, subscription, saved view, share link, embed token, dataset, and admin actions in portal audit logs.
- Portal publish and update flows validate script paths against configured script roots.
- JWT secrets must be configured with sufficient length before production use.
- Folder, report, and dataset permissions are enforced in portal controllers.

Operational cautions:

- Treat `.rptsql` files as executable scripts, not passive dashboard definitions.
- Restrict who can publish, update, or execute report scripts.
- Do not expose ReportPlayer directly to untrusted networks without an authenticated reverse proxy or the Report Portal.
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
- CSV export for portal audit review.

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

## 13. Security Contact

To report a security vulnerability in ETL-SQL, open a confidential issue or contact the project maintainer directly. Do not post vulnerability details in public issues.

---

**Policy Version**: 0.9.0
**Last Review Date**: 2026-05-18
**Reference Standards**: NIST SP 800-132 for PBKDF2 parameter guidance, OWASP secure logging principles, and least-privilege service deployment practices.
