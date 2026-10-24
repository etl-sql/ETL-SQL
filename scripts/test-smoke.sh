#!/bin/bash
set -e

LANE="all"
CONFIGURATION="Debug"
NO_RESTORE=false
NO_BUILD=false

# Simple argument parsing
while [[ $# -gt 0 ]]; do
    case $1 in
        --lane)
            LANE="$2"
            shift 2
            ;;
        --configuration|-c)
            CONFIGURATION="$2"
            shift 2
            ;;
        --no-restore)
            NO_RESTORE=true
            shift
            ;;
        --no-build)
            NO_BUILD=true
            shift
            ;;
        *)
            echo "Unknown argument: $1"
            exit 1
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
if [ "$NO_BUILD" = true ]; then
    node "$SCRIPT_DIR/sync-assets.js" -Check
else
    node "$SCRIPT_DIR/sync-assets.js"
fi

if [ "$LANE" = "all" ]; then
    selected_lanes=("core" "security" "reporting" "portal")
else
    selected_lanes=("$LANE")
fi

for lane in "${selected_lanes[@]}"; do
    case "$lane" in
        core)
            label="Core language behavior"
            project_list="tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
            filter_list="Category=Smoke.Core"
            ;;
        security)
            label="Security and path guardrails"
            project_list="tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj;tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj"
            filter_list="Category=Smoke.Security;Category=Smoke.Security"
            ;;
        reporting)
            label="Reporting manifest and runtime behavior"
            project_list="tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
            filter_list="Category=Smoke.Reporting"
            ;;
        portal)
            label="Portal publish, execute, and snapshot basics"
            project_list="tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj"
            filter_list="Category=Smoke.Portal"
            ;;
        *) echo "Unknown lane: $lane"; exit 1 ;;
    esac

    echo -e "\n==> Smoke lane: $label [$lane]"

    # Split projects and filters by semicolon
    IFS=';' read -ra projects <<< "$project_list"
    IFS=';' read -ra filters <<< "$filter_list"

    for i in "${!projects[@]}"; do
        project="${projects[$i]}"
        filter="${filters[$i]}"
        project_path="$REPO_ROOT/$project"

        if [ "$NO_BUILD" = true ]; then
            node "$SCRIPT_DIR/check-built-browser-assets.mjs" --configuration "$CONFIGURATION" --project "$project_path"
        fi
        args=("test" "$project_path" "--configuration" "$CONFIGURATION" "--filter" "$filter" "--logger" "console;verbosity=minimal")
        if [ "$NO_RESTORE" = true ]; then
            args+=("--no-restore")
        fi
        if [ "$NO_BUILD" = true ]; then
            args+=("--no-build")
        fi

        dotnet "${args[@]}"
    done
done
