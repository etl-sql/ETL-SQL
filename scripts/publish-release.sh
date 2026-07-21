#!/usr/bin/env bash
# publish-release.sh — bash wrapper invoking publish-release.ps1 via pwsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PWSH="$(command -v pwsh || command -v powershell || true)"

if [[ -z "$PWSH" ]]; then
    echo "PowerShell 7+ (pwsh) is required to run publish-release.ps1." >&2
    exit 1
fi

exec "$PWSH" -NoProfile -File "$SCRIPT_DIR/publish-release.ps1" "$@"
