#!/bin/sh
# Runs once, on first initialization of an environment's PostgreSQL volume. The Postgres image
# auto-creates POSTGRES_DB (the Portal database); this adds the separate Orchestrator database so the
# two services never share a database within the environment. Idempotent: skips if it already exists.
set -e

orch_db="${PG_DB_ORCH:-orch}"

exists="$(psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -tAc \
  "SELECT 1 FROM pg_database WHERE datname = '${orch_db}'")"

if [ "$exists" != "1" ]; then
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -c "CREATE DATABASE \"${orch_db}\" OWNER \"$POSTGRES_USER\""
  echo "Created orchestrator database '${orch_db}'."
fi
