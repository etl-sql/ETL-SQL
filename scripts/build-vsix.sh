#!/bin/bash
# build-vsix.sh - Build and package the ETL-SQL VS Code extension VSIX.
# Usage: ./scripts/build-vsix.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
EXTENSION_DIR="$ROOT/src/etl-sql-vscode"
RELEASE_DIR="$ROOT/release/vsix"

VERSION=$(jq -r '.version' "$EXTENSION_DIR/package.json" 2>/dev/null || echo "0.0.0")

echo "Building ETL-SQL VS Code Extension v$VERSION..."

# Stop running processes to avoid file locks
echo "  Stopping any running ETL-SQL processes..."
pkill -f "ETL-SQL-LSP" || true
pkill -f "ETL-SQL" || true
pkill -f "ETL-SQL-Report" || true

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

# 3.5 Publish C# Binaries to bundled bin/ directory for a self-contained VSIX
echo "  Publishing self-contained C# binaries..."
VSIX_BIN_DIR="$EXTENSION_DIR/bin"
mkdir -p "$VSIX_BIN_DIR"

# Determine local OS RID for publishing platform-appropriate binaries
OS_NAME="$(uname -s)"
case "$OS_NAME" in
    Darwin*)  RID="osx-arm64" ;;
    Linux*)   RID="linux-x64" ;;
    *)        RID="win-x64" ;;
esac

dotnet publish "$ROOT/src/ETL-SQL.App/ETL-SQL.App.csproj" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$VSIX_BIN_DIR" --nologo > /dev/null
dotnet publish "$ROOT/src/ETL-SQL.LanguageServer/ETL-SQL.LanguageServer.csproj" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$VSIX_BIN_DIR" --nologo > /dev/null
dotnet publish "$ROOT/src/ETL-SQL.ReportBuilder.CLI/ETL-SQL.ReportBuilder.CLI.csproj" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$VSIX_BIN_DIR" --nologo > /dev/null
dotnet publish "$ROOT/src/ETL-SQL.ReportPlayer/ETL-SQL.ReportPlayer.csproj" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$VSIX_BIN_DIR" --nologo > /dev/null

# 4. Package VSIX
echo "  Packaging VSIX..."
npx @vscode/vsce package --out "$RELEASE_DIR" --no-git-tag-version "$VERSION" --allow-missing-repository

echo ""
echo "VSIX ready in $RELEASE_DIR"
