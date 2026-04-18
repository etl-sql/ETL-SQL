# ETL-SQL Enterprise Security Policy

## 1. Executive Summary
ETL-SQL is engineered with a **Security-by-Design** philosophy, prioritizing the integrity of the host environment during the execution of data transformations. The engine employs a "Zero-Trust" isolation model, ensuring that all script-level operations are strictly sandboxed and cryptographically verified.

---

## 2. Threat Model & Security Philosophy
The engine treats all user-provided scripts as **Untrusted Actors**. Our security architecture is built on four core pillars:

- **Isolation**: Prevent scripts from exiting the approved workspace or accessing sensitive system assets.
- **Resource Governance**: Prevent denial-of-service (DoS) via runaway processes or recursive resource exhaustion.
- **Transparency (Auditability)**: Maintain non-bypassable audit logs for all security threshold overrides and access blocks (SEC-3).
- **Credential Hygiene**: Ensure secrets, tokens, and passwords are never exposed in logs, diagnostics, or output streams (SEC-4).

---

## 3. Host System Isolation (The Sandbox)
The sandbox is enforced at the core evaluation layer (`SecurityService`) via `ValidatePath()`, `ValidateFileType()`, and `ValidateWriteAccess()`, intercepting all filesystem and system-level calls before execution.

### 3.1 Non-Bypassable Path Validation (Zero-Trust)
All path resolutions undergo mandatory normalization and recursive segment matching.

**Critical blocks (never bypassable, even in test mode):**

| Category | Blocked Segments |
| :--- | :--- |
| Version control | `.git` |
| Credential stores | `.ssh`, `.aws`, `.azure`, `.kube`, `.gnupg`, `.config` |
| Windows OS | `Windows`, `System32` |
| Linux OS | `etc`, `/root` |

**Standard blocks (bypassed only in test mode for authorized test paths):**

| Category | Blocked Segments |
| :--- | :--- |
| IDE / Build artifacts | `.vscode`, `.idea`, `node_modules`, `bin`, `obj` |
| Windows system directories | `SysWOW64`, `Program Files`, `Program Files (x86)`, `ProgramData`, `AppData`, `Documents and Settings`, `Config.msi`, `System Volume Information` |
| Linux system directories | `/bin`, `/boot`, `/dev`, `/lib`, `/lib32`, `/lib64`, `/libx32`, `/lost+found`, `/media`, `/mnt`, `/opt`, `/proc`, `/run`, `/sbin`, `/srv`, `/sys`, `/tmp`, `/usr`, `/var` |
| Sensitive config | `.gnupg`, `.config`, `Users/Public` |

