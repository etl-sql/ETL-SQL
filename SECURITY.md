# ETL-SQL Enterprise Security Policy

## 1. Executive Summary
ETL-SQL is engineered with a **Security-by-Design** philosophy, prioritizing the integrity of the host environment during the execution of data transformations. The engine employs a "Zero-Trust" isolation model, ensuring that all script-level operations are strictly sandboxed and cryptographically verified.

---

## 2. Threat Model & Security Philosophy
The engine treats all user-provided scripts as **Untrusted Actors**. Our security architecture is built on four core pillars:

- **Isolation**: Prevent scripts from exiting the approved workspace or accessing sensitive system assets.
- **Resource Governance**: Prevent denial-of-service (DoS) via runaway processes or recursive resource exhaustion.
- **Transparency**: Maintain a non-bypassable audit trail for all security-relevant exceptions.
- **Credential Hygiene**: Ensure secrets, tokens, and passwords are never exposed in logs, diagnostics, or output streams.

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

> [!NOTE]
> Unknown extensions (not on either list) are blocked by default. Use the `### ALLOW_FILE_TYPE_ACCESS` override flag to allow specific unlisted extensions within an approved safe zone.

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
| Filesystem operation count | **100 per script** | `Security:MaxFileOperationsPerScript` | `### ALLOW_GREATER_THAN_n_FILE` |
| Recursive directory depth | **5 levels** | `Security:MaxRecursiveNestingDepth` | `### ALLOW_RECURSIVE_GREATER_THAN_n_LAYERS` |

> [!TIP]
> Error messages dynamically reflect the current configured limit, ensuring that developers know exactly which override flag (e.g., `### ALLOW_GREATER_THAN_500_FILE`) to use if a limit is increased by an administrator.


### 3.5 Network Egress Control (Outbound Hardening)
The engine provides a non-bypassable guard for all network-based connectors (`MSSQL`, `POSTGRES`, `API`, `SFTP`, etc.). This prevents data exfiltration to unauthorized endpoints.

**Default Behavior**: To maintain backward compatibility and ease of development, the engine is **unrestricted (`*`)** by default. Any valid network connection is permitted.

**Hardening (Strict Mode)**: To enable strict security, provide an explicit `AllowedHosts` list in your application configuration (`appsettings.json`). Once a non-empty list is provided, the engine clears the default "allow-all" flag and only permits connections to listed targets.

**Configuration Example (`appsettings.json`):**
```json
{
  "Security": {
    "AllowedHosts": [
      "localhost",
      "127.0.0.1",
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
- **Implicit Safety**: `localhost` and loopback IPs are not automatically allowed once strict mode is activated; they must be explicitly added to the list if needed.

### 3.7 Performance Observability & Resource Metrics
To facilitate monitoring and prevent stealth resource exhaustion, the engine provides high-visibility metrics for all execution sessions.

- **Periodic Emission**: The background `SchedulerService` emits a heart-beat log every 60 seconds containing the count of `ActiveJobs`, `QueuedJobs`, and `AvailableSlots`.
- **Per-Job Accountability**: Every script execution (spawned or in-process) captures and logs `PeakMemoryBytes` and `CpuTimeSeconds`.
- **Audit Visibility**: Use the `SHOW JOB HISTORY` command to inspect resource consumption for past executions. This provides an audit trail for spotting runaway scripts that may be staying within security limits but consuming excessive enterprise resources.

### 3.6 Hardening the Report Dashboard
The Report-SQL dashboard (`ReportPlayer`) exposes a live web interface that must be protected in multi-tenant environments.

- **Port Assignment**: By default, the server listens on `http://localhost:5200`. In production, this can be customized via `ReportPlayer:Port` in `appsettings.json` to avoid conflicts or to bind to specific interfaces.
- **Snapshot Integrity**: The dashboard uses a `SnapshotStore` that implements **atomic file writes** (write-to-temp-then-move) and process-level concurrency locking. This ensures that even during high-frequency refreshes, the data manifest cannot be corrupted.
- **Limited Surface Area**: The dashboard only exposes read-only visual data and slicer parameter updates. It does not provide arbitrary script execution capability to web users.


