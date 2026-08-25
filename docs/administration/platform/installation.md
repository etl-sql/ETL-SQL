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

### Windows

1. Run the `ETL-SQL-Enterprise-v0.18.0.msi` installer.
2. Select the workstation and server features required for the host.
3. The installer registers these Windows services when the server features are selected:
   - `ETL-SQL-Orchestrator`
   - `ETL-SQL-Portal`
4. Review the service accounts before production use. The installer default is `LocalSystem`; use a least-privilege domain or local service account when the service needs access to network shares, database drivers, certificates, or controlled script roots.

### Linux

Install the package for your distribution, then enable the services you intend to run:

```bash
sudo dpkg -i etl-sql_0.18.0_amd64.deb
sudo systemctl enable etl-sql-orchestrator
sudo systemctl start etl-sql-orchestrator
sudo systemctl enable etl-sql-portal
sudo systemctl start etl-sql-portal
```

For RPM-based systems, use the matching `.rpm` package and the same `systemctl` service names.

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

