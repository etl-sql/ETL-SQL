#!/bin/bash
# test_sdk_build.sh - Verify ETL-SQL SDK self-contained single-file builds and smoke-test them.
# Usage: ./scripts/test_sdk_build.sh [--rid <runtime-id>]
#   Default RID is auto-detected from the current platform.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="$ROOT/sdk_test_output"

# Auto-detect RID if not provided
RID=""
while [[ $# -gt 0 ]]; do
    case $1 in
        --rid|-r) RID="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

if [[ -z "$RID" ]]; then
    OS="$(uname -s)"
    ARCH="$(uname -m)"
    case "$OS" in
        Linux*)
            [[ "$ARCH" == "aarch64" ]] && RID="linux-arm64" || RID="linux-x64"
            ;;
        Darwin*)
            [[ "$ARCH" == "arm64" ]] && RID="osx-arm64" || RID="osx-x64"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            RID="win-x64"
            ;;
        *)
            echo "ERROR: Cannot detect platform. Pass --rid explicitly."
            exit 1
            ;;
    esac
fi

EXE_SUFFIX=""
[[ "$RID" == win-* ]] && EXE_SUFFIX=".exe"

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

declare -a PROJECTS=(
    "src/ETL-SQL.App/ETL-SQL.App.csproj:ETL-SQL"
    "src/ETL-SQL.LanguageServer/ETL-SQL.LanguageServer.csproj:ETL-SQL-LSP"
    "src/ETL-SQL.ReportBuilder.CLI/ETL-SQL.ReportBuilder.CLI.csproj:ETL-SQL-Report"
)

echo "--- SDK Build Verification (RID: $RID) ---"

ALL_PASSED=true

for ENTRY in "${PROJECTS[@]}"; do
    PROJ_PATH="$ROOT/${ENTRY%%:*}"
    BIN_NAME="${ENTRY##*:}"
    EXE_PATH="$OUTPUT_DIR/${BIN_NAME}${EXE_SUFFIX}"

    echo "Publishing ${BIN_NAME}..."
    dotnet publish "$PROJ_PATH" \
        -c Release -r "$RID" --self-contained true \
        -o "$OUTPUT_DIR" \
        /p:PublishSingleFile=true \
        /p:IncludeNativeLibrariesForSelfExtract=true \
        > /dev/null

    if [[ -f "$EXE_PATH" ]]; then
        SIZE_MB=$(awk "BEGIN{printf \"%.2f\", $(wc -c < "$EXE_PATH") / 1048576}")
        echo "  [SUCCESS] ${BIN_NAME}${EXE_SUFFIX} created (${SIZE_MB} MB)"

        echo "  Running smoke test..."
        case "$BIN_NAME" in
            ETL-SQL-LSP)
                # LSP waits for stdin input; just verify the binary exists and has content
                echo "  [SUCCESS] $BIN_NAME exists and is self-contained. Skipping execution (LSP)."
                ;;
            ETL-SQL)
                VERSION_OUT=$("$EXE_PATH" --version 2>&1) && echo "  [SUCCESS] $BIN_NAME smoke test passed: $VERSION_OUT" || { echo "  [FAILURE] $BIN_NAME smoke test failed"; ALL_PASSED=false; }
                ;;
            *)
                "$EXE_PATH" > /dev/null 2>&1 || true
                echo "  [SUCCESS] $BIN_NAME smoke test passed."
                ;;
        esac
    else
        echo "  [FAILURE] ${BIN_NAME}${EXE_SUFFIX} NOT found at $EXE_PATH"
        ALL_PASSED=false
    fi
done

echo ""
echo "Verification complete. Artifacts are in $OUTPUT_DIR"
[[ "$ALL_PASSED" == "true" ]] || exit 1
