# Departmental Isolation Topology

This document defines how to run **multiple isolated ETL-SQL environments** — for example
`dev`/`test`/`prod`, or separate departments — on shared or dedicated hardware **without** introducing
shared-table multitenancy. Each environment is a complete, independent ETL-SQL deployment: its own
Portal, Orchestrator, databases, artifact storage, keys, and service identity. Nothing is shared at
the application layer, so a fault, credential leak, or noisy workload in one environment cannot read
or mutate another.

Deployment templates that implement this topology live under [`deploy/`](../../../deploy):
Docker Compose ([`deploy/docker`](../../../deploy/docker)), Windows Services
([`deploy/windows`](../../../deploy/windows)), and systemd ([`deploy/systemd`](../../../deploy/systemd)).
The isolation verifier ([`deploy/verify`](../../../deploy/verify)) proves two environments do not overlap.

---

## 1. Isolation model

ETL-SQL is **single-tenant per deployment**. "Departmental isolation" means running *N* such
deployments side by side, each labelled by an **environment id** (a short, lowercase, DNS-safe token
such as `dev`, `finance`, `hr-prod`). The environment id is the single parameter that drives every
isolated resource name, path, port, account, and key below.

There is **no cross-environment trust**. The only supported way to move content between environments
is the explicit, secret-free portability package (Phase 2); read-only fleet visibility is the only
supported aggregation (Phase 3). Neither grants one environment access to another's data or keys.

---

## 2. Per-environment resources

Every environment **must** own a distinct instance of each resource below. Sharing any one of them
breaks isolation.

| Resource | Single-node default | HA / shared deployment | Isolation requirement |
| :--- | :--- | :--- | :--- |
| **Portal database** | `…/<env>/data/portal.db` (SQLite) | dedicated PostgreSQL database, e.g. `portal_<env>` | Distinct file/database **and** distinct DB login per environment. Never share a database or login across environments. |
| **Orchestrator database** | `…/<env>/data/etlsql.db` (SQLite) | dedicated PostgreSQL database, e.g. `orch_<env>` | As above. |
| **Artifact root** (scripts, snapshots, datasets, maps) | `…/<env>/{Reports,Snapshots,datasets,maps}` | dedicated share/prefix per environment (`Smb`/UNC) | Distinct root; the environment's service identity is the only principal with access. |
| **Data Protection key ring** | `…/<env>/data/.portal-keys` | dedicated shared path per environment | Distinct per environment. A shared key ring lets one environment decrypt another's protected cookies/state. |
| **Service identity** | dedicated OS account per environment | dedicated account / gMSA per environment | One account per environment per service. The account is granted access **only** to that environment's paths, databases, and keys. |
| **Network boundary** | distinct port pair per environment | distinct hostnames / network segments behind the load balancer | No environment listens on another's port; production environments should be network-segmented, not just port-separated. |
| **JWT signing secret** (`Portal:Jwt:Secret`) | unique 32+ char secret | same value across that environment's HA nodes only | Unique per environment; a shared secret lets a token minted for one environment authenticate to another. |
| **Dataset at-rest key** (`Portal:Dataset:AtRestKey`) | unique base64 key | same value across that environment's HA nodes only | Unique per environment. |
| **Orchestrator API key** (`Orchestrator:ApiKey` / `Portal:Orchestrator:ApiKey`) | unique key | same value across that environment's nodes only | Unique per environment; gates the Orchestrator job API. |

> **HA note:** within a *single* environment, all Portal/Orchestrator nodes share that environment's
> database, artifact root, Data Protection key ring, and the three keys above — that is required for
> Practical High Availability. The isolation boundary is **between environments**, never within one.

---

## 3. Naming and port conventions

Templates derive every name from the environment id (`<env>`):

| Item | Convention | Example (`finance`) |
| :--- | :--- | :--- |
| Compose project | `etlsql-<env>` | `etlsql-finance` |
| Windows services | `ETL-SQL-Portal-<env>`, `ETL-SQL-Orchestrator-<env>` | `ETL-SQL-Portal-finance` |
| systemd units | `etl-sql-portal@<env>`, `etl-sql-orchestrator@<env>` | `etl-sql-portal@finance` |
| OS service account | `etlsql-<env>` (Linux user/group) / `svc-etlsql-<env>` (Windows) | `etlsql-finance` |
| Install/data root | `/srv/etl-sql/<env>` (Linux), `C:\ETL-SQL\<env>` (Windows), `./<env>` (Docker) | `/srv/etl-sql/finance` |

