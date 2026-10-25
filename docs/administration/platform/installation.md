# Installation and Deployment

Installing ETL-SQL as workstation tooling, as managed services, or as a multi-node cluster.

## By deployment profile

| Profile | What you install |
| :--- | :--- |
| **Solo / Workstation** | The CLI only. Skip every service section — no Portal, no Orchestrator service, no reverse proxy. The OS account is your security boundary. |
| **Team / SME** | CLI plus the Portal and Orchestrator as managed services (Windows Service or systemd) on one host, behind TLS. |
| **Enterprise / Corporate** | As Team, across multiple nodes: shared PostgreSQL, shared artifact roots, a shared Data Protection key ring, and a load balancer with session affinity. See [state and high availability](state-and-ha.md). |
| **SaaS / Departmental** | As Enterprise, **installed once per environment** with a distinct port base, service account, data root and key set per environment. Generate the requirement list with `GET /api/admin/environments/plan` rather than deriving it by hand. |

New here? [Administration by deployment profile](../by-profile.md) gives each profile an ordered
path through these documents.

## Deployment Components

ETL-SQL can be deployed as workstation tooling, server services, or both.

| Component | Purpose | Typical host |
| :--- | :--- | :--- |
| Workstation | `ETL-SQL` CLI, terminal IDE, language server, and report tooling for script authors | Developer workstations, CI runners |
| Orchestrator Service | Background scheduler and job execution service | Application server |
| Portal | Web application for report catalog, snapshots, subscriptions, and administration | Application server |

The Orchestrator and Portal may run on the same host, on separate hosts, or as multiple
load-balanced nodes. Single-node deployments use SQLite by default. Practical High Availability
deployments use shared PostgreSQL state plus shared Portal artifact roots; configure the portal with
the orchestrator API URL and shared API key when the services are split. Use
[Operations/Capacity_Planning.md](../../architecture/decisions/capacity-planning.md) when deciding whether to start
shared or split the services.

---

## Production Installation

Use the packaged installers below. From v0.22.0, the source-only `scripts/install.ps1`,
`scripts/install.sh` and `install-service-windows.ps1`/`install-service-linux.sh` helpers are
removed. The workstation templates never downloaded binaries; the separate service helpers did
not implement the packaged installation's configuration and upgrade lifecycle. Use the MSI's
workstation/server feature selection on Windows and the Linux package's service units on Linux.
On macOS, use the native ZIP or Intel DMG described below.

### Windows

1. Run the `ETL-SQL-Enterprise-v0.21.0.msi` installer.
2. Select the workstation and server features required for the host.
3. The installer registers these Windows services when the server features are selected:
   - `ETL-SQL-Orchestrator`
   - `ETL-SQL-Portal`
4. Review the service accounts before production use. The installer default is `LocalSystem`; use a least-privilege domain or local service account when the service needs access to network shares, database drivers, certificates, or controlled script roots.

From v0.22.0, the MSI stops if it cannot preserve installed settings or complete service
configuration. Protected files are staged before replacement, and failed writes retain existing
keys. Final secret-file permissions allow only SYSTEM and Administrators.

If a failed replacement leaves an `appsettings.secrets.json.*.recovery` file, keep it. Configuration
will refuse to retry until an administrator reviews that protected recovery copy and restores the
original secrets file if needed. Do not discard it or generate replacement keys: existing cached
datasets require their original at-rest key.

### Linux

Install the package for your distribution, then enable the services you intend to run:

```bash
sudo dpkg -i etl-sql_0.21.0_amd64.deb
sudo systemctl enable etl-sql-orchestrator
sudo systemctl start etl-sql-orchestrator
sudo systemctl enable etl-sql-portal
sudo systemctl start etl-sql-portal
```

The current Linux package builder emits Debian `.deb` artifacts.

### macOS workstation

