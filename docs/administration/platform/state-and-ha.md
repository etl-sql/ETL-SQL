# Portal State, Data Roots, and High Availability

## 6. Portal State and Data Roots

The Portal constrains filesystem access to configured roots. Set these to service-owned directories rather than broad user folders:

| Setting | Purpose | Default in code |
| :--- | :--- | :--- |
| `Portal:DatabasePath` | Portal SQLite database | `./portal.db` |
| `Portal:Orchestrator:DatabasePath` | Location of Orchestrator's SQLite DB from Portal context | `../Orchestrator/etlsql.db` (relative to Portal database directory) |
| `Portal:Database:Provider` | Portal state provider: `Sqlite` or `Postgres` | `Sqlite` |
| `Portal:Database:ConnectionString` | Portal PostgreSQL connection string when provider is `Postgres` | *(required for Postgres)* |
| `Portal:ScriptRootPath` | Report and job script browser root | `./Reports` |
| `Portal:SnapshotDirectory` | Report snapshot output | `./Snapshots` |
| `Portal:DatasetRootPath` | Dataset files managed by the portal | `./data/datasets` |
| `Portal:MapRootPath` | Map assets used by reports | `./data/maps` |
| `Portal:Storage:Provider` | Artifact provider: `Local` or `Smb`/`Unc` | `Local` |
| `Portal:Storage:KeyRingPath` | ASP.NET Data Protection key ring and Keys artifact root | `.portal-keys` beside the portal DB |
| `Portal:Topology:ExpectedMode` | Readiness policy mode: `Auto`, `Standalone`, `Departmental`, or `HighAvailability` | `Auto` |
| `Portal:Topology:MinLivePortalNodes` | Minimum live Portal heartbeats required by `/healthz` in HA mode | `1` |
| `Portal:Topology:MinLiveOrchestratorNodes` | Minimum live Orchestrator heartbeats required by `/healthz` in HA mode | `0` |
| `Orchestrator:DatabasePath` | Orchestrator SQLite database | `%LocalAppData%/ETL-SQL/etlsql.db` |
| `Orchestrator:Database:Provider` | Orchestrator state provider: `Sqlite` or `Postgres` | `Sqlite` |
| `Orchestrator:Database:ConnectionString` | Orchestrator PostgreSQL connection string when provider is `Postgres` | *(required for Postgres)* |

The portal rejects script, snapshot, map, and dataset paths that resolve outside their configured roots.

### 6.1 Practical High Availability Configuration

For a load-balanced HA deployment, every Portal and Orchestrator node must point at the same
PostgreSQL database deployment. Every Portal node must also point at the same shared artifact roots and
the same Data Protection key ring. The supported shared filesystem provider is `Smb`/UNC.

Example Portal node configuration:

```json
{
  "Portal": {
    "Database": {
      "Provider": "Postgres",
      "ConnectionString": "Host=pg-ha.internal;Database=etlsql_portal;Username=etl_portal;Password=..."
    },
    "Storage": {
      "Provider": "Smb",
      "KeyRingPath": "\\\\fileserver\\etlsql\\keys"
    },
    "ScriptRootPath": "\\\\fileserver\\etlsql\\reports",
    "SnapshotDirectory": "\\\\fileserver\\etlsql\\snapshots",
    "DatasetRootPath": "\\\\fileserver\\etlsql\\datasets",
    "MapRootPath": "\\\\fileserver\\etlsql\\maps",
    "LoadBalancer": {
      "SessionAffinityEnabled": true,
      "SessionAffinityCookieName": "ETLSQL_PORTAL_AFFINITY",
      "SessionAffinityCookieMinutes": 480
    },
    "Topology": {
      "ExpectedMode": "HighAvailability",
      "MinLivePortalNodes": 2,
      "MinLiveOrchestratorNodes": 1,
      "RequirePostgresForHa": true,
      "RequireSharedKeyRingForHa": true
    },
    "Orchestrator": {
      "ApiUrl": "https://orchestrator-vip.example.com:5003",
      "ApiKey": "your-shared-secret"
    }
  }
}
```

Example Orchestrator node configuration:

