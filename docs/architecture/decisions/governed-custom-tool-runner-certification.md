# Governed Custom Tool Runner - Security Controls & Evidence

## Overview
This document records the implemented security controls, tested boundaries, and explicit non-claims for the Governed Custom Tool Runner (`EXECUTE TOOL`) in ETL-SQL.

## Implemented Controls & Verified Evidence

### 1. Injection, Parameter Roles & Execution Control
* **Vector**: Script author or caller parameters attempting shell injection, multi-argument expansion, second-pass placeholder substitution, or interpreter code execution.
* **Control**: `ExecuteToolStatementHandler` launches processes with `UseShellExecute = false` and `ProcessStartInfo.ArgumentList` (no `cmd.exe` or `/bin/sh -c`). `BuildArguments` tokenizes `ARGS` templates first and replaces placeholders in a single pass so nested `{OTHER}` syntax inside parameter values is never re-expanded. Parameter role validation blocks option-shaped values (while permitting signed negative numbers and paths) and blocks interpreter execution switches (`-c`, `-e`, `--eval`, `/c`, `-command`) or unanchored caller script entrypoints unless an administrator sets `ALLOW_CALLER_CODE_ARGS = true`.
* **Evidence**: Verified by unit tests in `ToolSecretDeliveryTests` and `ExternalToolSecurityTests`.

### 2. Catalog Containment & Administrative Ownership
* **Vector**: Alias traversal (`../`), reparse-point swaps, unprivileged catalog registration, or `PATH`-hijacked native executable lookups.
* **Control**: `LocalToolCatalogProvider` enforces strict alias validation (`ToolAliasValidator`), root containment, and reparse-point checks across resolve, store, get, list, enable, disable, and delete. `ToolAdminService` requires elevated administrator/root privileges for machine catalog mutations, `OsToolCatalogProtector` protects catalog roots/entries with read-only service ACLs (`MachineAdministrator` default; `PersonalWorkstation` explicit only), and native `EXECUTABLE` `COMMAND` / `SCRIPT_PATH` values must be fully qualified non-UNC, non-reparse paths.
* **Non-claim**: Cryptographic artifact pinning has a separate [native artifact approval and evidence record](external-tool-use-and-artifact-approval.md#xt08--approve-and-launch-a-specific-native-artifact-version).

### 3. Dry-Run (`WHAT_IF`) Safety
* **Control**: `ExecuteToolStatementHandler` validates catalog definitions, native path contracts, Docker/policy approvals, resource limits, and parameter roles before `context.IsWhatIf` short-circuits, returning without creating scratch directories, issuing capability tokens, recording ledger starts, or spawning processes.

### 4. Container Image Pinning & Governance Policy
* **Control**: `CONTAINER` tools require a valid `@sha256:<64-hex>` digest and pass `ProcessPolicyRules.EnforceContainerToolImage` (enforcing enrolled enterprise `Security:AllowedDockerImages` policy, including revocation, and any configured host-admin `Governance:Docker:AllowedImages` allowlist).

### 5. Protocol Bounds & Staged Output Integrity
* **Control**: Output streaming enforces `Tools:Limits:MaxBytes`, `Tools:Limits:MaxRows`, `Tools:Limits:MaxLineChars`, and `Tools:Limits:MaxStderrChars` before deserialization into `#temp` / staged tables. Zero-exit completion, scratch `/tmp:rw,noexec,nosuid`, and capability tokens do not by themselves establish host isolation.

### 6. Container Resource Ceilings & Isolation Profile Distinction
* **Control**: `CONTAINER` runs inject explicit Docker ceilings (`--cpus`, `--memory`, `--memory-swap` equal to `--memory`, and `--pids-limit`) derived from administrator-controlled `Tools:Limits` configuration and bounded tool options, alongside `--read-only`, `--cap-drop ALL`, `--security-opt no-new-privileges:true`, non-root `--user`, and `--network none` by default.
* **Non-claim / Separation of Profiles**:
  1. **Standard Container (`ContainerStandard` / default `CONTAINER`)**: Constrained by Docker resource flags and Linux kernel namespaces; not equivalent to hypervisor/gVisor isolation for hostile multi-tenant execution.
  2. **Hardened Container (`Hardened` / `HardenedSandbox` / `HardenedShared` / `HostileTenant`)**: Rejected unless an approved hardened OCI runtime (`Tools:Limits:HardenedContainerRuntime`) is explicitly configured and network/mount isolation is intact.
  3. **Trusted Dedicated Native (`EXECUTABLE` / `TrustedNative`)**: Inherits the host OS account authority and cannot enforce container `--cpus`, `--memory`, `--pids-limit`, or `--network none`. Rejected in multi-tenant contexts (unless explicitly overridden) and rejected whenever container resource options or container/hardened isolation profiles are claimed.

---
**Status**: Active controls (adversarial host-kernel certification out of scope of unit suite)
**Updated**: November 2026
