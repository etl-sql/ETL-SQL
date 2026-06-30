#!/bin/bash
# test-scale-certification.sh - Run the ETL-SQL scale certification suite and produce reports.
# Usage: ./scripts/test-scale-certification.sh [--tier Smoke|Standard|Stress|Huge|Provider|All]
#                                               [--out-dir <dir>] [--row-count-scale <multiplier>]

set -e

TIER="Smoke"
OUT_DIR="./certification-results"
ROW_COUNT_SCALE=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --tier|-t)            TIER="$2";            shift 2 ;;
        --out-dir|-o)         OUT_DIR="$2";          shift 2 ;;
        --row-count-scale|-s) ROW_COUNT_SCALE="$2";  shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

case "$TIER" in
    Smoke|Standard|Stress|Huge|Provider|All) ;;
    *) echo "ERROR: --tier must be one of: Smoke Standard Stress Huge Provider All"; exit 1 ;;
esac

# Default row count scale per tier
if [[ -z "$ROW_COUNT_SCALE" ]]; then
    case "$TIER" in
        Standard) ROW_COUNT_SCALE="10.0" ;;
        Stress)   ROW_COUNT_SCALE="100.0" ;;
        Huge)     ROW_COUNT_SCALE="1000.0" ;;
        *)        ROW_COUNT_SCALE="1.0" ;;
    esac
    SCALE_SPECIFIED=false
else
    SCALE_SPECIFIED=true
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

echo "======================================================="
echo " ETL-SQL Scale Certification Runner"
echo " Tier: $TIER  |  Row scale: ${ROW_COUNT_SCALE}x"
echo "======================================================="
echo ""

# 1. Build
echo "Building solution..."
dotnet build "$REPO_ROOT/ETL-SQL.slnx" -c Release --no-restore -v quiet
echo ""

# 2. Set environment variables
export CERT_ROW_SCALE="$ROW_COUNT_SCALE"
export CERT_CERTIFICATION_TIER="$( [[ "$TIER" == "All" ]] && echo "" || echo "$TIER" )"
export DOTNET_gcServer=1

if [[ "$TIER" == "Standard" ]]; then
    export CERT_STANDARD_ROW_SCALE="$ROW_COUNT_SCALE"
elif [[ "$TIER" == "Stress" ]]; then
    export CERT_STRESS_ROW_SCALE="$ROW_COUNT_SCALE"
elif [[ "$TIER" == "Huge" ]]; then
    export CERT_HUGE_ROW_SCALE="$ROW_COUNT_SCALE"
elif [[ "$TIER" == "Provider" ]]; then
    export CERT_PROVIDER_ROW_SCALE="$ROW_COUNT_SCALE"
elif [[ "$TIER" == "All" ]]; then
    export CERT_STANDARD_ROW_SCALE="$( [[ "$SCALE_SPECIFIED" == "true" ]] && echo "$ROW_COUNT_SCALE" || echo "10.0" )"
    export CERT_STRESS_ROW_SCALE="$( [[ "$SCALE_SPECIFIED" == "true" ]] && echo "$ROW_COUNT_SCALE" || echo "100.0" )"
    export CERT_PROVIDER_ROW_SCALE="$( [[ "$SCALE_SPECIFIED" == "true" ]] && echo "$ROW_COUNT_SCALE" || echo "1.0" )"
fi

# 3. Run tests
FILTER="$( [[ "$TIER" == "All" ]] && echo "Category=ScaleCertification" || echo "Category=ScaleCertification&Tier=$TIER" )"
mkdir -p "$OUT_DIR"
RAW_LOG="$OUT_DIR/raw-output.txt"

echo "Running certification tests (filter: $FILTER)..."
dotnet test "$REPO_ROOT/ETL-SQL.slnx" \
    --filter "$FILTER" \
    --logger "console;verbosity=detailed" \
    -c Release \
    --no-build \
    2>&1 | tee "$RAW_LOG"
TEST_EXIT=$?

# 4. Parse CERT_METRIC lines
METRICS_FILE="$OUT_DIR/metrics.jsonl"
grep -oP 'CERT_METRIC:\K.+' "$RAW_LOG" > "$METRICS_FILE" 2>/dev/null || true

