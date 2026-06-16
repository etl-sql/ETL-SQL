#!/bin/bash
# publish_vsix.sh - Package the VS Code extension VSIX for a specific platform target.
# Usage: ./scripts/publish_vsix.sh --platform <rid> --bin-dir <path>
#   rid:     win-x64 | linux-x64 | osx-x64 | osx-arm64

set -e

PLATFORM=""
BIN_SOURCE_DIR=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --platform|-p) PLATFORM="$2"; shift 2 ;;
        --bin-dir|-b)  BIN_SOURCE_DIR="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

if [[ -z "$PLATFORM" || -z "$BIN_SOURCE_DIR" ]]; then
    echo "Usage: $0 --platform <rid> --bin-dir <path>"
    echo "  rid: win-x64 | linux-x64 | osx-x64 | osx-arm64"
    exit 1
fi

declare -A VSIX_TARGET_MAP=(
    ["win-x64"]="win32-x64"
    ["linux-x64"]="linux-x64"
    ["osx-x64"]="darwin-x64"
    ["osx-arm64"]="darwin-arm64"
)

VSIX_TARGET="${VSIX_TARGET_MAP[$PLATFORM]:-}"
if [[ -z "$VSIX_TARGET" ]]; then
    echo "ERROR: Unsupported platform: $PLATFORM"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXTENSION_DIR="$(dirname "$SCRIPT_DIR")/src/etl-sql-vscode"
BUNDLED_BIN_DIR="$EXTENSION_DIR/bin"

echo "Packaging VSIX for $VSIX_TARGET..."

# 1. Prepare bin folder
rm -rf "$BUNDLED_BIN_DIR"
mkdir -p "$BUNDLED_BIN_DIR"

# 2. Copy the 3 required executables
EXE_SUFFIX=""
[[ "$PLATFORM" == "win-x64" ]] && EXE_SUFFIX=".exe"

for BIN in "ETL-SQL" "ETL-SQL-LSP" "ETL-SQL-Report"; do
    SRC="$BIN_SOURCE_DIR/${BIN}${EXE_SUFFIX}"
    if [[ -f "$SRC" ]]; then
        echo "  Bundling ${BIN}${EXE_SUFFIX}"
        cp "$SRC" "$BUNDLED_BIN_DIR/"
    else
        echo "  WARNING: Binary not found: $SRC"
    fi
done

# 3. Build and package
cd "$EXTENSION_DIR"
echo "  Compiling extension..."
npm install --no-audit --no-fund --legacy-peer-deps > /dev/null
npm run compile > /dev/null

echo "  Running vsce package..."
VSIX_FILE="etl-sql-vscode-${VSIX_TARGET}.vsix"
npx @vscode/vsce package --target "$VSIX_TARGET" --out "$VSIX_FILE" > /dev/null

if [[ -f "$EXTENSION_DIR/$VSIX_FILE" ]]; then
    echo "  VSIX created: $EXTENSION_DIR/$VSIX_FILE"
else
    echo "ERROR: Failed to create VSIX."
    rm -rf "$BUNDLED_BIN_DIR"
    exit 1
fi

# 4. Clean up bundled binaries
rm -rf "$BUNDLED_BIN_DIR"
