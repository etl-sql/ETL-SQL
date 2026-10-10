#!/bin/bash
# Node.js 22/npm 10; supports Bash 3 on macOS. All progress goes to stderr.
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec node "$SCRIPT_DIR/package-vsix.mjs" build "$@"