```json
{
  "Orchestrator": {
    "ApiKey": "your-shared-secret",
    "Database": {
      "Provider": "Postgres",
      "ConnectionString": "Host=pg-ha.internal;Database=etlsql_orchestrator;Username=etl_orch;Password=..."
    },
    "ScriptRoot": "\\\\fileserver\\etlsql\\scripts"
  },
  "Jobs": {
    "UseProcessSpawning": true,
    "ExecutablePath": "C:\\Program Files\\ETL-SQL\\bin\\ETL-SQL.exe"
  },
  "Scheduler": {
    "QuarantineFailureThreshold": 5
  },
  "Cluster": {
    "NodeHeartbeatSeconds": 30
  }
}
```

Operational requirements:

- Use sticky routing on the `ETLSQL_PORTAL_AFFINITY` cookie, or the configured
  `Portal:LoadBalancer:SessionAffinityCookieName`, because interactive sessions are node-local.
- Point load balancer health checks at `GET /healthz`. It returns HTTP 200 only when the Portal can
  reach PostgreSQL, shared snapshot storage, and the node-registry/lease store, and when the configured
  topology contract is satisfied. Use `GET /health` for richer monitoring.
- Keep `Portal:Jwt:Secret`, `Portal:Dataset:AtRestKey`, `Portal:Storage:KeyRingPath`, and
  `Portal:Orchestrator:ApiKey` identical across Portal nodes.
- Run Portal and Orchestrator under service identities that can read/write the configured PostgreSQL
  databases and shared storage roots. For SMB/UNC roots, use a domain identity or managed service
  account with explicit share and NTFS permissions.
- Back up PostgreSQL and the shared artifact roots as one coordinated recovery set. The HA state is no
  longer represented by only `portal.db` and `etlsql.db` files.

For the supported standalone, departmental, and HA topologies; readiness response contract; failure
certification matrix; and responsibility boundary between ETL-SQL and infrastructure, see
[`docs/architecture/decisions/HA_Topology_Failure_Certification.md`](../../architecture/decisions/HA_Topology_Failure_Certification.md).

### 6.2 Containerized HA Clustering (Docker Compose)

For container-native deployments (such as Docker engines, overlay networks, or Swarm environments), ETL-SQL provides a clustered, multi-node Compose template under [`deploy/docker/`](../../../deploy/docker) designed to run an active-active clustered environment with dynamic scaling.

The HA container configuration utilizes:
- **Shared PostgreSQL Database**: Centralized PostgreSQL container (configured via `docker-compose.ha.yml`) that replaces local SQLite database files. Both Portal and Orchestrator nodes communicate with this shared instance.
- **Shared Host Volume Binding**: Mapped to `ENV_DATA_ROOT`. This directory hosts the reports, snapshots, datasets, maps, and the `.portal-keys` Data Protection key ring. Since all scaled Portal containers mount this same directory structure, they automatically share the Data Protection keys needed to decrypt and validate session tokens and cookies.
- **Dynamic Load Balancing**: An HAProxy load balancer handles ingress routing on host ports `5000` (Portal) and `5001` (Orchestrator API).
- **Session Affinity**: Because Portal interactive sessions are stored in process-local memory caches, the load balancer routes client requests stickily based on the `ETLSQL_PORTAL_AFFINITY` cookie. Stateless Orchestrator jobs are round-robin balanced.

#### Deploying and Scaling the HA Stack

1. Navigate to the deployment folder:
   ```bash
   cd deploy/docker
   ```

2. Generate your unique environment configuration:
   ```bash
   cp environment-ha.env.example production-ha.env
   # Edit production-ha.env to supply unique JWT secrets, API keys, database credentials, and ports
   ```

3. Spin up the stack with your chosen scale (e.g., 3 Portals and 2 Orchestrators):
   ```bash
   docker compose --env-file production-ha.env -f docker-compose.ha.yml up -d --scale portal=3 --scale orchestrator=2
   ```

To dynamically scale containers up or down, execute the `up` command again with updated `--scale` flags. HAProxy dynamically queries Docker's internal DNS (`127.0.0.11`) to discover new container instances and mark decommissioned instances as down.

---

