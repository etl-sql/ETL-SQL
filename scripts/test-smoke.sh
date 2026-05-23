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

# Define lanes and their associated test project and category filters
declare -A lane_labels
declare -A lane_projects
declare -A lane_filters

lane_labels[core]="Core language behavior"
lane_projects[core]="tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
lane_filters[core]="Category=Smoke.Core"

lane_labels[security]="Security and path guardrails"
lane_projects[security]="tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj;tests/ETL-SQL.ReportPortal.Tests/ETL-SQL.ReportPortal.Tests.csproj"
lane_filters[security]="Category=Smoke.Security;Category=Smoke.Security"

lane_labels[reporting]="Reporting manifest and runtime behavior"
lane_projects[reporting]="tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj"
lane_filters[reporting]="Category=Smoke.Reporting"

lane_labels[portal]="Report Portal publish, execute, and snapshot basics"
lane_projects[portal]="tests/ETL-SQL.ReportPortal.Tests/ETL-SQL.ReportPortal.Tests.csproj"
lane_filters[portal]="Category=Smoke.Portal"

if [ "$LANE" = "all" ]; then
    selected_lanes=("core" "security" "reporting" "portal")
else
    selected_lanes=("$LANE")
fi

for lane in "${selected_lanes[@]}"; do
    if [ -z "${lane_labels[$lane]}" ]; then
        echo "Unknown lane: $lane"
        exit 1
    fi

    echo -e "\n==> Smoke lane: ${lane_labels[$lane]} [$lane]"

    # Split projects and filters by semicolon
    IFS=';' read -ra projects <<< "${lane_projects[$lane]}"
    IFS=';' read -ra filters <<< "${lane_filters[$lane]}"

    for i in "${!projects[@]}"; do
        project="${projects[$i]}"
        filter="${filters[$i]}"
        project_path="$REPO_ROOT/$project"

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
