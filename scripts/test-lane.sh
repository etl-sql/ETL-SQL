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
ENGINE_FILTER="(Category!=Integration)&(Category!=Performance)&(Category!=ScaleCertification)&(Category!=ScaleAssessment)&(Category!=BillionRowCertification)&(Category!=DeploymentProfile)&(Category!=EbnfConformance)"
PORTAL_FILTER="(Category!=Integration)&(Category!=HostedServices)"

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
    node "$REPO_ROOT/scripts/test-publish-folders.mjs"
    node "$REPO_ROOT/scripts/test-subscription-history-ui.mjs"
    node "$REPO_ROOT/scripts/test-result-grid-ui.mjs"
    node "$REPO_ROOT/scripts/test-admin-catalog-ui.mjs"
    node "$REPO_ROOT/scripts/test-dataset-acl-ui.mjs"
    node "$REPO_ROOT/scripts/test-orchestrator-acl-ui.mjs"
}

invoke_fuzz_lane() {
    local seed="$1" iterations="$2" strict_exec="$3"
    local previous_seed="${ETLSQL_FUZZ_SEED-}"
    local previous_iterations="${ETLSQL_FUZZ_ITERATIONS-}"
    local previous_strict="${ETLSQL_FUZZ_STRICT_EXEC-}"
    ETLSQL_FUZZ_SEED="$seed"
    ETLSQL_FUZZ_ITERATIONS="$iterations"
    ETLSQL_FUZZ_STRICT_EXEC="$strict_exec"
    export ETLSQL_FUZZ_SEED ETLSQL_FUZZ_ITERATIONS ETLSQL_FUZZ_STRICT_EXEC
    invoke_dotnet_test "tests/ETL-SQL.FuzzTests/ETL-SQL.FuzzTests.csproj" "Category=Fuzz"
    ETLSQL_FUZZ_SEED="$previous_seed"
    ETLSQL_FUZZ_ITERATIONS="$previous_iterations"
    ETLSQL_FUZZ_STRICT_EXEC="$previous_strict"
    export ETLSQL_FUZZ_SEED ETLSQL_FUZZ_ITERATIONS ETLSQL_FUZZ_STRICT_EXEC
}

case "$LANE" in
    smoke)
        smoke_args=("--lane" "all" "--configuration" "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then smoke_args+=("--no-restore"); fi
        if [ "$NO_BUILD" = true ]; then smoke_args+=("--no-build"); fi
        bash "$SCRIPT_DIR/test-smoke.sh" "${smoke_args[@]}"
        ;;
    fast)
        smoke_args=("--lane" "all" "--configuration" "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then smoke_args+=("--no-restore"); fi
        if [ "$NO_BUILD" = true ]; then smoke_args+=("--no-build"); fi
        bash "$SCRIPT_DIR/test-smoke.sh" "${smoke_args[@]}"

        invoke_dotnet_test "tests/ETL-SQL.LanguageServer.Tests/ETL-SQL.LanguageServer.Tests.csproj" ""
        ;;
    engine)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "$ENGINE_FILTER"
        ;;
    portal)
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" "$PORTAL_FILTER"
        # Keep the real hosted-service pipeline in its own process, away from unrelated classes.
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" "Category=HostedServices"
        invoke_lineage_ui_smoke
        ;;
    portal-hosted)
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" "Category=HostedServices"
        ;;
    browser)
        invoke_dotnet_test "tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj" "Category=Browser"
        ;;
    integration)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "Category=Integration"
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" "Category=Integration"
        ;;
    perf)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "Category=Performance"
        invoke_dotnet_test "tests/ETL-SQL.PerfTests/ETL-SQL.PerfTests.csproj" "Category=Performance"
        ;;
    full)
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.LanguageServer.Tests/ETL-SQL.LanguageServer.Tests.csproj" ""
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" "$PORTAL_FILTER"
        invoke_dotnet_test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" "Category=HostedServices"
        invoke_lineage_ui_smoke
        invoke_dotnet_test "tests/ETL-SQL.PerfTests/ETL-SQL.PerfTests.csproj" ""
        ;;
    release)
        fast_args=(--lane fast --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then fast_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then fast_args+=(--no-build); fi
        if [ "$COLLECT_COVERAGE" = true ]; then
            fast_args+=(--collect-coverage --results-directory "$RESULTS_DIRECTORY")
        fi
        bash "$SCRIPT_DIR/test-lane.sh" "${fast_args[@]}"

        engine_args=(--lane engine --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then engine_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then engine_args+=(--no-build); fi
        if [ "$COLLECT_COVERAGE" = true ]; then
            engine_args+=(--collect-coverage --results-directory "$RESULTS_DIRECTORY")
        fi
        bash "$SCRIPT_DIR/test-lane.sh" "${engine_args[@]}"

        portal_args=(--lane portal --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then portal_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then portal_args+=(--no-build); fi
        bash "$SCRIPT_DIR/test-lane.sh" "${portal_args[@]}"

        fuzz_args=(--lane fuzz-smoke --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then fuzz_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then fuzz_args+=(--no-build); fi
        bash "$SCRIPT_DIR/test-lane.sh" "${fuzz_args[@]}"

        ebnf_args=(--lane ebnf --configuration "$CONFIGURATION")
        if [ "$NO_RESTORE" = true ]; then ebnf_args+=(--no-restore); fi
        if [ "$NO_BUILD" = true ]; then ebnf_args+=(--no-build); fi
        bash "$SCRIPT_DIR/test-lane.sh" "${ebnf_args[@]}"

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
    ebnf)
        # Deterministic grammar generation/rejection contract; deliberately separate from the
        # quick-feedback lanes so release ownership remains visible.
        invoke_dotnet_test "tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj" "Category=EbnfConformance"
        ;;
    fuzz-smoke)
        invoke_fuzz_lane "12345" "2000" "1"
        ;;
    fuzz)
        FUZZ_ITERATIONS="${ETLSQL_FUZZ_ITERATIONS:-100000}"
        invoke_fuzz_lane "" "$FUZZ_ITERATIONS" "${ETLSQL_FUZZ_STRICT_EXEC-}"
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
