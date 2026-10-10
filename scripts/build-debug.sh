#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
EXT_DIR="$ROOT_DIR/src/etl-sql-vscode"
UI_DIR="$EXT_DIR/ui"

SKIP_TESTS=false
for arg in "$@"; do
    if [ "$arg" == "--skip-tests" ] || [ "$arg" == "-SkipTests" ]; then
        SKIP_TESTS=true
    fi
done

errors=()

step() {
    local label="$1"
    shift
    echo -e "\n==> $label"
    if "$@"; then
        echo -e "OK: $label"
    else
        echo -e "FAILED: $label"
        errors+=("$label")
    fi
}

node "$ROOT_DIR/scripts/sync-assets.js"
step ".NET build" dotnet build "$ROOT_DIR/ETL-SQL.slnx" --nologo -v minimal
step "UI build  (vite)" npm --prefix "$UI_DIR" run build --silent
step "Extension compile  (tsc)" npm --prefix "$EXT_DIR" run compile --silent

if [ "$SKIP_TESTS" = false ]; then
    step "Extension unit tests  (vitest)" npm --prefix "$EXT_DIR" run test:unit
fi

echo ""
if [ ${#errors[@]} -eq 0 ]; then
    echo "All steps passed. Ready to debug."
else
    echo "Failed steps: ${errors[*]}"
    exit 1
fi
