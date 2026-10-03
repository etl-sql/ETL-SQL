#!/bin/bash
# Run the canonical cross-platform scale runner and preserve its exit status.
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
args=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --tier|-t) args+=("-Tier" "$2"); shift 2 ;;
        --out-dir|-o) args+=("-OutDir" "$2"); shift 2 ;;
        --row-count-scale|-s) args+=("-RowCountScale" "$2"); shift 2 ;;
        --samples) args+=("-Samples" "$2"); shift 2 ;;
        --scenario) args+=("-Scenario" "$2"); shift 2 ;;
        --repository-root) args+=("-RepositoryRoot" "$2"); shift 2 ;;
        --fixture-root) args+=("-FixtureRoot" "$2"); shift 2 ;;
        --skip-build) args+=("-SkipBuild"); shift ;;
        --no-warm-up) args+=("-NoWarmUp"); shift ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

if ! command -v pwsh >/dev/null 2>&1; then
    echo "PowerShell 7 (pwsh) is required for scale certification." >&2
    exit 1
fi
exec pwsh -NoProfile -File "$SCRIPT_DIR/Test-ScaleCertification.ps1" "${args[@]}"
