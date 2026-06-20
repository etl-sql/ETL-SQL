# ETL-SQL deployment templates — departmental isolation

Templates for running **multiple isolated ETL-SQL environments** (dev/test/prod, or separate
departments) side by side, each a complete, independent deployment with its own databases, artifact
storage, keys, service identity, and ports. No application-layer sharing — see the topology and
runbook in [Docs/Operations/Departmental_Isolation.md](../Docs/Operations/Departmental_Isolation.md).

| Directory | Platform | What it provides |
| :--- | :--- | :--- |
| [`docker/`](docker) | Docker Compose | Per-environment stack (own PostgreSQL, databases, artifact root, keys, ports), namespaced by Compose project. |
| [`windows/`](windows) | Windows Services | `Install-Environment.ps1` registers `ETL-SQL-{Portal,Orchestrator}-<env>` under a dedicated service account with ACL-locked, isolated storage and per-service config. |
| [`systemd/`](systemd) | Linux / systemd | Templated `etl-sql-{portal,orchestrator}@<env>` units + `install-environment.sh` (dedicated user, 0700 data root, per-instance env file). |
| [`verify/`](verify) | any | Isolation verifier proving no two environments share a database, root, key ring, port, account, or key. |

## Workflow

1. Pick a short environment id per environment (`dev`, `finance`, `hr-prod`) and a distinct port block.
2. Deploy each environment with the template for your platform, supplying **unique** keys
   (`PORTAL_JWT_SECRET`, `PORTAL_DATASET_KEY`, `ORCH_API_KEY`) and data root per environment.
3. Each installer/template emits a descriptor file (`<root>/<env>.env`); run the verifier over all of
   them and resolve any reported overlap before serving traffic.

Every template keeps the standalone single-node defaults (SQLite + local storage) and switches to
PostgreSQL + shared artifact roots only when configured for Practical High Availability — the
isolation boundary is always **between environments**, never within one.
