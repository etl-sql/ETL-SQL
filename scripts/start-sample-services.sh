#!/bin/bash
# start-sample-services.sh - Start the Portal (5000) and Orchestrator (5001) from this checkout so
# the samples tagged `@requires: portal` and `@requires: orchestrator` can run.
# Usage: bash scripts/start-sample-services.sh <log-directory> [Debug|Release]
#
# Both services run from source, with throwaway state, on loopback, and with the development
# credentials the samples carry. They are left running for the caller (a CI job ends them with the
# runner; locally, stop the two PIDs written to <log-directory>/pids).

set -euo pipefail

if [[ $# -lt 1 ]]; then
    echo "Usage: bash scripts/start-sample-services.sh <log-directory> [Debug|Release]" >&2
    exit 1
fi
CONFIGURATION="${2:-Debug}"
if [[ "$CONFIGURATION" != Debug && "$CONFIGURATION" != Release ]]; then
    echo "Configuration must be Debug or Release." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
mkdir -p "$1"
LOG_DIR="$(cd "$1" && pwd)"
STATE_DIR="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/etlsql-sample-services.XXXXXX")"
PORTAL_PROJECT="$ROOT/src/ETL-SQL.Portal"
ORCHESTRATOR_PROJECT="$ROOT/src/ETL-SQL.Orchestrator.Service"

for PROJECT in "$ORCHESTRATOR_PROJECT" "$PORTAL_PROJECT"; do
    NAME="$(basename "$PROJECT")"
    echo "Building $NAME ($CONFIGURATION)..."
    if ! timeout 600 dotnet build "$PROJECT" --configuration "$CONFIGURATION" --nologo -v quiet > "$LOG_DIR/build-$NAME.log" 2>&1; then
        tail -40 "$LOG_DIR/build-$NAME.log" >&2
        echo "Building $NAME failed. Complete output: $LOG_DIR/build-$NAME.log" >&2
        exit 1
    fi
done

# --no-launch-profile: the launch profiles name other ports and would override ASPNETCORE_URLS.
ASPNETCORE_URLS="http://127.0.0.1:5001" \
Orchestrator__ApiKey='LocalDevApiKey_32CharsMinimumRequiredForSecurity!' \
Orchestrator__IdentitySigningSecret='LocalDevIdentitySigningSecret_32CharsMinimum!' \
Orchestrator__RequireFederatedIdentity=false \
Orchestrator__DatabasePath="$STATE_DIR/orchestrator.db" \
Jobs__UseProcessSpawning=false \
Session__Root="$STATE_DIR/orchestrator-sessions" \
    nohup dotnet run --no-build --no-launch-profile --configuration "$CONFIGURATION" --project "$ORCHESTRATOR_PROJECT" \
    > "$LOG_DIR/orchestrator.log" 2>&1 &
ORCHESTRATOR_PID=$!

ASPNETCORE_URLS="http://127.0.0.1:5000" \
Portal__ScriptRootPath="$ROOT/samples" \
Portal__DatabasePath="$STATE_DIR/portal.db" \
Portal__SnapshotDirectory="$STATE_DIR/snapshots" \
Portal__DatasetRootPath="$STATE_DIR/datasets" \
Portal__Jwt__Secret='LocalDevelopmentSecretKeyForPortal_32CharsMinimum!' \
Portal__Dataset__AtRestKey='c29tZS1zZWN1cmUtYmFzZTY0LWF0LXJlc3Qta2V5LTMyLWJ5dGVzLSE=' \
Portal__FirstRun__AdminUsername=admin \
Portal__FirstRun__AdminPassword='Admin12345!Password' \
Portal__FirstRun__MustChangePassword=false \
Portal__Studio__Mode=SourceControlled \
Portal__Studio__RoleCapabilities__Admin__0=StudioAccess \
Portal__Studio__RoleCapabilities__Admin__1=ReportPublish \
    nohup dotnet run --no-build --no-launch-profile --configuration "$CONFIGURATION" --project "$PORTAL_PROJECT" \
    > "$LOG_DIR/portal.log" 2>&1 &
PORTAL_PID=$!

printf '%s\n%s\n' "$ORCHESTRATOR_PID" "$PORTAL_PID" > "$LOG_DIR/pids"

healthy() {
    [[ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$1/health" || true)" == 200 ]]
}

for ATTEMPT in $(seq 1 90); do
    if healthy http://127.0.0.1:5000 && healthy http://127.0.0.1:5001; then
        echo "Portal (5000) and Orchestrator (5001) are healthy."
        exit 0
    fi
    for PID in "$ORCHESTRATOR_PID" "$PORTAL_PID"; do
        if ! kill -0 "$PID" 2>/dev/null; then
            break 2
        fi
    done
    sleep 2
done

for NAME in orchestrator portal; do
    echo "--- $NAME.log" >&2
    tail -40 "$LOG_DIR/$NAME.log" >&2 || true
done
kill "$ORCHESTRATOR_PID" "$PORTAL_PID" 2>/dev/null || true
echo "The Portal and Orchestrator did not both become healthy. Logs: $LOG_DIR" >&2
exit 1