**Port allocation.** Each environment gets a contiguous block so nothing collides. The templates use
a `PORT_BASE` per environment and derive:

| Service/endpoint | Offset from `PORT_BASE` | Default `dev` (`PORT_BASE=5000`) |
| :--- | :--- | :--- |
| Portal HTTP | `+0` | 5000 |
| Orchestrator HTTP | `+1` | 5001 |
| Portal HTTPS | `+2` | 5002 |
| Orchestrator HTTPS | `+3` | 5003 |
| PostgreSQL (HA, optional published) | `+32` | 5032 |

Assign each environment a distinct `PORT_BASE` at least 10 apart (e.g. `dev=5000`, `test=5010`,
`prod=5020`).

---

## 4. Config surface per environment

The per-environment values the templates set (via environment variables / drop-in config), grouped
by isolation concern:

```text
# Identity of the environment
ETLSQL_ENV=<env>

# Databases (single-node SQLite shown; HA uses Provider=Postgres + ConnectionString)
Portal__DatabasePath / Portal__Database__Provider / Portal__Database__ConnectionString
Orchestrator__Database__Provider / Orchestrator__Database__ConnectionString

# Artifact roots
Portal__ScriptRootPath / Portal__SnapshotDirectory / Portal__DatasetRootPath / Portal__MapRootPath
Portal__Storage__Provider / Portal__Storage__KeyRingPath

# Keys (unique per environment)
Portal__Jwt__Secret
Portal__Dataset__AtRestKey
Orchestrator__ApiKey  /  Portal__Orchestrator__ApiKey   (must match within the environment)

# Wiring + network
Portal__Orchestrator__ApiUrl
ASPNETCORE_URLS / Kestrel endpoints
```

