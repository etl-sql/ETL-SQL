#!/bin/bash
# test-all-samples.sh - Run all ETL-SQL sample scripts and report pass/fail.
# Usage: ./scripts/test-all-samples.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
SAMPLES_DIR="$ROOT/samples"
PROJECT_PATH="$ROOT/src/ETL-SQL.App"

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
        performance|portal|orchestrator) return 1 ;;
        *) return 0 ;;
    esac
}

TOTAL=0; PASSED=0; FAILED=0; SKIPPED=0
FAILED_NAMES=()

echo "======================================================="
echo " ETL-SQL SAMPLE VALIDATOR STARTING..."

mapfile -t SCRIPTS < <(find "$SAMPLES_DIR" -type f \( -name "*.etlsql" -o -name "*.rptsql" \) | sort)
TOTAL="${#SCRIPTS[@]}"
echo " Found $TOTAL scripts to validate in '$SAMPLES_DIR'."
echo "======================================================="
echo ""

for SCRIPT_FILE in "${SCRIPTS[@]}"; do
    SCRIPT_NAME="$(basename "$SCRIPT_FILE")"

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

    printf "Starting: %s ... " "$SCRIPT_NAME"

    OUTPUT=$(timeout 180 dotnet run --no-build --project "$PROJECT_PATH" -- run "$SCRIPT_FILE" --silent 2>&1) && EXIT_CODE=$? || EXIT_CODE=$?

    if [[ "$OUTPUT" =~ "CRITICAL FAILURE" || "$OUTPUT" =~ "Unhandled exception" ]]; then
        EXIT_CODE=1
    fi

    if [[ "$EXIT_CODE" -eq 0 ]]; then
        echo "PASSED"
        PASSED=$((PASSED + 1))
    else
        echo "FAILED"
        FAILED=$((FAILED + 1))
        FAILED_NAMES+=("$SCRIPT_NAME|$EXIT_CODE|$(echo "$OUTPUT" | head -6 | tr '\n' '|')")
    fi
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
