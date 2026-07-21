#!/bin/bash
# set-version.sh - Update the ETL-SQL version across all canonical locations.
# Usage: ./scripts/set-version.sh <version>
#   version: Target version in Major.Minor.Patch format, e.g. 0.9.0
#
# NOTE: On Windows, use Set-Version.ps1 instead. Running this script via
# Git Bash on Windows will strip CRLF line endings from the files it touches.
# That is harmless (git restores CRLF on checkout) but produces noisy diffs.

set -e

usage() {
    echo "Usage: $0 <version>"
    echo "  version: e.g. 0.9.0"
    exit 1
}

VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then usage; fi
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: version must be Major.Minor.Patch, got: $VERSION"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"

# Portable in-place sed (macOS requires an empty-string backup argument)
sedi() {
    if [[ "$OSTYPE" == "darwin"* ]]; then
        sed -i '' "$@"
    else
        sed -i "$@"
    fi
}

# Replace pattern in file; report UPDATED or OK
update_file() {
    local rel="$1"
    local pattern="$2"
    local replacement="$3"
    local full="$ROOT/$rel"

    if [[ ! -f "$full" ]]; then
        printf "  SKIP     %s (not found)\n" "$rel"
        return
    fi

    local tmp
    tmp=$(mktemp)
    cp "$full" "$tmp"
    sedi -E "s|$pattern|$replacement|g" "$full"

    if ! cmp -s "$tmp" "$full"; then
        printf "  UPDATED  %s\n" "$rel"
    else
        printf "  OK       %s\n" "$rel"
    fi
    rm "$tmp"
}

# Special handler for package-lock.json: only replace the two root-level
# "version" fields that follow a "name": "etl-sql-vscode" line.
update_lock_file() {
    local rel="src/etl-sql-vscode/package-lock.json"
    local full="$ROOT/$rel"

    if [[ ! -f "$full" ]]; then
        printf "  SKIP     %s (not found)\n" "$rel"
        return
    fi

    local tmp
    tmp=$(mktemp)
    cp "$full" "$tmp"
    # When a line matches the package name, advance to next line and replace version
    sedi -E '/\"name\": \"etl-sql-vscode\"/{n; s/\"version\": \"[0-9]+\.[0-9]+\.[0-9]+\"/\"version\": \"'"$VERSION"'\"/;}' "$full"

    if ! cmp -s "$tmp" "$full"; then
        printf "  UPDATED  %s\n" "$rel"
    else
        printf "  OK       %s\n" "$rel"
    fi
    rm "$tmp"
}

echo ""
echo "======================================================="
echo " ETL-SQL Version Bump -> $VERSION"
echo "======================================================="
echo ""

# .NET version
update_file "Directory.Build.props" \
    "(<VersionPrefix>)[0-9]+\.[0-9]+\.[0-9]+(</VersionPrefix>)" \
    "\1${VERSION}\2"

# VS Code extension manifest
update_file "src/etl-sql-vscode/package.json" \
    "(\"version\": \")[0-9]+\.[0-9]+\.[0-9]+(\")" \
    "\1${VERSION}\2"

# VS Code extension lock file (context-aware)
update_lock_file

# README badge and release example
update_file "README.md" \
    "(ETL--SQL-v)[0-9]+\.[0-9]+\.[0-9]+(-blue)" \
    "\1${VERSION}\2"

update_file "README.md" \
    "(publish the )[0-9]+\.[0-9]+\.[0-9]+( artifacts)" \
    "\1${VERSION}\2"

update_file "README.md" \
    "(Master-Release\.ps1 -Version \")[0-9]+\.[0-9]+\.[0-9]+(\")" \
    "\1${VERSION}\2"

# Release scripts
update_file "scripts/Master-Release.ps1" \
    "(\[string\]\\\$Version = \")[0-9]+\.[0-9]+\.[0-9]+(\")" \
    "\1${VERSION}\2"

update_file "scripts/Master-Release.ps1" \
    "(Master-Release\.ps1 -Version \")[0-9]+\.[0-9]+\.[0-9]+(\")" \
    "\1${VERSION}\2"

update_file "scripts/publish-release.ps1" \
    "(\} else \{ \")[0-9]+\.[0-9]+\.[0-9]+(\" \})" \
    "\1${VERSION}\2"