Use the release ZIP matching `osx-x64` or `osx-arm64`. The Intel DMG contains `ETL-SQL.app`;
its CLI tools live under `Contents/Resources/bin` inside the application bundle.

From v0.22.0, DEB payloads and macOS application bundles remove group/other write access from
program trees while preserving executable and read-only bits. Native package builders require
`dpkg-deb` on Linux or `hdiutil` on macOS and fail when those tools are missing. They resolve staging
under the repository and reject linked staging ancestors or outputs before cleanup.
DEB and DMG builds also reject publish folders containing runtime secrets, databases, key rings
or runtime directories. Publish into a clean folder. Rejection preserves prior staging and packages.

The portable ZIP producer uses the same runtime-state inventory. It validates platforms and
versions before cleanup and checks archive file names and hashes before promoting each ZIP.
Complete builds stage under `artifacts/portable-release/<attempt>/output/` and replace `release/`
after packaging, SBOM and checksum work succeeds. Failed attempts and previous releases remain
under the attempt directory with `transaction.json`. Inspect that record and `previous/` before
resolving a recovery failure. Concurrent builds are blocked by `publish.lock`; a lock left by an
interrupted producer requires checking that the original process has stopped before removing it.
Retained attempts use disk space until you remove them after review.
The release wrapper extends this transaction through native installer collection. Prior native
build outputs are retained in `native-previous/` and restored after failure; partial replacements
are retained in `native-failed/`. Final checksums include the native packages before promotion.

### Docker / Containerized

ETL-SQL provides pre-configured Docker Compose configurations to run containerized instances of the Orchestrator and Portal services.

1. **Pull-Based Deployments (Operator Workflow)**:
   The central [docker-compose.yml](../../../docker-compose.yml) file is structured for container registry pulls. It references pre-built images:
   - `etl-sql/orchestrator:latest` (runs on port `5001`)
   - `etl-sql/portal:latest` (runs on port `5000`)

   Deploying this configuration only requires copying `docker-compose.yml` to your host server and running:
   ```bash
   docker compose up -d
   ```
   *Note: This workflow does not require the C# source tree or SDK tooling to be installed on the host.*

2. **Persistence and Volumes**:
   The compose file exposes volume binds to preserve runtime data on the host machine:
   - `./data` — Holds the portal's SQLite catalog database (`portal.db`)
   - `./Reports` — Directory for uploaded ETL scripts and report queries
   - `./Snapshots` — Storage for generated report extracts and snapshots
   - `./logs/orchestrator` — Background execution log output

3. **Development Builds (Source Override)**:
   If you have the source tree cloned locally and need to test code modifications inside the containers, copy [`deploy/docker/docker-compose.override.example.yml`](../../../deploy/docker/docker-compose.override.example.yml) to `docker-compose.override.yml`. When Docker Compose finds this file alongside the main compose config, it automatically overrides the registry images and compiles the local C# code via multi-stage builds.

4. **High Availability Scaling**:
   For multi-node active-active load-balanced clusters, use the HA-specific docker compose template located at [deploy/docker/docker-compose.ha.yml](../../../deploy/docker/docker-compose.ha.yml). This setup supports variable container scaling behind a sticky HAProxy load balancer. See [Containerized HA Clustering (Docker Compose)](state-and-ha.md#containerized-ha-clustering-docker-compose) for detailed instructions.

### First-Run Checklist

Before exposing the services to users:

1. Set a production JWT secret for the portal.
2. Set an orchestrator API key if the management API is reachable beyond a loopback-only or isolated internal network.
3. Configure HTTPS certificates or place the services behind a TLS-terminating reverse proxy.
4. Set script, snapshot, dataset, and map root directories to dedicated service-owned folders.
5. Confirm backup coverage for portal/orchestrator state and artifact roots: SQLite files for
   single-node deployments, or PostgreSQL backups plus shared storage snapshots for HA deployments.
6. Run a simple `MOCKDB` script and a sample report from the service account context.

---