The standalone single-node defaults stay SQLite + local storage; set `Provider=Postgres` and
`Storage:Provider=Smb`/`Unc` only for HA. See the
[Portal Administrators Guide](../../administration/portal/README.md) for the full key
reference and the [Administrator's Guide](../../administration/platform/README.md) for HA requirements.

---

## 5. Verifying isolation

After deploying two or more environments, run the isolation verifier
([`deploy/verify`](../../../deploy/verify)) to prove they do not overlap. It fails if any two
environments share a database target, artifact root, Data Protection key ring, port, service account,
or any of the three keys, and — where it can resolve OS permissions — that one environment's service
account cannot read another environment's data root or key ring. See
[§6 Runbook](#6-isolation-verification-runbook).

---

## 6. Isolation verification runbook

Run this whenever you add an environment, change a service account, or before promoting an environment
to production.

1. **Collect the environment descriptors.** Each environment exposes its effective per-environment
   resources as an environment descriptor file (the templates emit one as `<root>/<env>.env`). Gather
   the descriptor for every environment on the host (or fleet).
2. **Run the verifier** over all descriptors:
   - Linux/macOS: `deploy/verify/verify-isolation.sh /srv/etl-sql/*/«env».env`
   - Windows: `pwsh -File deploy/verify/Test-Isolation.ps1 -EnvFile C:\ETL-SQL\*\*.env`
3. **Resolve every reported overlap.** Any shared database target, artifact root, key-ring path, port,
   service account, or key is a hard failure — fix it before serving traffic.
4. **Confirm cross-account denial.** On a host running more than one environment, the verifier (run as
   an administrator) checks that environment A's service account is **not** granted read/write on
   environment B's data root and key ring. If your platform cannot be probed automatically, perform
   the manual check: attempt to read `<B data root>/portal.db` and `<B key ring>` as A's service
   account and confirm access is denied.
5. **Record the result** alongside the deployment change record. A clean verifier run is the evidence
   that the isolation boundary holds.

---

## 7. Fleet aggregation trust boundary

Once environments are isolated, an operator often wants **one read-only view of the whole fleet**.
ETL-SQL supports this without weakening isolation, under a strict trust boundary (P2.1):

- **Read-only.** The aggregator only issues `GET /api/fleet/status` against each environment. It never
  writes, runs scripts, or reads report data.
- **Scoped service account per environment.** Each environment provisions a dedicated user in the
  `FleetReader` role. That role authorizes **only** `GET /api/fleet/status` — every other endpoint
  (admin/identity, secrets, configuration, report publish/execute, report data) returns `403` for a
  FleetReader token. Issue a distinct FleetReader credential per environment; never reuse a department
  admin or a cross-environment credential.
- **No raw data blending.** `/api/fleet/status` returns only aggregate operational counts and
  sanitized inventory metadata — environment/node id, installed version, schema versions,
  policy version/hash/timestamps, provider names, readiness findings, and queue/storage health.
  No report rows, scripts, user identities, secrets, keys, policy payload values, local paths,
  certificate thumbprints, or event targets cross the boundary.
- **Containment.** Because the FleetReader credential authorizes nothing but the status endpoint, a
  compromised aggregator credential cannot pivot into any department's database, artifact storage,
  encryption keys, or execution capability — it can only read that environment's health summary. This
  is certified by `FleetContainmentTests`.

### What the aggregator reads

`GET /api/fleet/status` (role `FleetReader` or `Admin`) returns:

| Field | Meaning |
| :--- | :--- |
| `environment` | The environment id (`ETLSQL_ENV`). |
| `status` | Overall health (`Healthy` / `Degraded` / `Unhealthy`) from the node's health checks. |
| `queueDepth`, `activeExecutions` | Node-local queued and running execution jobs. |
| `failedRefreshes` | Reports whose last refresh failed. |
| `auditOutboxPending`, `auditOutboxFailed` | Durable audit-outbox backlog and terminal failures. |
| `storage` | Whether shared artifact storage is reachable (`ok` / `unavailable`). |
| `securityEvents` | Local security-event queue/collector health: pending/failed counts, oldest pending time, stored bytes, drops, and collector reachability. |
| `inventory.environment`, `inventory.nodeId` | Environment id and Portal node id for operations routing and HA affinity diagnosis. |
| `inventory.installedVersion` | Portal assembly version running on the node. |
| `inventory.schemaVersions` | Supported enrollment, policy-envelope, policy-payload, security-event schema versions plus Portal migration counts and last applied migration id. |
| `inventory.policy` | Enrollment/policy availability, policy version/hash, issuance/expiry/load timestamps, signing/client-certificate configured flags, optional client-certificate expiry, and governed-key count. |
| `inventory.providers` | Portal database provider and artifact-storage provider names only; no connection strings or paths. |
| `inventory.configurationFingerprint` | Stable hash of non-secret fleet configuration inputs used to spot drift between nodes/environments without exposing values. |
| `inventory.upgradeReadiness` | Boolean readiness plus findings such as pending migrations, unavailable storage, or unavailable/near-expiry enterprise policy. |

`FleetHealthAggregator` fans out to each environment's endpoint with its scoped token, tolerates
unreachable environments (reporting them rather than failing the whole view), and merges the results
into a `FleetHealthReport` (with `Total`, `Unreachable`, and `Unhealthy` counts).

The aggregator can shape that read-only result with `FleetViewOptions` after polling: search across
environment/node/status/provider/policy/readiness fields, filter by status/reachability/provider/
policy version/upgrade readiness, and group by status, environment, database provider, storage
provider, policy version, or upgrade readiness. This is a local presentation operation over the
already-authorized status payloads. It does not grant the aggregator any additional endpoint,
mutation, script execution, report data, identity, secret, or key access.

The aggregated `FleetHealthReport` also includes actionable findings synthesized from those same
status payloads:

- **Unsupported schema/version findings** — enrollment, policy-envelope, policy-payload, and
  security-event schemas that the aggregator does not support.
- **Missing capability findings** — an environment that omits inventory or security-event diagnostics.
- **Dependency findings** — unreachable environments, degraded/unhealthy environment health, pending
  migrations, unavailable artifact storage, unavailable enterprise policy, or near-expiry policy.
- **Divergence findings** — nodes in the same environment reporting different policy versions,
  policy hashes, installed versions, or non-secret configuration fingerprints.

Findings are advisory and read-only. They identify what an operator should inspect; they do not let
the fleet view mutate a remote environment or bypass the per-environment authority model.

### Rolling upgrade compatibility metadata and reports

Each inventory payload includes machine-readable compatibility metadata:

- `metadataVersion` — compatibility metadata contract version.
- `compatibilityWindow` — the rolling-compatibility promise for this node.
- `rollingUpgradeSequence` — the supported operator sequence:
  readiness check, node drain, binary deployment, single-owner database migration, health
  verification, traffic restoration, postflight readiness, and rollback decision.
- `components` — per-component contract metadata for Portal, engine, reporting/snapshots, Portal
  database schema, artifact storage, enterprise enrollment, policy envelope, policy payload,
  security-event collector, connectors, and plugins.

`FleetHealthAggregator.BuildUpgradeReport` produces fleet-wide preflight and postflight reports from
the already-collected status payloads. Preflight checks that environments are reachable and healthy,
inventory/compatibility metadata is present, compatibility metadata uses the supported contract,
installed versions remain within the advertised N-1 window, upgrade readiness is true, and Portal
schemas have no pending migrations before traffic is restored. The N-1 check requires nodes in the
same environment to report parseable semantic versions with the same major version and no more than
one minor version of spread; wider or unparseable version spans fail clearly as
`unsupported-compatibility-window`. Postflight adds a divergence check so mixed policy,
configuration, or installed-version states are visible after deployment. Package deployment remains
outside ETL-SQL; use Intune, SCCM, Ansible, Kubernetes, systemd, Windows Services, or your existing
release tooling to install binaries and drain/restore traffic.

Portal startup database mutation is serialized by `PortalDatabaseMigrationLock`: PostgreSQL uses a
provider advisory lock and SQLite uses a process-local semaphore. Fleet inventory exposes the
current migration state as `inventory.migration`: state, owner node id, provider, lock kind/key,
started/acquired/completed/updated timestamps, pending migration count, and sanitized error text.
Operators can therefore see which node owns migration/startup maintenance, whether it is waiting,
checking, applying, succeeded, or failed, and whether recovery should be restore-from-backup or a
normal retry after fixing the reported dependency.

### Machine and node lifecycle behavior

ETL-SQL uses two separate registries, with different lifecycle rules:

- **Policy machines** are enrolled execution identities known to the policy authority. Administrators
  register them through **Admin -> Policy Authority -> Machine enrollment** or
  `POST /api/admin/policy-authority/machines` with machine ID, enrollment ID, tenant, environment,
  optional client-certificate thumbprint, and optional canary group. Policy retrieval is always bound
  to this registered tenant/environment; caller-supplied headers cannot move a machine into another
  environment.
- **Service nodes** are live Portal/Orchestrator processes in the shared node registry. They are not
  manually registered. `NodeHeartbeatService` writes a TTL heartbeat with node ID, role, timestamps,
  and sanitized capacity/security-event metadata. A graceful shutdown deregisters immediately; a
  crashed node becomes stale when its heartbeat expires and is pruned as housekeeping.

Duplicate and retirement behavior:

- Registering a machine ID that is already active is rejected. To reassign a machine ID, revoke the
  old record first, then register it again with the new enrollment details. Prefer a new machine ID
  for replacements so incident history stays clear.
- A machine presenting a known machine ID with a different enrollment ID or tenant is denied as a
  copied/reassigned identity and the denial is audited as `POLICY_ENVELOPE_DENIED`.
- Revoking a policy machine marks it unusable immediately. Subsequent policy retrieval attempts for
  that machine return the uniform unauthorized-machine error and are audited; no cache or canary
  setting can override revocation.
- Retiring a service node means stopping the Portal/Orchestrator process cleanly so it deregisters
  its heartbeat. If a host is lost, wait for the TTL to expire or let `PruneExpiredNodesAsync`
  remove the stale row. Do not reuse a stale node ID manually; generated node IDs are process-unique.
- Fleet views treat unreachable environments and expired node heartbeats as operational findings,
  not authority to mutate another environment. Recovery remains local to the affected environment's
  operators or automation.