# 5. Write JSON report (array of metric objects)
JSON_PATH="$OUT_DIR/cert-report.json"
CPU_MODEL=$(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2- | sed 's/^[[:space:]]*//' || true)
TOTAL_RAM_BYTES=$(awk '/MemTotal/ { print $2 * 1024 }' /proc/meminfo 2>/dev/null || echo 0)
RUNTIME_VERSION=$(dotnet --version)
DISK_MODEL=$(lsblk -ndo MODEL "$(df --output=source "$REPO_ROOT" | tail -1)" 2>/dev/null | head -1 | sed 's/^[[:space:]]*//;s/[[:space:]]*$//' || true)
WORKSPACE_FREE_BYTES=$(df -B1 --output=avail "$REPO_ROOT" | tail -1 | tr -d ' ')
{
    echo "{"
    echo "  \"generatedAt\": \"$(date -u +"%Y-%m-%dT%H:%M:%SZ")\","
    echo "  \"tier\": \"$TIER\","
    echo "  \"rowCountScale\": $ROW_COUNT_SCALE,"
    echo "  \"testsPassed\": $( [[ $TEST_EXIT -eq 0 ]] && echo "true" || echo "false" ),"
    echo "  \"hardware\": {"
    echo "    \"machineName\": \"$(hostname)\","
    echo "    \"operatingSystem\": \"$(uname -s)\","
    echo "    \"osVersion\": \"$(uname -r)\","
    echo "    \"processor\": \"$CPU_MODEL\","
    echo "    \"logicalCores\": $(getconf _NPROCESSORS_ONLN),"
    echo "    \"physicalMemoryBytes\": ${TOTAL_RAM_BYTES%.*},"
    echo "    \"diskModel\": \"$DISK_MODEL\","
    echo "    \"workspaceFreeBytes\": ${WORKSPACE_FREE_BYTES:-0},"
    echo "    \"runtimeVersion\": \".NET $RUNTIME_VERSION\","
    echo "    \"buildConfiguration\": \"Release\","
    echo "    \"serverGcRequested\": true"
    echo "  },"
    echo "  \"scenarios\": ["
    FIRST=true
    while IFS= read -r LINE; do
        [[ -z "$LINE" ]] && continue
        [[ "$FIRST" == "true" ]] && FIRST=false || echo ","
        printf '    %s' "$LINE"
    done < "$METRICS_FILE"
    echo ""
    echo "  ]"
    echo "}"
} > "$JSON_PATH"
echo "JSON report: $JSON_PATH"

