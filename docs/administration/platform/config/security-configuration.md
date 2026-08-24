# Security Configuration

> **Applies to:** Solo · Team · Enterprise · SaaS

Configure the zero-trust execution sandbox: path boundaries, egress fencing, file-operation limits, regex and SMTP anti-abuse ceilings, and disk-spill encryption.

ETL-SQL settings can be configured via `appsettings.json`, environment variables (replace `:` with `__`), or command-line parameters.

---

## Path Protection and Approved Zones

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Security:PathProtectionMode` | string | `Restricted` | Controls filesystem boundary protection. `Restricted` blocks reads/writes outside approved safe zones. |
| `Security:ApprovedSafeZones` | array | `[]` | Absolute paths where scripts may read or write files when `PathProtectionMode` is `Restricted`. |
| `Security:AdditionalBlockedPaths` | array | `[]` | Administrator-defined paths or path segment names to deny. Rooted entries match by canonical path prefix; relative entries match path segments. |
| `Security:AdditionalBlockedExtensions` | array | `[]` | Extra file extensions to deny. These only add restrictions and cannot weaken built-in blocked extensions. |

> [!IMPORTANT]
> Never add system directories (`C:\Windows`, `/etc`, `/root`, `.git`, `.ssh`) to `ApprovedSafeZones`. The engine blocks script reads/writes to `.sql`, `.etlsql`, and `.rptsql` files regardless of safe zones — this is the **Script Immutability Guardrail**.

---

## Host and Environment Variable Access

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Security:AllowedHosts` | array | `["*"]` | Hosts permitted to connect to the Portal's HTTP endpoints. |
| `Security:AllowedEnvVars` | array | `["TEMP", "USERDOMAIN", ...]` | Environment variables accessible within ETL scripts via `ENV_VAR()`. |

---

## Execution Limits

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Security:MaxFileOperationsPerScript` | integer | `100` | `SET ALLOW_FILE_OPERATIONS = n` or `SET MAX_FILE_OPERATIONS = n` | Maximum file modifications per script run. |
| `Security:MaxRecursiveNestingDepth` | integer | `5` | `SET ALLOW_RECURSIVE_LAYERS = n` | Maximum nesting depth when scripts call other scripts via `RUN SCRIPT`. |
| `Security:MaxParallelDegree` | integer | `32` | `SET MAX_PARALLEL_DEGREE = n` | Maximum concurrent threads used in parallel command blocks. |
| `Security:MaxStringResultSize` | integer | `104857600` | `SET MAX_STRING_RESULT_SIZE = n` | Maximum length in bytes for string results (default 100 MB). |
| `Security:MaxSmtpEmailsPerScript` | integer | `100` | `SET MAX_SMTP_EMAILS_PER_SCRIPT = n` | Anti-spam ceiling on emails sent per script run. |
| `Security:RegexMatchTimeoutMs` | integer | `1000` | `SET REGEX_MATCH_TIMEOUT = n` | Maximum milliseconds for a single regex evaluation. |
| `Security:MaxInternalOperations` | integer | `100000` | — | Limit on internal loop execution steps to prevent infinite loops. |

---

## Disk Spill Security

| Key | Type | Default | Ad-Hoc SET Command | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Security:SpillEncryptionEnabled` | boolean | `true` | `SET SPILL_ENCRYPTION ON\|OFF` | Encrypts buffers spilled to local disk during heavy queries. |
| `Security:SpillCompressionEnabled` | boolean | `true` | `SET SPILL_COMPRESSION ON\|OFF` | Compresses spilled buffers to reduce disk usage. |
| `Security:SpillFormat` | string | `Arrow` | `SET SPILL_FORMAT = 'AUTO'\|'JSON'\|'PARQUET'` | Serialization format for data spills. |

---

## Infrastructure Egress Fence

Independently of `Security:AllowedHosts`, connectors can **never** reach the hosting infrastructure a deployment runs on. The fence is always active — it applies to standalone, unenrolled, Managed Dedicated, and shared multi-tenant hosts alike.

The fence denies four classes of destination:

| Class | Covers |
| :--- | :--- |
| Cloud instance metadata | `169.254.169.254`, `169.254.170.2`, `168.63.129.16`, `100.100.100.200`, `192.0.0.192`, `fd00:ec2::254`, `metadata.google.internal`, `metadata.goog`, `metadata`, `instance-data` |
| Link-local node services | Anything in `169.254.0.0/16` or `fe80::/10` (kubelet, node agents) |
| Container runtime bridge | `host.docker.internal`, `gateway.docker.internal`, `host.containers.internal`, `kubernetes.default[.svc[.cluster.local]]` and related names |
| Cluster service discovery | Any host ending `.svc`, `.cluster.local`, `.svc.cluster.local`, `.pod.cluster.local` |

Obfuscated address forms are normalized before the check. The fence applies at connection creation, on every dynamic REST URL (including redirect targets), and again at socket-connect time. It is port-independent.

> [!NOTE]
> Loopback and RFC 1918 private ranges are **not** fenced — on-premises databases live there and are governed by `Security:AllowedHosts`.

### Egress Fence Exemptions

If you genuinely run a service at a fenced address, list it exactly in `Security:EgressFenceExemptions`. Wildcards are rejected.

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Security:EgressFenceExemptions` | array | `[]` | Exact hosts or addresses exempted from the built-in infrastructure egress fence. Wildcards are rejected. |

### Declaring Internal Ranges

Declare your hosting control plane CIDRs, internal management networks, and other tenants' pod/service CIDRs in `Security:DeniedEgressRanges` (or in authoritative organization policy under `network.deniedEgressRanges`):

| Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Security:DeniedEgressRanges` | array | `[]` | CIDR ranges this deployment's workloads may never reach. Cannot be exempted. A malformed entry is rejected by policy validation. |

```json
"Security": {
  "DeniedEgressRanges": [ "10.42.0.0/16", "172.20.0.0/14", "fd12:3456::/32" ]
}
```

> [!CAUTION]
> `DeniedEgressRanges` entries have **no** exemption path. A malformed CIDR is rejected by policy validation rather than silently skipped, so a typo cannot quietly remove a control you believe is in place.

---

## Related

- [Configuration Settings Reference](../appsettings-reference.md) — full config hub
- [Engine Configuration](engine-configuration.md) — memory, batching, and query limits
- [Authoritative Organization Policy](../organization-policy.md) — fleet-wide policy enforcement
- [Platform Administration](../README.md)
