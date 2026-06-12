#!/bin/bash
# build_vsix.sh - Build and package the ETL-SQL VS Code extension VSIX.
# Usage: ./scripts/build_vsix.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
EXTENSION_DIR="$ROOT/src/etl-sql-vscode"
RELEASE_DIR="$ROOT/release/vsix"

VERSION=$(jq -r '.version' "$EXTENSION_DIR/package.json" 2>/dev/null || echo "0.0.0")

echo "Building ETL-SQL VS Code Extension v$VERSION..."

mkdir -p "$RELEASE_DIR"
cd "$EXTENSION_DIR"

# 1. Install extension dependencies
echo "  Installing extension npm dependencies..."
npm install --no-audit --no-fund --legacy-peer-deps > /dev/null

# 2. Build React UI
echo "  Building React UI..."
cd ui
npm install --no-audit --no-fund --legacy-peer-deps > /dev/null
npm run build > /dev/null
cd ..

# 3. Prep metadata
cp "$ROOT/LICENSE.md" "$EXTENSION_DIR/LICENSE.md"
cp "$ROOT/NOTICE.md" "$EXTENSION_DIR/NOTICE.md"

# 4. Package VSIX
echo "  Packaging VSIX..."
npx @vscode/vsce package --out "$RELEASE_DIR" --no-git-tag-version "$VERSION" --allow-missing-repository

echo ""
echo "VSIX ready in $RELEASE_DIR"