**Root directory lockdown:** Any path resolving exactly to a drive root (e.g., `C:\` or `/`) always throws a `SecurityException` regardless of test mode or override flags.

**Session metadata protection:** Scripts cannot directly access `.etlsession` files or `.recovery.json` manifests. Direct access to `_temp` session storage folders is also blocked.

### 3.2 Immutable File Type Hardening
To prevent arbitrary code execution or system-level tampering, the following extensions are globally blacklisted and can **never** be accessed regardless of override flags:

```
.dll  .exe  .bat  .cmd  .sh  .msi  .sys  .com  .pfx  .cer
```

The following extensions are explicitly on the **allowed whitelist**:
```
.csv  .json  .parquet  .avro  .db  .enc  .gz  .7z  .txt  .sql
.log  .xlsx  .xml  .yaml  .yml  .ini  .md  .zip
```

> Unknown extensions (not on either list) are blocked by default. Use the `SET ALLOW_FILE_TYPE_ACCESS ON;` override command to allow specific unlisted extensions within an approved safe zone.

### 3.3 Script Immutability (Human-Centric Control)
To protect the integrity of the application's control plane, the engine enforces a strict write-block via `ValidateWriteAccess()`. Scripts **cannot write, move, or rename** files with these extensions:

```
.etlsql  .rptsql  .sql  .etls  .py  .js  .sh  .bat  .cmd
```

**Rationale**: Application logic is reserved exclusively for the human operator. The engine is a consumer of logic, not a producer, preventing automated "self-modifying" script attacks.

### 3.4 Resource Thresholds (Runaway Protection)
To maintain host stability, `SecurityService.CheckRunawayProtection()` enforces strict resource caps. These are configurable per-installation via `appsettings.json` (see the [Administrators Guide](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Administrators_Guide.md) for details).

| Guard | Default Limit | Configuration Key | Override Flag |
| :--- | :--- | :--- | :--- |
| Filesystem operation count | **100 per script** | `Security:MaxFileOperationsPerScript` | `SET ALLOW_GREATER_THAN_n_FILE ON/OFF` |
| Recursive directory depth | **5 levels** | `Security:MaxRecursiveNestingDepth` | `SET ALLOW_RECURSIVE_GREATER_THAN_n_LAYERS ON/OFF` |
| Parallel execution degree | **32 threads** | `Security:MaxParallelDegree` | `SET MAX_PARALLEL_DEGREE = n` |
| String result memory limit | **100 MB** | `Security:MaxStringResultSize` | `SET MAX_STRING_RESULT_SIZE = n` |
| Regex match timeout | **1000 ms** | `Security:RegexMatchTimeoutMs` | `SET REGEX_MATCH_TIMEOUT = n` |

> [!TIP]
> Error messages dynamically reflect the current configured limit, ensuring that developers know exactly which override command (e.g., `SET ALLOW_GREATER_THAN_500_FILE ON;`) to use if a limit is increased by an administrator.


### 3.5 Network Egress Control (Outbound Hardening)
The engine provides a non-bypassable guard for all network-based connectors (`MSSQL`, `POSTGRES`, `API`, `SFTP`, etc.). This prevents data exfiltration to unauthorized endpoints.

**Default Behavior**: To maintain backward compatibility and ease of development, the engine is **unrestricted (`*`)** by default. Any valid network connection is permitted.

**Hardening (Strict Mode)**: To enable strict security, provide an explicit `AllowedHosts` list in your application configuration (`appsettings.json`). Once a non-empty list is provided, the engine clears the default "allow-all" flag and only permits connections to listed targets.

**Configuration Example (`appsettings.json`):**
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

**Validation Rules:**
- **Exact Match**: Matches the host string exactly (case-insensitive).
- **Wildcards**: Supports `*` at the start of a domain (e.g., `*.google.com` matches `api.google.com` and `translate.google.com`).
- **Implicit Loopback Safety**: `localhost`, `127.0.0.1`, and `::1` (IPv6 loopback) are **always permitted**, even in strict mode. They do not need to be added to `AllowedHosts`.

### 3.6 Environment Variable Access Control
The `ENV('VAR_NAME')` function is gated by `SecurityService.ValidateEnvVar()`. This prevents scripts from reading sensitive host environment variables (API keys, credentials, etc.).

**Default Behavior**: All environment variable access is **blocked by default**. An empty `AllowedEnvVars` set means no `ENV()` calls will succeed.

**Configuration**: Administrators populate `AllowedEnvVars` at startup (via `appsettings.json`). Use `*` to allow all (not recommended in multi-tenant environments).

```json
{
  "Security": {
    "AllowedEnvVars": ["APP_ENV", "BUILD_NUMBER", "DEPLOY_TARGET"]
  }
}
```

**Rationale**: Prevents a malicious or mistaken script from exfiltrating host secrets via `ENV('AWS_SECRET_ACCESS_KEY')` or similar.

### 3.7 Safe Zone Registration Guard (`IsSystemPath`)
To prevent administrators from accidentally registering critical system directories as approved safe zones, `SecurityService.IsSystemPath()` is called before any path is added to `ApprovedSafeZones`. Drive roots, `Windows`, `System32`, `etc`, `bin`, `sbin`, `usr`, `var`, and `Boot` are all rejected as safe zone candidates.

### 3.8 Performance Observability & Resource Metrics
To facilitate monitoring and prevent stealth resource exhaustion, the engine provides high-visibility metrics for all execution sessions.

- **Periodic Emission**: The background `SchedulerService` emits a heart-beat log every 60 seconds containing the count of `ActiveJobs`, `QueuedJobs`, and `AvailableSlots`.
- **Per-Job Accountability**: Every script execution (spawned or in-process) captures and logs `PeakMemoryBytes` and `CpuTimeSeconds`.
- **Audit Visibility**: Use the `SHOW JOB HISTORY` command to inspect resource consumption for past executions. This provides an audit trail for spotting runaway scripts that may be staying within security limits but consuming excessive enterprise resources.

### 3.9 Hardening the Report Dashboard
The Report-SQL dashboard (`ReportPlayer`) exposes a live web interface that must be protected in multi-tenant environments.

- **Port Assignment**: By default, the server listens on `http://localhost:5200`. In production, this can be customized via `ReportPlayer:Port` in `appsettings.json` to avoid conflicts or to bind to specific interfaces.
- **Snapshot Integrity**: The dashboard uses a `SnapshotStore` that implements **atomic file writes** (write-to-temp-then-move) and process-level concurrency locking. This ensures that even during high-frequency refreshes, the data manifest cannot be corrupted.
- **Limited Surface Area**: The dashboard only exposes read-only visual data and slicer parameter updates. It does not provide arbitrary script execution capability to web users.


> [!IMPORTANT]
> Override commands are **only honored** when the target path resides within a verified **Approved Safe Zone** (configured in `appsettings.json`). Providing an override command while operating outside a safe zone still throws a `SecurityException`.
>
> All authorized bypasses are logged as `Warning` audits. Administrators can inspect the active safe zones via `SHOW SAFE ZONES;`.

---

## 4. Cryptographic Architecture
ETL-SQL implements a dual-layer cryptographic model to balance local security with asset portability.

### 4.1 Hardware-Bound Session Hardening (Data at Rest)
Session snapshots (`.etlsession`) and temporary cache data are protected using **hardware-locked encryption**, ensuring sensitive ephemeral data remains local to the originating system.

| Platform | Mechanism | Scope |
| :--- | :--- | :--- |
| **Windows** | OS-native **DPAPI** (`ProtectedData.Protect`, `CurrentUser` scope) | Current OS user on current machine |
| **Linux / macOS** | **Machine-locked AES-256** using a high-entropy key at `$XDG_DATA_HOME/etl-sql/machine.key` (i.e., `~/.local/share/etl-sql/machine.key`), generated on first run | Current machine only |

**Security guarantee:** Session state is strictly non-portable. It cannot be decrypted by a different OS user, on a different machine, or after OS reinstallation.

### 4.2 Portable Asset Protection (`ENC:` Prefix)
For connection strings and files intended for cross-machine portability, the engine uses a **Master Password** model managed via `USE PASSWORD = '...'`.

| Property | Value |
| :--- | :--- |
| Algorithm | AES-256 (CBC) |
| Key derivation | PBKDF2-SHA256 |
| Iterations | 600,000 (meets NIST SP 800-132 recommendation) |
| Salt | 16 bytes, cryptographically random per-operation |

**Design consequence**: The same plaintext encrypted twice produces different ciphertexts (unique salts), providing robust protection against rainbow table and known-plaintext attacks.

**Workflow:**
```sql
-- Step 1: Encrypt a script's plaintext connection strings
USE PASSWORD = 'myMasterSecret';
-- Engine calls SecurityService.EncryptScript() — replaces all plaintext
-- connection strings with ENC:... inplace.

-- Step 2: At runtime, decrypt transparently
USE PASSWORD = 'myMasterSecret';
CREATE CONNECTION db ON MSSQL('ENC:U2FsdGVkX1+...'); -- decrypted before connecting
```

**`SecurityService.NeedsEncryption()`** — can be called by the IDE or CLI to warn users that a script still contains unencrypted plaintext connection strings before saving or sharing.

> [!NOTE]
> To opt a specific connection out of script encryption, add `ENCRYPT=OFF` to its `WITH()` block. This is recorded in the `NeedsEncryption()` check and will not be flagged as a plaintext credential.

### 4.3 SSH/RSA File Encryption (Public-Key Portability)
For scenarios requiring asymmetric (key-pair) encryption — such as delivering encrypted files to partners who hold a private key — the engine supports RSA-wrapped AES encryption via SSH key files.

| Property | Value |
| :--- | :--- |
| File encryption | AES-256 (random key per operation) |
| Key transport | RSA-OAEP-SHA256 |
| Key source | SSH public/private key files (PEM format) |

The AES session key is encrypted with the recipient's RSA public key and prepended to the output file. Decryption requires the corresponding private key (passphrase-protected keys are supported). This provides the portability of the `ENC:` model without sharing a password.

### 4.4 Credential Masking
Credentials are never allowed to appear in output. The engine enforces:
- Connection strings containing passwords are masked in all `SHOW CONNECTIONS` output, diagnostics, and exception messages.
- `ENC:` values are passed as-is to the `SecurityService` for decryption and are **never** logged.
- `PASSWORD` and `TOKEN` option values in `WITH()` blocks are redacted in any serialized connection metadata.

> [!CAUTION]
> Never include raw credentials, API keys, or database passwords in `PRINT` statements or `BODY` strings in `SEND EMAIL`. These will appear in session logs. Use `ENCRYPTED` typed variables and `ENC:` strings instead.

### 4.5 Session-Bound Spill Hardening (Intermediate Data)
During high-scale query processing (large joins, aggregations, sorts), the engine spills intermediate row data to the host's temporary directory. To prevent exposure of PII or sensitive transformed data, the engine employs a non-bypassable **Secure Spill Pipeline**.

| Property | Value |
| :--- | :--- |
| **Logic** | Handled by the centralized `SpillStore` subsystem |
| **Encryption** | AES-256 (CBC) |
| **Key Lifecycle** | Random 256-bit key generated at `Evaluator` initialization; held only in memory; destroyed when the session ends. |
| **Compression** | GZip (Optimal) — applied before encryption to minimize disk footprint and I/O latency. |
| **Target Engines** | `ExternalJoinEngine`, `ExternalAggregateEngine`, `ExternalWindowEngine`, `ExternalSortEngine` |

**Security Guarantee**: Even if a malicious actor gains filesystem access to the temporary directory while a query is running, the spilled data is ciphertext and cannot be decrypted without the ephemeral session key. All spill artifacts are aggressively deleted upon session completion.

---

## 5. Audit Trail & Forensics

Every security violation or unauthorized access attempt triggers an immediate `SecurityException`, halting execution and generating a diagnostic entry in the session audit logs.

> [!IMPORTANT]
> **Audit Integrity**: The engine explicitly blocks any script-level attempt to modify session audit metadata or recovery manifests (`.etlsession`, `.recovery.json`). These files are only accessible to the engine's internal `SessionManager` via the `IsInternalOperation` bypass, which is not accessible from script context.

### 5.1 `SecurityException` Thrown By
All of the following trigger an immediate halt with a `SecurityException`:
- `ValidatePath()` — blocked path segment, root access, session metadata access
- `ValidateFileType()` — blocked extension (`.dll`, `.exe`, etc.)
- `ValidateWriteAccess()` — write attempt to a logic file (`.etlsql`, etc.)
- `CheckRunawayProtection()` — operation count or recursion depth exceeded
- `ValidateHost()` — connection to a host not in `AllowedHosts` (strict mode only)
- `ValidateEnvVar()` — read of an environment variable not in `AllowedEnvVars`

### 5.2 What Gets Logged
| Event | Logged |
| :--- | :--- |
| `SecurityException` thrown | Yes — message, path, operation type |
| Override command used (`SET ALLOW_...`) | Yes — command name, script line, current safe zone |
| `ENC:` decryption failure | Yes — failure recorded; ciphertext is **not** included |
| Script contains unencrypted credentials (`NeedsEncryption()`) | Warning issued in IDE/CLI |

---

## 6. Test Mode Behavior
The `SecurityService` has an explicit `IsTestMode` flag used only by the automated test harness. When active:

- Paths within `AppDomain.CurrentDomain.BaseDirectory` and `Path.GetTempPath()` are automatically authorized, bypassing standard directory blocks only.
- **Critical blocks are never bypassed** — `.git`, `.ssh`, `Windows`, `System32`, etc. remain blocked even in test mode.
- `IsTestMode` is **never readable or settable** from ETL-SQL script context. It is only injectable during test initialization.

> [!CAUTION]
> `IsInternalOperation` bypass exists for the `SessionManager` to read/write its own `.etlsession` files. This flag must **never** be exposed to script context or passed through any user-facing API surface.

---

## 7. Known Limitations & Open Risk Items

| Risk | Severity | Status |
| :--- | :--- | :--- |
| `SET ALLOW_...` override flags are purely state-based and not cryptographically signed | Medium | A malicious script author can add these freely in a configured safe zone |
| No rate-limiting or throttle on `SEND EMAIL` — possible spam amplification | Low | Manual safe zone + operation count limit provides some protection |
|`.sql` is on the write blocklist but also on the allowed-read whitelist | Note | Deliberate — reading `.sql` is allowed; writing is not |

---

## 8. Security Contact
To report a security vulnerability in ETL-SQL, open a confidential issue or contact the project maintainer directly. Do not post vulnerability details in public issues.

---

**Policy Version**: 0.6
**Compliance Standard**: Built with reference to NIST SP 800-204 (Microservices Security), NIST SP 800-132 (Password-Based Key Derivation), and OWASP CLI Security Principles.
**Last Review Date**: 2026-04-14
