#!/bin/bash
# test-all-samples.sh - Run all ETL-SQL sample scripts and report pass/fail.
# Usage: ./scripts/test-all-samples.sh [passes]
#
# passes (default 1) runs the whole suite that many times. More than one pass proves the samples are
# re-runnable: sample output is gitignored, so a sample that writes to a persistent store passes on
# a clean checkout and fails for anyone who runs it twice. Mirrors Test-AllSamples.ps1 -Passes.

set -e

PASSES="${1:-1}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
SAMPLES_DIR="$ROOT/samples"
PROJECT_PATH="$ROOT/src/ETL-SQL.App"
VALIDATOR_STATE_ROOT="$ROOT/release-validation/sample-validator-$$"
SECURITY_EVENT_OUTBOX_PATH="$VALIDATOR_STATE_ROOT/security-events.db"
SESSION_ROOT="$VALIDATOR_STATE_ROOT/sessions"
ORCHESTRATOR_DATABASE_PATH="$VALIDATOR_STATE_ROOT/orchestrator.db"
mkdir -p "$VALIDATOR_STATE_ROOT"
trap 'rm -rf -- "$VALIDATOR_STATE_ROOT"' EXIT

# Check port availability (returns 0=open, 1=closed)
port_open() {
    local host="$1" port="$2"
    if command -v nc &>/dev/null; then
        nc -z -w1 "$host" "$port" 2>/dev/null
    else
        (echo >/dev/tcp/"$host"/"$port") 2>/dev/null
    fi
}

# Service availability checks (mirrors Test-AllSamples.ps1 skip logic)
service_available() {
    local service="$1"
    case "$service" in
        postgres|postgresql) port_open localhost 5432 ;;
        mssql|sqlserver)     port_open localhost 1433 ;;
        docker)              command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1 ;;
        performance|portal|orchestrator) return 1 ;;
        *) return 0 ;;
    esac
}

TOTAL=0; PASSED=0; FAILED=0; SKIPPED=0
FAILED_NAMES=()

echo "======================================================="
echo " ETL-SQL SAMPLE VALIDATOR STARTING..."

mapfile -t SCRIPTS < <(find "$SAMPLES_DIR" -type f \( -name "*.etlsql" -o -name "*.rptsql" \) | sort)
echo " Found ${#SCRIPTS[@]} scripts to validate in '$SAMPLES_DIR'."
if [[ "$PASSES" -gt 1 ]]; then
    echo " Running $PASSES passes to prove the samples are re-runnable."
fi
echo "======================================================="
echo ""

TOTAL=$(( ${#SCRIPTS[@]} * PASSES ))

for PASS in $(seq 1 "$PASSES"); do
if [[ "$PASSES" -gt 1 ]]; then
    echo ""
    echo "--- Pass $PASS of $PASSES ---"
    echo ""
fi

for SCRIPT_FILE in "${SCRIPTS[@]}"; do
    SCRIPT_NAME="$(basename "$SCRIPT_FILE")"
    if [[ "$PASSES" -gt 1 ]]; then
        SCRIPT_NAME="$SCRIPT_NAME (pass $PASS)"
    fi

    # Check @requires: tag in first 5 lines
    REQUIRES_TAG=$(head -5 "$SCRIPT_FILE" 2>/dev/null | grep -i '@requires:' | head -1 || true)
    if [[ -n "$REQUIRES_TAG" ]]; then
        SERVICE=$(echo "$REQUIRES_TAG" | sed -E 's/.*@requires:[[:space:]]*//' | tr '[:upper:]' '[:lower:]' | tr -d '\r')
        if ! service_available "$SERVICE"; then
            echo "SKIPPED $SCRIPT_NAME ($SERVICE unavailable)"
            SKIPPED=$((SKIPPED + 1))
            continue
        fi
    fi

    EXPECTED_EXIT_TAG=$(head -5 "$SCRIPT_FILE" 2>/dev/null | grep -i '@expected-exit-code:' | head -1 || true)
    EXPECTED_ERROR_TAG=$(head -5 "$SCRIPT_FILE" 2>/dev/null | grep -i '@expected-error:' | head -1 || true)
    EXPECTED_EXIT=""
    EXPECTED_ERROR=""
    if [[ -n "$EXPECTED_EXIT_TAG" ]]; then
        EXPECTED_EXIT=$(echo "$EXPECTED_EXIT_TAG" | sed -E 's/.*@expected-exit-code:[[:space:]]*//' | tr -d '\r')
        EXPECTED_ERROR=$(echo "$EXPECTED_ERROR_TAG" | sed -E 's/.*@expected-error:[[:space:]]*//' | tr -d '\r')
    fi

    printf "Starting: %s ... " "$SCRIPT_NAME"

    OUTPUT=$(ETLSQL_SECURITY_EVENT_OUTBOX_PATH="$SECURITY_EVENT_OUTBOX_PATH" Session__Root="$SESSION_ROOT" Orchestrator__DatabasePath="$ORCHESTRATOR_DATABASE_PATH" timeout 180 dotnet run --no-build --project "$PROJECT_PATH" -- run "$SCRIPT_FILE" --silent 2>&1) && EXIT_CODE=$? || EXIT_CODE=$?

    HAS_INTERNAL_ERROR=false
    if [[ "$OUTPUT" =~ "CRITICAL FAILURE" || "$OUTPUT" =~ "Unhandled exception" ]]; then
        HAS_INTERNAL_ERROR=true
        EXIT_CODE=1
    fi

    EXPECTED_FAILURE_MATCHED=false
    if [[ -n "$EXPECTED_EXIT" && -n "$EXPECTED_ERROR" && "$HAS_INTERNAL_ERROR" == false && "$EXIT_CODE" -eq "$EXPECTED_EXIT" && "$OUTPUT" == *"$EXPECTED_ERROR"* ]]; then
        EXPECTED_FAILURE_MATCHED=true
    fi

    if { [[ -z "$EXPECTED_EXIT" && "$EXIT_CODE" -eq 0 ]]; } || [[ "$EXPECTED_FAILURE_MATCHED" == true ]]; then
        echo "PASSED"
        PASSED=$((PASSED + 1))
    else
        echo "FAILED"
        FAILED=$((FAILED + 1))
        FAILED_NAMES+=("$SCRIPT_NAME|$EXIT_CODE|$(echo "$OUTPUT" | head -6 | tr '\n' '|')")
    fi
done
done

echo ""
echo "======================================================="
echo " VALIDATION SUMMARY"
echo "======================================================="
printf " Total Scripts : %d\n" "$TOTAL"
printf " Passed        : %d\n" "$PASSED"
printf " Skipped       : %d\n" "$SKIPPED"
printf " Failed        : %d\n" "$FAILED"

if [[ "$FAILED" -gt 0 ]]; then
    echo ""
    echo "Failed Scripts Detail:"
    for ENTRY in "${FAILED_NAMES[@]}"; do
        IFS='|' read -r NAME CODE PREVIEW <<< "$ENTRY"
        echo "  - $NAME (Exit Code: $CODE)"
        echo "    Preview: ${PREVIEW:0:200}"
    done
    echo ""
    echo "Recommendation: Run failing scripts individually with the verbose flag to debug."
    exit 1
else
    echo ""
    echo "SUCCESS: All ETL-SQL Samples validated flawlessly!"
fi
echo "======================================================="