# 6. Write Markdown report
MD_PATH="$OUT_DIR/cert-report.md"
{
    echo "# ETL-SQL Scale Certification Report"
    echo ""
    echo "Generated: $(date '+%Y-%m-%d %H:%M:%S')  |  Tier: **$TIER**  |  Row scale: **${ROW_COUNT_SCALE}x**"
    echo ""
    echo "## Results"
    echo ""
    echo "| Scenario | Rows | Rows/s | Elapsed (ms) | Spill Write | Peak WS (MB) | Private (MB) | Heap (MB) | Allocated (MB) | CPU % | GC Pause (ms) | Bound (MB) | Pass |"
    echo "| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |"

    METRIC_COUNT=0
    while IFS= read -r LINE; do
        [[ -z "$LINE" ]] && continue
        METRIC_COUNT=$((METRIC_COUNT + 1))
        # Extract fields with sed/grep (no jq required)
        get_field() { echo "$LINE" | grep -oP "\"$1\"\\s*:\\s*\\K[^,}]+\\s*" | tr -d '" \r\n'; }
        SCENARIO=$(get_field scenario)
        ROWS=$(get_field rowCount)
        ELAPSED=$(get_field elapsedMs)
        ROWS_PER_SECOND=$(get_field rowsPerSecond)
        SPILL=$(get_field spillWriteBytes)
        PEAK_WS=$(get_field peakProcessWorkingSetMB)
        PRIVATE_MB=$(get_field peakPrivateBytesMB)
        HEAP_MB=$(get_field peakManagedHeapMB)
        ALLOCATED_MB=$(get_field allocatedMB)
        CPU_PERCENT=$(get_field cpuUtilizationPercent)
        GC_PAUSE=$(get_field gcPauseMs)
        BOUND=$(get_field memoryBoundMB)
        PASSED_VAL=$(get_field passed)
        PASS_LABEL="$( [[ "$PASSED_VAL" == "true" ]] && echo "OK" || echo "FAIL" )"
        echo "| $SCENARIO | $ROWS | $ROWS_PER_SECOND | $ELAPSED | $SPILL | $PEAK_WS | $PRIVATE_MB | $HEAP_MB | $ALLOCATED_MB | $CPU_PERCENT | $GC_PAUSE | $BOUND | $PASS_LABEL |"
    done < "$METRICS_FILE"

    if [[ "$METRIC_COUNT" -eq 0 ]]; then
        echo "| _No metrics collected — check test output_ | | | | | | | | | | | | |"
    fi

    echo ""
    echo "## Environment"
    echo ""
    echo "- OS: $(uname -s) $(uname -r)"
    echo "- CPU: $CPU_MODEL ($(getconf _NPROCESSORS_ONLN) logical cores)"
    echo "- RAM: $(( ${TOTAL_RAM_BYTES%.*} / 1024 / 1024 / 1024 )) GB"
    echo "- Disk: $DISK_MODEL; workspace free $(( ${WORKSPACE_FREE_BYTES:-0} / 1024 / 1024 / 1024 )) GB"
    echo "- Runtime: .NET $RUNTIME_VERSION, Release, server GC requested"

    echo ""
    echo "## Operator Status"
    echo ""
    echo "| Operator | Execution Mode | Scale Tested | Notes |"
    echo "| :--- | :--- | :--- | :--- |"
    echo "| ORDER BY | External Sort (multi-chunk) | 50k rows | ExternalSortChunkSize forced to 5k |"
    echo "| GROUP BY | External Aggregate | 100k rows | OperatorMemoryGrantMB forced to 1 MB |"
    echo "| JOIN (equality) | External Hash Join | 50k rows | JoinSpillThreshold forced to 5k |"
    echo "| SELECT INTO #temp | Temp Table Spill | 50k rows | TempTableSpillThresholdRows forced to 10k |"
    echo "| SELECT (streaming) | Result Cap | 100k rows | MaxLastResultRows cap enforced at 50k |"
    echo "| WINDOW ROW_NUMBER | External Window | 50k rows | WindowSpillThreshold forced to 5k |"
    echo "| CSV ingest | Connector batch read | 50k rows | Row count and checksum certified |"
    echo "| Parquet round trip | Connector batch write/read | 50k rows | Row count and checksum certified |"
    echo "| CREATE DATASET snapshot/reload | Query -> Parquet cache -> reload | 50k rows | Row count and checksum certified after cached reload |"
    echo "| GROUP BY CUBE | External Aggregate grouping-set expansion | 50k rows | Expanded row count, checksum, and spill bytes certified |"
    echo "| Scalar subquery cache | Correlated subquery LRU cache | 50k rows | Row count, checksum, and exact hit/miss counts certified |"
    echo "| Spill cleanup after success | Non-persistent temp-table spill lifecycle | 50k rows | Spill directory removed after evaluator disposal |"
    echo "| Spill cleanup after failure | Non-persistent temp-table spill lifecycle | 50k rows | Forced source failure still removes spill directory after evaluator disposal |"
} > "$MD_PATH"
echo "Markdown report: $MD_PATH"

# 7. Summary
echo ""
echo "======================================================="
PASS_COUNT=0; FAIL_COUNT=0
while IFS= read -r LINE; do
    [[ -z "$LINE" ]] && continue
    PASSED_VAL=$(echo "$LINE" | grep -oP '"passed"\s*:\s*\K(true|false)')
    [[ "$PASSED_VAL" == "true" ]] && PASS_COUNT=$((PASS_COUNT + 1)) || FAIL_COUNT=$((FAIL_COUNT + 1))
done < "$METRICS_FILE"
TOTAL=$((PASS_COUNT + FAIL_COUNT))

if [[ $TEST_EXIT -eq 0 && $FAIL_COUNT -eq 0 ]]; then
    echo " Certification PASSED: $PASS_COUNT/$TOTAL scenarios"
else
    echo " Certification FAILED: $FAIL_COUNT/$TOTAL scenarios failed"
fi
echo "======================================================="

exit $TEST_EXIT
