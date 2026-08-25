# systemd Deployment Templates

This directory contains Linux `systemd` templates for isolated ETL-SQL environments. Each environment gets separate Portal and Orchestrator service instances, a dedicated service user, a private data root, per-instance environment configuration, and independently managed ports and keys.

## Files

| File | Purpose |
| :--- | :--- |
| `etl-sql-portal@.service` | Templated Portal unit. Run as `etl-sql-portal@<env>.service`. |
| `etl-sql-orchestrator@.service` | Templated Orchestrator unit. Run as `etl-sql-orchestrator@<env>.service`. |
| `install-environment.sh` | Installer that creates the environment user, data directories, environment descriptor, and service registrations. |

## Usage

Run the installer once per environment with a short environment id such as `dev`, `finance`, or `hr-prod`. Use unique ports, storage roots, service identities, JWT secrets, dataset keys, and Orchestrator API keys for every environment.

```bash
sudo ./install-environment.sh \
  --env finance \
  --port-base 5010 \
  --jwt-secret "$PORTAL_JWT_SECRET" \
  --dataset-key "$PORTAL_DATASET_KEY" \
  --orch-api-key "$ORCH_API_KEY"

sudo systemctl enable --now etl-sql-portal@finance.service
sudo systemctl enable --now etl-sql-orchestrator@finance.service
```

After installing more than one environment, verify isolation:

```bash
../verify/verify-isolation.sh /srv/etl-sql/*/*.env
```

See [`../README.md`](../README.md) for the platform overview and [`../../docs/architecture/decisions/Departmental_Isolation.md`](../../docs/architecture/decisions/departmental-isolation.md) for the full runbook.
