#!/usr/bin/env bash
# Proves a set of ETL-SQL environments are isolated: no two environments share a database target,
# artifact root, Data Protection key ring, port, service account, or encryption key.
#
#   ./verify-isolation.sh /srv/etl-sql/*/*.env
#   ./verify-isolation.sh ./finance.env ./hr.env
#
# Reads one descriptor per environment (KEY=VALUE; installers emit <root>/<env>.env; Docker env files
# work too). Environments are grouped by ETLSQL_ENV so HA-node descriptors for the same environment
# are not flagged against each other. Exit 0 = isolated, 1 = overlap(s), 2 = usage error.
set -euo pipefail

UNIQUE_KEYS="COMPOSE_PROJECT_NAME ENV_DATA_ROOT KEY_RING_PATH SERVICE_ACCOUNT PORT_PORTAL PORT_ORCH PORT_PG PORTAL_JWT_SECRET PORTAL_DATASET_KEY ORCH_API_KEY PORTAL_DB ORCH_DB"

files=("$@")
[ "${#files[@]}" -ge 1 ] || { echo "Usage: verify-isolation.sh <descriptor.env> [more.env ...]" >&2; exit 2; }

getval() { grep -E "^$2=" "$1" 2>/dev/null | head -n1 | cut -d= -f2- || true; }
getid()  { local id; id="$(getval "$1" ETLSQL_ENV)"; [ -n "$id" ] && echo "$id" || basename "$1" .env; }

is_secret() { case "$1" in PORTAL_JWT_SECRET|PORTAL_DATASET_KEY|ORCH_API_KEY) return 0;; *) return 1;; esac; }

violations=0
for key in $UNIQUE_KEYS; do
  declare -A seen=()
  for f in "${files[@]}"; do
    [ -f "$f" ] || continue
    v="$(getval "$f" "$key")"; [ -n "$v" ] || continue
    id="$(getid "$f")"
    case " ${seen[$v]:-} " in *" $id "*) ;; *) seen[$v]="${seen[$v]:-} $id" ;; esac
  done
  for v in "${!seen[@]}"; do
    # shellcheck disable=SC2086
    set -- ${seen[$v]}
    if [ "$#" -gt 1 ]; then
      if is_secret "$key"; then shown="<shared value>"; else shown="$v"; fi
      ids="$(echo "${seen[$v]}" | tr -s ' ' | sed 's/^ //')"
      echo "  - Shared $key ($shown) across environments: $ids" >&2
      violations=$((violations + 1))
    fi
  done
  unset seen
done

if [ "$violations" -gt 0 ]; then
  echo "ISOLATION VIOLATIONS: $violations" >&2
  exit 1
fi
echo "OK: environments are isolated (no shared databases, roots, key rings, ports, accounts, or keys)."
exit 0
