# ETL-SQL Security Hardening Policy

This document outlines the security architecture and sandbox policies implemented to protect the host system during ETL-SQL script execution.

## Core Security Pillars

### 1. File System Sandboxing
Every path resolution in the engine is now intercepted by the `SecurityService` to enforce strict access control:
- **Absolute Paths**: All relative paths are normalized to full absolute paths before validation.
- **Root Protection**: Explicitly blocks any operation targeting the system root (e.g., `C:\` or `/`).
- **Protected Directory Blacklist**: Access is forbidden to sensitive system and environment folders:
    - `.git`, `.vscode`, `.idea`
    - `node_modules`, `bin`, `obj`
    - Windows system directories (`System32`, `Windows`)
- **File Type Whitelist**: Data operations are restricted to approved connector types:
    - Allowed: `.csv`, `.json`, `.parquet`, `.txt`, `.sql`, `.log`, `.xlsx`, `.xml`, `.yaml`, `.zip`, `.md`
    - Blocked: `.dll`, `.exe`, `.bat`, `.cmd`, `.sh`, `.msi`, `.sys`

### 2. Runaway Process Protection
To prevent accidental loops or malicious scripts from overwhelming the system, the engine enforces hard safety caps:
- **Operation Limit**: Maximum of **100** file/directory operations per script run.
- **Recursion Limit**: Maximum directory nesting depth of **5** layers for operations like `COPY` or `DELETE_CONTENTS`.

### 3. Explicit Permission Overrides
Advanced users can bypass safety limits by including explicit "security flags" at the top of their scripts. These flags signal intentional authorization:
| Flag | Description |
| :--- | :--- |
| `### ALLOW_FILE_TYPE_ACCESS` | Allows processing of file types not in the standard whitelist. |
| `### ALLOW_GREATER_THAN_100_FILE` | Disables the 100-operation safety cap for high-volume ETL tasks. |
| `### ALLOW_RECURSIVE_GREATER_THAN_5_LAYERS` | Allows deep recursive directory tree walks. |

### 4. Session & Storage Isolation
- **Secure Session Management**: Session snapshots and temp tables are restricted to approved application data folders (`%AppData%`).
- **Encryption by Default**: All sensitive connection strings and session states are encrypted using machine-specific keys or user-provided master passwords.

## Incident Reporting
Any security violation triggers an immediate `SecurityException`, which halts script execution and logs the event to the session audit trail:
> `Runaway protection: File operation count (101) exceeds the safety limit of 100.`

---
*Policy Version: 1.1*
*Last Updated: 2026-04-11*