> [!IMPORTANT]
> Override flags are **only honored** when the target path resides within a verified **Approved Safe Zone** (registered directories in `ApprovedSafeZones`). Providing an override flag while operating outside a safe zone still throws a `SecurityException` — with a distinct message explaining that the path is outside an authorized workspace.

---

## 4. Cryptographic Architecture
ETL-SQL implements a dual-layer cryptographic model to balance local security with asset portability.

### 4.1 Hardware-Bound Session Hardening (Data at Rest)
Session snapshots (`.etlsession`) and temporary cache data are protected using **hardware-locked encryption**, ensuring sensitive ephemeral data remains local to the originating system.

| Platform | Mechanism | Scope |
| :--- | :--- | :--- |
| **Windows** | OS-native **DPAPI** (`ProtectedData.Protect`, `CurrentUser` scope) | Current OS user on current machine |
| **Linux / macOS** | **Machine-locked AES-256** using a high-entropy key at `~/.etl-sql/machine.key` (generated on first-run, `chmod 600`) | Current machine only |

**Security guarantee:** Session state is strictly non-portable. It cannot be decrypted by a different OS user, on a different machine, or after OS reinstallation.

### 4.2 Portable Asset Protection (`ENC:` Prefix)
For connection strings and files intended for cross-machine portability, the engine uses a **Master Password** model managed via `USE PASSWORD = '...'`.

| Property | Value |
| :--- | :--- |
| Algorithm | AES-256 (CBC) |
| Key derivation | PBKDF2 |
| Iterations | 10,000 (brute-force resistance) |
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

### 4.3 Credential Masking
Credentials are never allowed to appear in output. The engine enforces:
- Connection strings containing passwords are masked in all `SHOW CONNECTIONS` output, diagnostics, and exception messages.
- `ENC:` values are passed as-is to the `SecurityService` for decryption and are **never** logged.
- `PASSWORD` and `TOKEN` option values in `WITH()` blocks are redacted in any serialized connection metadata.

> [!CAUTION]
> Never include raw credentials, API keys, or database passwords in `PRINT` statements or `BODY` strings in `SEND EMAIL`. These will appear in session logs. Use `ENCRYPTED` typed variables and `ENC:` strings instead.

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

### 5.2 What Gets Logged
| Event | Logged |
| :--- | :--- |
| `SecurityException` thrown | Yes — message, path, operation type |
| Override flag used (`### ALLOW_...`) | Yes — flag name, script line, current safe zone |
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
| PBKDF2 iteration count of 10,000 is below the current NIST SP 800-132 recommendation of ≥ 600,000 | Medium | Consider increasing; may impact startup latency |
| No per-user or per-script `ApprovedSafeZones` management UI | Medium | Safe zones are currently added programmatically only |
| `### ALLOW_...` override flags are comment-based and not cryptographically signed | Medium | A malicious script author can add these freely in a safe zone |
| No network egress controls — any `API`/`SFTP`/`FTP`/`SMTP` connection can reach any endpoint | Medium | **Implemented (SEC-2)** — Configure via `AllowedHosts` in appsettings.json |
| No rate-limiting or throttle on `SEND EMAIL` — possible spam amplification | Low | Manual safe zone + operation count limit provides some protection |
|`.sql` is on the write blocklist but also on the allowed-read whitelist | Note | Deliberate — reading `.sql` is allowed; writing is not |

---

## 8. Security Contact
To report a security vulnerability in ETL-SQL, open a confidential issue or contact the project maintainer directly. Do not post vulnerability details in public issues.

---

**Policy Version**: 0.5
**Compliance Standard**: Built with reference to NIST SP 800-204 (Microservices Security), NIST SP 800-132 (Password-Based Key Derivation), and OWASP CLI Security Principles.
**Last Review Date**: 2026-04-13
