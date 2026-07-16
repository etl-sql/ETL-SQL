# Docker Compose — isolated departmental environments

Run multiple fully isolated ETL-SQL environments (dev/test/prod or per department) with Docker
Compose. Each environment is a self-contained stack: its own PostgreSQL instance and volume, its own
Portal and Orchestrator databases, its own artifact root, its own keys, and its own port block.
Nothing is shared between environments — see
[Departmental_Isolation.md](../../docs/architecture/decisions/Departmental_Isolation.md).

## Files

| File | Purpose |
| :--- | :--- |
| `docker-compose.environment.yml` | Parameterized stack: PostgreSQL + Orchestrator + Portal, namespaced by `COMPOSE_PROJECT_NAME`. |
| `environment.env.example` | Per-environment settings. Copy once per environment and edit every value. |
| `docker-compose.override.example.yml` | Optional local-source override. Copy to repository-root `docker-compose.override.yml` when you want Docker Compose to build local C# source instead of using registry images. |
| `docker-compose.ha.yml` | High Availability cluster stack: PostgreSQL + load-balanced / scaled Orchestrator and Portal nodes. |
| `environment-ha.env.example` | Per-environment settings for the HA stack. |
| `haproxy.cfg` | HAProxy configuration for cookie-based sticky sessions (Report Portal) and round-robin routing (Orchestrator). |
| `initdb/10-create-orchestrator-db.sh` | First-run hook that creates the separate Orchestrator database in Postgres. |

## Quick start (Standard Isolated Environment)

```bash
cd deploy/docker
cp environment.env.example finance.env
# edit finance.env: set ETLSQL_ENV, COMPOSE_PROJECT_NAME=etlsql-finance, a distinct PORT_* block,
# ENV_DATA_ROOT, and UNIQUE PG_PASSWORD / PORTAL_JWT_SECRET / PORTAL_DATASET_KEY / ORCH_API_KEY.

docker compose --env-file finance.env -f docker-compose.environment.yml up -d
docker compose --env-file finance.env -f docker-compose.environment.yml ps
```

Bring up a second environment by repeating with its own env file and a different `PORT_BASE`:

```bash
cp environment.env.example hr.env   # ETLSQL_ENV=hr, COMPOSE_PROJECT_NAME=etlsql-hr, PORT_PORTAL=5010 …
docker compose --env-file hr.env -f docker-compose.environment.yml up -d
```

## High Availability (HA) Scaling Quick start

To run a multi-node, load-balanced HA setup with a variable number of service replicas:

1. Copy the HA environment example file:
   ```bash
   cp environment-ha.env.example production-ha.env
   ```
2. Edit `production-ha.env` to set unique credentials, shared path roots, and keys.
3. Bring up the stack with any desired number of Report Portal and Orchestrator instances:
   ```bash
   docker compose --env-file production-ha.env -f docker-compose.ha.yml up -d --scale portal=3 --scale orchestrator=2
   ```

### How HA Scaling works
- **Shared State**: PostgreSQL is used as the database provider for both services. All Portal and Orchestrator nodes communicate with the same DB.
- **Shared Storage**: The Report files (`Reports`), Snapshots, datasets, maps, and Data Protection key ring are bind-mounted to a shared directory (`ENV_DATA_ROOT`) on the host. This ensures all scaled containers access the same files.
- **Dynamic Load Balancing**: HAProxy acts as the entry point. It maps port 5000 and 5001. Using Docker's internal DNS (`127.0.0.11`), HAProxy dynamically detects when containers are scaled up or down.
- **Session Affinity**: Interactive Portal sessions are stored process-locally. HAProxy uses the application-provided `ETLSQL_PORTAL_AFFINITY` cookie to route user requests stickily to the same container instance. Stateless Orchestrator requests are round-robin balanced.

## Isolation guarantees

- **Project namespacing** — `COMPOSE_PROJECT_NAME=etlsql-<env>` namespaces containers, the default
  network, and the named `pgdata` volume, so two environments never collide or share storage.
- **Separate databases** — each environment runs its own PostgreSQL with its own credentials and
  volume; within it the Portal and Orchestrator use separate databases.
- **Separate artifact roots** — bind-mounted under the environment's `ENV_DATA_ROOT`.
- **Unique keys** — `PORTAL_JWT_SECRET`, `PORTAL_DATASET_KEY`, and `ORCH_API_KEY` must be unique per
  environment; reusing them across environments breaks the isolation boundary.

After bringing up more than one environment, verify they do not overlap:

```bash
../verify/verify-isolation.sh ./finance.env ./hr.env
```

> Production note: pin `ETLSQL_IMAGE_TAG` to a release, terminate TLS at a reverse proxy or
> load balancer, and place production environments on separate networks rather than only separate
> ports.