update_file "scripts/build-msi.ps1" \
    "(\} else \{ \")[0-9]+\.[0-9]+\.[0-9]+(\" \})" \
    "\1${VERSION}\2"

update_file "scripts/build-vsix.ps1" \
    "(\\\$Version = \")[0-9]+\.[0-9]+\.[0-9]+(\")" \
    "\1${VERSION}\2"

update_file "scripts/build-mac-dmg.sh" \
    "(VERSION=\\\$\{1:-\")[0-9]+\.[0-9]+\.[0-9]+(\"\\})" \
    "\1${VERSION}\2"

update_file "scripts/build-linux-packages.sh" \
    "(VERSION=\\\$\{1:-\")[0-9]+\.[0-9]+\.[0-9]+(\"\\})" \
    "\1${VERSION}\2"

# User-facing docs (post-IA-restructure locations under docs/)
update_file "docs/guides/faq.md" \
    "(current release baseline is \*\*v)[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

# Release checklist target-version pointer (two mirrored copies: releases/ and guides/)
update_file "docs/releases/release-checklist.md" \
    "(current target: \*\*)[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

update_file "docs/guides/release-checklist.md" \
    "(current target: \*\*)[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

update_file "docs/guides/migration-guide.md" \
    "(ETL-SQL Migration Guide \(v)[0-9]+\.[0-9]+\.[0-9]+(\))" \
    "\1${VERSION}\2"

update_file "docs/guides/migration-guide.md" \
    "(ETL-SQL v)[0-9]+\.[0-9]+\.[0-9]+( is the current release baseline)" \
    "\1${VERSION}\2"

update_file "docs/guides/QUICKSTART.txt" \
    "(ETL-SQL v)[0-9]+\.[0-9]+\.[0-9]+( Quickstart)" \
    "\1${VERSION}\2"

update_file "docs/reference/performance/performance.md" \
    "(\*\*Applies to ETL-SQL )[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

update_file "docs/administration/platform/installation.md" \
    "(ETL-SQL-Enterprise-v)[0-9]+\.[0-9]+\.[0-9]+(\.msi)" \
    "\1${VERSION}\2"

update_file "docs/administration/platform/installation.md" \
    "(etl-sql_)[0-9]+\.[0-9]+\.[0-9]+(_amd64\.deb)" \
    "\1${VERSION}\2"

# Security policy
update_file "SECURITY.md" \
    "(\*\*Policy Version\*\*: )[0-9]+\.[0-9]+\.[0-9]+" \
    "\1${VERSION}"

# Architecture docs (each carries a per-doc "Applies to" baseline). The index
# README mirrors those baselines in a table, so it is bumped in one pass too.
# NOTE: standards/*.md deliberately pin the version a standard was *established*
# ("Applies to ETL-SQL 0.7.0 — Established with ...") and must NOT be bumped.
update_file "docs/architecture/Connectors.md" \
    "(\*\*Applies to ETL-SQL )[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

update_file "docs/architecture/Orchestrator.md" \
    "(\*\*Applies to ETL-SQL )[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

update_file "docs/architecture/Lineage.md" \
    "(\*\*Applies to ETL-SQL )[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

update_file "docs/architecture/Presentation.md" \
    "(\*\*Applies to ETL-SQL )[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

# Architecture index table rows: "**Applies to ETL-SQL X.Y.Z**" (4 rows)
update_file "docs/architecture/README.md" \
    "(\*\*Applies to ETL-SQL )[0-9]+\.[0-9]+\.[0-9]+(\*\*)" \
    "\1${VERSION}\2"

# Guides index table: migration-guide row mirrors its title + baseline sentence
update_file "docs/guides/README.md" \
    "(ETL-SQL Migration Guide \(v)[0-9]+\.[0-9]+\.[0-9]+(\))" \
    "\1${VERSION}\2"

update_file "docs/guides/README.md" \
    "(ETL-SQL v)[0-9]+\.[0-9]+\.[0-9]+( is the current release baseline)" \
    "\1${VERSION}\2"

echo ""
echo "Done."
echo ""
echo "Next steps:"
echo "  1. Add a ## [$VERSION] entry to CHANGELOG.md"
echo "  2. Commit: git commit -am \"Bump version to $VERSION\""
echo "  3. Tag when ready: git tag v$VERSION && git push origin v$VERSION"
echo ""
