#!/bin/bash
# compare-benchmarks.sh - Compare BenchmarkDotNet JSON results against a baseline.
# Exits 1 if any benchmark regresses by more than the threshold.
# Usage: ./scripts/compare-benchmarks.sh --baseline <file> --current <file> [--threshold <pct>]
# Requires: jq

set -e

BASELINE=""
CURRENT=""
THRESHOLD=15

while [[ $# -gt 0 ]]; do
    case $1 in
        --baseline|-b) BASELINE="$2"; shift 2 ;;
        --current|-c)  CURRENT="$2";  shift 2 ;;
        --threshold|-t) THRESHOLD="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

if [[ -z "$BASELINE" || -z "$CURRENT" ]]; then
    echo "Usage: $0 --baseline <file> --current <file> [--threshold <pct>]"
    exit 1
fi

if ! command -v jq &>/dev/null; then
    echo "ERROR: jq is required. Install it with: apt install jq / brew install jq"
    exit 1
fi

for f in "$BASELINE" "$CURRENT"; do
    if [[ ! -f "$f" ]]; then
        echo "ERROR: File not found: $f"
        exit 1
    fi
done

echo ""
echo "Benchmark Comparison (threshold: ${THRESHOLD}%)"
echo "  Baseline : $BASELINE"
echo "  Current  : $CURRENT"
echo ""

# Build associative arrays: name -> mean (nanoseconds)
declare -A baseline_map
while IFS=$'\t' read -r name mean; do
    baseline_map["$name"]="$mean"
done < <(jq -r '.Benchmarks[] | [.FullName, .Statistics.Mean] | @tsv' "$BASELINE")

FAILURES=0

printf "%-70s %12s %12s %10s %10s\n" "Benchmark" "Baseline(ms)" "Current(ms)" "Change" "Status"
printf "%-70s %12s %12s %10s %10s\n" "$(printf '%0.s-' {1..70})" "$(printf '%0.s-' {1..12})" "$(printf '%0.s-' {1..12})" "$(printf '%0.s-' {1..10})" "$(printf '%0.s-' {1..10})"

while IFS=$'\t' read -r name cur_mean; do
    base_mean="${baseline_map[$name]:-}"

    if [[ -z "$base_mean" ]]; then
        printf "%-70s %12s %12s %10s %10s\n" \
            "${name:0:70}" "N/A (new)" \
            "$(awk "BEGIN{printf \"%.2f\", $cur_mean/1000000}")" \
            "NEW" ""
        continue
    fi

    read -r ratio pct status < <(awk -v cur="$cur_mean" -v base="$base_mean" -v thr="$THRESHOLD" 'BEGIN {
        ratio = cur / base
        pct   = (ratio - 1.0) * 100.0
        status = (ratio > 1.0 + thr/100.0) ? "REGRESSED" : "OK"
        printf "%.6f %.2f %s\n", ratio, pct, status
    }')

    if [[ "$status" == "REGRESSED" ]]; then
        FAILURES=$((FAILURES + 1))
    fi

    printf "%-70s %12s %12s %10s %10s\n" \
        "${name:0:70}" \
        "$(awk "BEGIN{printf \"%.2f\", $base_mean/1000000}")" \
        "$(awk "BEGIN{printf \"%.2f\", $cur_mean/1000000}")" \
        "$(printf '%+.1f%%' "$pct")" \
        "$status"

done < <(jq -r '.Benchmarks[] | [.FullName, .Statistics.Mean] | @tsv' "$CURRENT" | sort)

echo ""
if [[ "$FAILURES" -eq 0 ]]; then
    echo "All benchmarks within ${THRESHOLD}% of baseline. No regressions."
    exit 0
else
    echo "ERROR: $FAILURES benchmark(s) regressed by more than ${THRESHOLD}%."
    exit 1
fi
