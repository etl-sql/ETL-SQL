#!/usr/bin/env bash
# Installs an isolated ETL-SQL environment (Portal + Orchestrator) as systemd template instances with
# their own user, data root, config, key ring, keys, and ports. Run as root.
#
#   sudo ./install-environment.sh --env finance --port-base 5010 \
#        --jwt-secret "$JWT" --dataset-key "$DSK" --orch-api-key "$OAK"
#
# Add --portal-db / --orch-db (PostgreSQL connection strings) for HA; otherwise per-env SQLite is used.
# See Docs/Operations/Departmental_Isolation.md.
set -euo pipefail

ENV_ID="" ; PORT_BASE="5000" ; JWT="" ; DSK="" ; OAK="" ; PORTAL_DB="" ; ORCH_DB=""
INSTALL_ROOT="/srv/etl-sql" ; BIN="/usr/lib/etl-sql/bin"
UNIT_SRC="$(cd "$(dirname "$0")" && pwd)"

while [ $# -gt 0 ]; do
  case "$1" in
    --env)          ENV_ID="$2"; shift 2 ;;
    --port-base)    PORT_BASE="$2"; shift 2 ;;
    --jwt-secret)   JWT="$2"; shift 2 ;;
    --dataset-key)  DSK="$2"; shift 2 ;;
    --orch-api-key) OAK="$2"; shift 2 ;;
    --portal-db)    PORTAL_DB="$2"; shift 2 ;;
    --orch-db)      ORCH_DB="$2"; shift 2 ;;
    --install-root) INSTALL_ROOT="$2"; shift 2 ;;
    --bin)          BIN="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ "$(id -u)" -eq 0 ] || { echo "Must run as root." >&2; exit 1; }
echo "$ENV_ID" | grep -Eq '^[a-z0-9][a-z0-9-]{0,30}$' || { echo "Invalid --env (lowercase/dns-safe)." >&2; exit 2; }
[ -n "$JWT" ] && [ -n "$DSK" ] && [ -n "$OAK" ] || { echo "--jwt-secret, --dataset-key, --orch-api-key are required." >&2; exit 2; }

USER_NAME="etlsql-${ENV_ID}"
ENV_ROOT="${INSTALL_ROOT}/${ENV_ID}"
DATA_DIR="${ENV_ROOT}/data"
KEY_RING="${DATA_DIR}/.portal-keys"
PORT_PORTAL=$((PORT_BASE + 0))
PORT_ORCH=$((PORT_BASE + 1))

# 1. Dedicated, no-login system account per environment.
if ! getent group  "$USER_NAME" >/dev/null; then groupadd --system "$USER_NAME"; fi
if ! getent passwd "$USER_NAME" >/dev/null; then
  useradd --system --gid "$USER_NAME" --home-dir "$ENV_ROOT" --shell /usr/sbin/nologin "$USER_NAME"
fi

# 2. Per-environment directory tree, owned by the env account; root is 0700 so no other env user
#    (or other unprivileged user) can traverse into this environment's data.
mkdir -p "$ENV_ROOT" "$DATA_DIR" "$KEY_RING" \
         "$ENV_ROOT/Reports" "$ENV_ROOT/Snapshots" "$ENV_ROOT/datasets" "$ENV_ROOT/maps" "$ENV_ROOT/logs"
chown -R "$USER_NAME:$USER_NAME" "$ENV_ROOT"
chmod 0700 "$ENV_ROOT"
chmod 0700 "$KEY_RING"

# 3. systemd EnvironmentFile (app config, contains secrets) — root-owned, group-readable by the env
#    group only.
mkdir -p /etc/etl-sql
ENV_FILE="/etc/etl-sql/${ENV_ID}.env"
{
  echo "ASPNETCORE_URLS=http://+:${PORT_PORTAL}"
  echo "Portal__ScriptRootPath=${ENV_ROOT}/Reports"
  echo "Portal__SnapshotDirectory=${ENV_ROOT}/Snapshots"
  echo "Portal__DatasetRootPath=${ENV_ROOT}/datasets"
  echo "Portal__MapRootPath=${ENV_ROOT}/maps"
  echo "Portal__Storage__KeyRingPath=${KEY_RING}"
  echo "Portal__Jwt__Secret=${JWT}"
  echo "Portal__Dataset__AtRestKey=${DSK}"
  echo "Portal__Orchestrator__ApiUrl=http://localhost:${PORT_ORCH}"
  echo "Portal__Orchestrator__ApiKey=${OAK}"
  echo "Orchestrator__ApiKey=${OAK}"
  if [ -n "$PORTAL_DB" ] && [ -n "$ORCH_DB" ]; then
    echo "Portal__Database__Provider=Postgres"
    echo "Portal__Database__ConnectionString=${PORTAL_DB}"
    echo "Orchestrator__Database__Provider=Postgres"
    echo "Orchestrator__Database__ConnectionString=${ORCH_DB}"
  else
    echo "Portal__DatabasePath=${DATA_DIR}/portal.db"
    echo "Orchestrator__Database__Provider=Sqlite"
    echo "Orchestrator__DatabasePath=${DATA_DIR}/etlsql.db"
  fi
} > "$ENV_FILE"
chown "root:$USER_NAME" "$ENV_FILE"
chmod 0640 "$ENV_FILE"

# 4. Canonical descriptor for the isolation verifier (raw values; owned by the env account, 0600).
DESC="${ENV_ROOT}/${ENV_ID}.env"
{
  echo "ETLSQL_ENV=${ENV_ID}"
  echo "SERVICE_ACCOUNT=${USER_NAME}"
  echo "ENV_DATA_ROOT=${ENV_ROOT}"
  echo "KEY_RING_PATH=${KEY_RING}"
  echo "PORT_PORTAL=${PORT_PORTAL}"
  echo "PORT_ORCH=${PORT_ORCH}"
  echo "PORTAL_JWT_SECRET=${JWT}"
  echo "PORTAL_DATASET_KEY=${DSK}"
  echo "ORCH_API_KEY=${OAK}"
  echo "PORTAL_DB=${PORTAL_DB:-${DATA_DIR}/portal.db}"
  echo "ORCH_DB=${ORCH_DB:-${DATA_DIR}/etlsql.db}"
} > "$DESC"
chown "$USER_NAME:$USER_NAME" "$DESC"
chmod 0600 "$DESC"

# 5. Install the templated units (idempotent) and enable this environment's instances.
for unit in etl-sql-orchestrator@.service etl-sql-portal@.service; do
  install -m 0644 "${UNIT_SRC}/${unit}" "/etc/systemd/system/${unit}"
done
systemctl daemon-reload
systemctl enable --now "etl-sql-orchestrator@${ENV_ID}.service" "etl-sql-portal@${ENV_ID}.service"

echo "Environment '${ENV_ID}' installed under ${ENV_ROOT} (ports ${PORT_PORTAL}/${PORT_ORCH})."
echo "Descriptor: ${DESC}"
echo "Verify isolation:  sudo ${UNIT_SRC}/../verify/verify-isolation.sh ${INSTALL_ROOT}/*/*.env"
