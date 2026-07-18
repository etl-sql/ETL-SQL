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
FAST_FILTER="(Category!=Integration)&(Category!=Performance)&(Category!=ScaleCertification)&(FullyQualifiedName!~Integration)&(FullyQualifiedName!~Performance)"

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

invoke_lineage_ui_smoke() {
    node "$REPO_ROOT/scripts/test-lineage-ui.mjs"
}

case "$LANE" in
    smoke)
        smoke_args=("--lane" "all" "--configuration" "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then smoke_args+=("--no-restore"); fi
        if [ "$NO_BUILD" = true ]; then smoke_args+=("--no-build"); fi
        bash "$SCRIPT_DIR/test-smoke.sh" "${smoke_args[@]}"
        ;;
    fast)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "$FAST_FILTER"
        invoke_dotnet_test "tests/ETL-SQL.LanguageServer.Tests/ETL-SQL.LanguageServer.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" ""
        invoke_lineage_ui_smoke
        ;;
    engine)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "$FAST_FILTER"
        ;;
    portal)
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" ""
        invoke_lineage_ui_smoke
        ;;
    integration)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "Category=Integration"
        ;;
    perf)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "Category=Performance"
        invoke_dotnet_test "tests/ETL-SQL.PerfTests/ETL-SQL.PerfTests.csproj" "Category=Performance"
        ;;
    full)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.LanguageServer.Tests/ETL-SQL.LanguageServer.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" ""
        invoke_lineage_ui_smoke
        invoke_dotnet_test "tests/ETL-SQL.PerfTests/ETL-SQL.PerfTests.csproj" ""
        ;;
    release)
        smoke_args=(--lane smoke --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then smoke_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then smoke_args+=(--no-build); fi
        bash "$SCRIPT_DIR/test-lane.sh" "${smoke_args[@]}"

        fast_args=(--lane fast --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then fast_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then fast_args+=(--no-build); fi
        if [ "$COLLECT_COVERAGE" = true ]; then
            fast_args+=(--collect-coverage --results-directory "$RESULTS_DIRECTORY")
        fi
        bash "$SCRIPT_DIR/test-lane.sh" "${fast_args[@]}"

        slt_args=(--lane slt --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then slt_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then slt_args+=(--no-build); fi
        bash "$SCRIPT_DIR/test-lane.sh" "${slt_args[@]}"
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
