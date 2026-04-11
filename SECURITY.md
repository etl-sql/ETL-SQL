# ETL-SQL Enterprise Security Policy

## 1. Executive Summary
ETL-SQL is engineered with a **Security-by-Design** philosophy, prioritizing the integrity of the host environment during the execution of data transformations. The engine employs a "Zero-Trust" isolation model, ensuring that all script-level operations are strictly sandboxed and cryptographically verified.

## 2. Threat Model & Security Philosophy
The engine treats all user-provided scripts as **Untrusted Actors**. Our security architecture is built on three core pillars:
- **Isolation**: Prevent scripts from exiting the approved workspace or accessing sensitive system assets.
- **Resource Governance**: Prevent denial-of-service (DoS) via runaway processes or recursive resource exhaustion.
- **Transparency**: Maintain a non-bypassable audit trail for all security-relevant exceptions.

## 3. Host System Isolation (The Sandbox)
The sandbox is enforced at the core evaluation layer, intercepting all filesystem and system-level calls before execution.

### 3.1 Non-Bypassable Path Validation (Zero-Trust)
All path resolutions undergo mandatory normalization and recursive segment matching.
- **Immutable System Blocklist**: Absolute protection for OS-critical directories (e.g., `Windows`, `System32`, `/etc`, `/lib`, `/root`). These blocks are enforced at the engine level and cannot be overridden by script flags.
- **Sensitive Asset Protection**: Explicitly blocks access to development/config metadata (e.g., `.git`, `.ssh`, `.aws`, `.kube`) to prevent credential harvesting.
- **Root Directory Lockdown**: Prevents operations targeting the system root (`C:\` or `/`) to protect filesystem integrity.

### 3.2 Immutable File Type Hardening
To prevent arbitrary code execution or system-level tampering, the following extensions are globally blacklisted:
- `.dll`, `.exe`, `.bat`, `.cmd`, `.sh`, `.msi`, `.sys`, `.com`, `.pfx`, `.cer`.

### 3.3 Resource Thresholds (Runaway Protection)
To maintain host stability, the engine enforces strict resource caps:
- **Operation Cap**: Maximum of 100 filesystem operations per script execution.
- **Recursion Depth**: Maximum of 5 directory layers for recursive operations.
- **Safe Zone Enforcement**: Safety overrides (e.g., `### ALLOW_GREATER_THAN_100_FILE`) are **only honored** when the target path resides within a verified **Approved Safe Zone** (the directory of the active script).

### 3.4 Script Immutability (Human-Centric Control)
To protect the integrity of the application's control plane, the engine enforces a strict write-block on logic files.
- **Protected Extensions**: Writing, moving, or renaming files with extensions `.etlsql`, `.rptsql`, `.sql`, `.etls`, `.py`, `.js`, `.sh`, `.bat`, or `.cmd` is strictly prohibited via script execution.
- **Human Mastery**: Application logic is reserved exclusively for the human operator. The engine is a consumer of logic, not a producer, preventing automated "self-modifying" script attacks.

## 4. Cryptographic Architecture
ETL-SQL implements a dual-layer cryptographic model to balance local security with asset portability.

### 4.1 Hardware-Bound Session Hardening (Data at Rest)
Session snapshots (`.etlsession`) and temporary cache data are protected using **Hardware-Locked Encryption** to ensure that sensitive ephemeral data remains local to the system.
- **Windows Architecture**: Utilizes OS-native **DPAPI** (`System.Security.Cryptography.ProtectedData`) in `CurrentUser` scope. 
- **Linux/macOS Architecture**: Utilizes **Machine-Locked AES-256 Encryption** using a high-entropy secret stored in a protected user directory (`~/.etl-sql/machine.key`). This key is generated on first-run and secured with OS-level file permissions (chmod 600).
- **Security Guarantee**: State is strictly non-portable; it can only be decrypted by the the original OS user on the original host machine.
- **Transparency**: Encryption is transparent to the user, eliminating password management risks for ad-hoc state.

### 4.2 Portable Asset Protection
For connection strings and files intended for cross-machine portability, the engine utilizes a **Master Password** model.
- **Algorithm**: AES-256 with PBKDF2 key derivation.
- **Key Derivation**: 10,000 iterations for resistance against brute-force attacks.
- **Entropy & Salting**: Every operation utilizes a **16-byte cryptographically random salt**, ensuring unique ciphertexts for identical inputs and providing robust protection against rainbow table attacks.

## 5. Auditing & Forensics
Every security violation or unauthorized access attempt triggers an immediate `SecurityException`, halting execution and generating a diagnostic entry in the session audit logs. 

> [!IMPORTANT]
> **Audit Integrity**: The engine explicitly blocks any script-level attempt to modify session audit metadata or recovery manifests.

---
**Policy Version**: 1.5  
**Compliance Standard**: Built with reference to NIST SP 800-204 (Microservices Security) and OWASP CLI Security Principles.  
**Last Review Date**: 2026-04-11
