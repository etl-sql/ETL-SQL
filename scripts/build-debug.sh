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
    local action="$2"
    echo -e "\n==> $label"
    if eval "$action"; then
        echo -e "OK: $label"
    else
        echo -e "FAILED: $label"
        errors+=("$label")
    fi
}

step ".NET build" "dotnet build \"$ROOT_DIR/ETL-SQL.slnx\" --nologo -v minimal"
step "UI build  (vite)" "cd \"$UI_DIR\" && npm run build --silent"
step "Extension compile  (tsc)" "cd \"$EXT_DIR\" && npm run compile --silent"

if [ "$SKIP_TESTS" = false ]; then
    step "Extension unit tests  (vitest)" "cd \"$EXT_DIR\" && npm run test:unit"
fi

echo ""
if [ ${#errors[@]} -eq 0 ]; then
    echo "All steps passed. Ready to debug."
else
    echo "Failed steps: ${errors[*]}"
    exit 1
fi
