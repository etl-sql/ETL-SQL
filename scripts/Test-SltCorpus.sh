#!/bin/bash
set -e

CORPUS_ONLY=false
LABEL=""
BUILD=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --corpus-only)
            CORPUS_ONLY=true
            shift
            ;;
        --label)
            LABEL="$2"
            shift 2
            ;;
        --build)
            BUILD=true
            shift
            ;;
        *)
            echo "Unknown argument: $1"
            exit 1
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_ROOT="$(dirname "$SCRIPT_DIR")"

export ETL_SQL_RUN_SLT="1"

STAMP=$(date +'%Y%m%d_%H%M%S')
if [ -n "$LABEL" ]; then
    DIR_NAME="${STAMP}_${LABEL}"
else
    DIR_NAME="${STAMP}"
fi

OUT_DIR="$SOLUTION_ROOT/slt_results/$DIR_NAME"
mkdir -p "$OUT_DIR"

LOG_PATH="$OUT_DIR/console_output.log"

echo "======================================================="
echo " ETL-SQL SLT RUNNER (BASH)"
echo " Results : $OUT_DIR"
if [ "$CORPUS_ONLY" = true ]; then
    echo " Mode    : Corpus only (select1-select5)"
else
    echo " Mode    : Full SLT suite"
fi
echo " Log     : $LOG_PATH"
echo "======================================================="
echo ""

if [ "$CORPUS_ONLY" = true ]; then
    TEST_FILTER="Category=SLT&FullyQualifiedName~corpus"
else
    TEST_FILTER="Category=SLT"
fi

dotnet_args=("test" "$SOLUTION_ROOT/ETL-SQL.slnx" "--filter" "$TEST_FILTER" "--logger" "trx;LogFileName=slt_results.trx" "--results-directory" "$OUT_DIR")
if [ "$BUILD" = false ]; then
    dotnet_args+=("--no-build")
fi

# Run and tee to log file
dotnet "${dotnet_args[@]}" 2>&1 | tee "$LOG_PATH"

# Update latest pointer
echo "$OUT_DIR" > "$SOLUTION_ROOT/slt_results/latest.txt"

echo ""
echo "======================================================="
echo " Run ./scripts/Parse-SltResults.ps1 (or bash equivalent if ported) for a summary."
echo "======================================================="
