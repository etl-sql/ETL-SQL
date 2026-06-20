# Departmental Isolation Topology

This document defines how to run **multiple isolated ETL-SQL environments** — for example
`dev`/`test`/`prod`, or separate departments — on shared or dedicated hardware **without** introducing
shared-table multitenancy. Each environment is a complete, independent ETL-SQL deployment: its own
Portal, Orchestrator, databases, artifact storage, keys, and service identity. Nothing is shared at
the application layer, so a fault, credential leak, or noisy workload in one environment cannot read
or mutate another.

Deployment templates that implement this topology live under [`deploy/`](../../deploy/):
Docker Compose ([`deploy/docker`](../../deploy/docker)), Windows Services
([`deploy/windows`](../../deploy/windows)), and systemd ([`deploy/systemd`](../../deploy/systemd)).
The isolation verifier ([`deploy/verify`](../../deploy/verify)) proves two environments do not overlap.

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
[Report Portal Administrators Guide](../ReportPortal_Administrators_Guide.md) for the full key
reference and the [Administrator's Guide](../Administrators_Guide.md) for HA requirements.

---

## 5. Verifying isolation

After deploying two or more environments, run the isolation verifier
([`deploy/verify`](../../deploy/verify)) to prove they do not overlap. It fails if any two
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
