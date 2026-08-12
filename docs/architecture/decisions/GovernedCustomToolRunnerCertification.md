# Governed Custom Tool Runner - Adversarial Certification Evidence

## Overview
This document records the adversarial certification evidence for the Governed Custom Tool Runner extension in ETL-SQL. The runner implements OCI-hardened execution bindings, zero-trust network policies, and isolated capabilities to ensure that custom containerized tools cannot compromise the engine, access unauthorized tenant data, or exhaust host resources.

## Certification Vectors & Evidence

### 1. Injection & Execution Control
* **Vector**: Malicious script author attempting to inject arbitrary shell commands or overwrite the container entrypoint.
* **Evidence (Pass)**: The `ExecuteToolStatementHandler` executes Docker via `System.Diagnostics.Process` bypassing shell evaluation (`cmd.exe` / `bash -c`). Arguments are passed directly in the `ArgumentList`. All execution options (image, working directory, environment variables) are strictly parsed from the validated AST and are immutable during execution.
* **Artifact**: `ExecuteToolStatementHandler.cs` lines 88-210.

### 2. Sandbox Escape & Read-Only Roots
* **Vector**: Tool attempting to overwrite host-mounted binaries, install rootkits, or mutate the container image layer.
* **Evidence (Pass)**: The OCI binding enforces `--read-only` root filesystems, `--cap-drop ALL`, and `--security-opt no-new-privileges:true`. A dedicated `--tmpfs /tmp:rw,noexec,nosuid,size=65536k` provides strict, isolated scratch space with `noexec` to prevent runtime code downloading and execution.

### 3. Unauthorized Data, Secret, and Network Access
* **Vector**: Tool attempting to exfiltrate data via public internet, or probe the Orchestrator/Portal control plane.
* **Evidence (Pass)**: 
  * Network isolation is enforced by default via `--network none`.
  * Secrets are resolved just-in-time and passed strictly via `--env` using the explicit `CAPABILITY_SECRETS` allowlist.
  * API access to the Orchestrator is restricted via short-lived `CapabilityToken` payloads. Tokens are cryptographically tied to the session ID, tenant, policy hash, and expire automatically. They grant access only to the current sandbox context.

### 4. Artifact Substitution & Image Spoofing
* **Vector**: Supply chain attack replacing the tag of a benign image with a malicious one on the registry.
* **Evidence (Pass)**: The `CONTAINER` tool handler explicitly rejects execution unless the `IMAGE` identifier contains a pinned digest (`@sha256:`). Tags (like `latest` or `v1.2`) are insufficient for execution.

### 5. Protocol Confusion & Staged Output Integrity
* **Vector**: Tool streaming arbitrary memory payloads or malformed responses to crash the engine parser.
* **Evidence (Pass)**: 
  * Protocol bounds are enforced via configurable limits (e.g., 100MB / 1M rows) before JSON deserialization.
  * Outputs are buffered to an `InMemoryDataSource` and schema-validated against the `#targetTable`. Only perfectly conformed and fully validated outputs are merged into the session context upon a successful `0` exit code.

### 6. Checkpoint Replacement & Idempotency
* **Vector**: Tool repeating side-effecting operations upon job retry or orchestrator restart.
* **Evidence (Pass)**: 
  * A deterministic `operationId` is computed by hashing the session ID, tool digest, protocol version, execution policy hash, and all sanitized arguments/environments.
  * This hash is recorded in the `ExecutionLedger`. Upon job resume, any tool with a matching identity and a `Completed` status is bypassed without re-invocation. Capabilities (`ETLSQL_CAPABILITY_TOKEN`) are explicitly excluded from the idempotency hash and regenerated dynamically on resume to avoid replay attacks.

### 7. Resource Exhaustion
* **Vector**: Tool performing infinite loops or consuming all disk space/memory.
* **Evidence (Pass)**: 
  * Memory and CPU limits are governed by the tenant's `ExecutionPolicy`.
  * Disk writes are constrained by the `tmpfs` `size=65536k` limitation, preventing host disk starvation.

### 8. Cross-Tenant Isolation
* **Vector**: Tool deployed in a shared SaaS environment accessing another tenant's mounts or network namespace.
* **Evidence (Pass)**: 
  * Capability tokens explicitly bake in the `TenantId` retrieved from the `StorageCapability` context.
  * Shared session volumes are strictly partitioned. The execution boundary prevents the tool container from sharing IPC, PID, or Network namespaces with other tenant instances.

## Isolation Profiles
To prevent merging isolation strategies into one weak default, the runner conceptually separates:
1. **Hardened Shared**: `CONTAINER` tool bindings with OCI capabilities dropped, `nobody` user enforcement, and zero networking.
2. **Dedicated Environment**: `EXECUTABLE` native bindings, running with host limits but trusted to interact with the broader dedicated instance (relying on single-tenant host segregation).
3. **Interactive Isolation**: Real-time tools triggered via the IDE/LSP which bypass durable ledger idempotency but retain strict `stdout` streaming bounds.

---
**Status**: Certified
**Date**: August 2026
