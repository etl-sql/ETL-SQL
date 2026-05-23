#!/bin/bash
set -e

LANE="fast"
CONFIGURATION="Debug"
NO_RESTORE=false
NO_BUILD=false
COLLECT_COVERAGE=false
RESULTS_DIRECTORY="coverage"

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
        --collect-coverage)
            COLLECT_COVERAGE=true
            shift
            ;;
        --results-directory)
            RESULTS_DIRECTORY="$2"
            shift 2
            ;;
        *)
            echo "Unknown argument: $1"
            exit 1
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

invoke_dotnet_test() {
    local project="$1"
    local filter="$2"
    local project_path="$REPO_ROOT/$project"

    args=("test" "$project_path" "--configuration" "$CONFIGURATION" "--logger" "console;verbosity=minimal")

    if [ "$NO_RESTORE" = true ]; then
        args+=("--no-restore")
    fi
    if [ "$NO_BUILD" = true ]; then
        args+=("--no-build")
    fi
    if [ -n "$filter" ]; then
        args+=("--filter" "$filter")
    fi
    if [ "$COLLECT_COVERAGE" = true ]; then
        args+=(
            "--collect:XPlat Code Coverage"
            "--results-directory"
            "$REPO_ROOT/$RESULTS_DIRECTORY"
        )
    fi

    dotnet "${args[@]}"
}

case "$LANE" in
    smoke)
        smoke_args=("--lane" "all" "--configuration" "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then smoke_args+=("--no-restore"); fi
        if [ "$NO_BUILD" = true ]; then smoke_args+=("--no-build"); fi
        bash "$SCRIPT_DIR/test-smoke.sh" "${smoke_args[@]}"
        ;;
    fast)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "(Category!=Integration)&(Category!=Performance)&(FullyQualifiedName!~Integration)&(FullyQualifiedName!~Performance)"
        invoke_dotnet_test "tests/ETL-SQL.LanguageServer.Tests/ETL-SQL.LanguageServer.Tests.csproj" ""
        ;;
    engine)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "(Category!=Integration)&(Category!=Performance)&(FullyQualifiedName!~Integration)&(FullyQualifiedName!~Performance)"
        ;;
    portal)
        invoke_dotnet_test "tests/ETL-SQL.ReportPortal.Tests/ETL-SQL.ReportPortal.Tests.csproj" ""
        ;;
    integration)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "Category=Integration"
        invoke_dotnet_test "tests/ETL-SQL.ReportPortal.Tests/ETL-SQL.ReportPortal.Tests.csproj" ""
        ;;
    perf)
        invoke_dotnet_test "tests/ETL-SQL.PerfTests/ETL-SQL.PerfTests.csproj" "Category=Performance"
        ;;
    full)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.LanguageServer.Tests/ETL-SQL.LanguageServer.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.ReportPortal.Tests/ETL-SQL.ReportPortal.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.PerfTests/ETL-SQL.PerfTests.csproj" ""
        ;;
    slt)
        PREVIOUS_RUN_SLT="$ETL_SQL_RUN_SLT"
        export ETL_SQL_RUN_SLT="1"
        invoke_dotnet_test "tests/ETL-SQL.SqlLogicTests/ETL-SQL.SqlLogicTests.csproj" "Category=SLT"
        export ETL_SQL_RUN_SLT="$PREVIOUS_RUN_SLT"
        ;;
    benchmarks)
        args=("run" "--project" "$REPO_ROOT/tests/ETL-SQL.Benchmarks/ETL-SQL.Benchmarks.csproj" "--configuration" "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then
            args+=("--no-restore")
        fi
        dotnet "${args[@]}"
        ;;
    *)
        echo "Unknown lane: $LANE"
        exit 1
        ;;
esac
