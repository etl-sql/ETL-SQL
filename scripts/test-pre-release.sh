#!/usr/bin/env bash
# test-pre-release.sh — local-first pre-release validation gate
# Bash equivalent of Test-PreRelease.ps1
#
# Usage:
#   ./scripts/test-pre-release.sh
#   ./scripts/test-pre-release.sh --resume
#   ./scripts/test-pre-release.sh --quick --include-slt
#   ./scripts/test-pre-release.sh --explain --include-slt --include-docker-integration
#   ./scripts/test-pre-release.sh --include-docker-integration --include-standard-scale
#   ./scripts/test-pre-release.sh --build-installers --platforms linux-x64,osx-arm64

set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
CONFIGURATION="Release"
RESUME=false
FORCE_RESUME=false
SKIP_NODE=false
SKIP_SCALE=false
INCLUDE_DOCKER=false
INCLUDE_SLT=false
INCLUDE_STANDARD_SCALE=false
BUILD_INSTALLERS=false
QUICK=false
EXPLAIN=false
PLATFORMS="linux-x64"
OUT_DIR="release-validation"

while [[ $# -gt 0 ]]; do
    case $1 in
        --configuration|-c)          CONFIGURATION="$2"; shift 2 ;;
        --resume)                    RESUME=true;         shift ;;
        --force-resume)              FORCE_RESUME=true;   shift ;;
        --skip-node)                 SKIP_NODE=true;      shift ;;
        --skip-scale)                SKIP_SCALE=true;     shift ;;
        --include-docker-integration) INCLUDE_DOCKER=true; shift ;;
        --include-slt)               INCLUDE_SLT=true;    shift ;;
        --include-standard-scale)    INCLUDE_STANDARD_SCALE=true; shift ;;
        --build-installers)          BUILD_INSTALLERS=true; shift ;;
        --quick)                     QUICK=true;          shift ;;
        --explain)                   EXPLAIN=true;        shift ;;
        --platforms)                 PLATFORMS="$2";      shift 2 ;;
        --out-dir)                   OUT_DIR="$2";        shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
RUN_ID="$(date '+%Y%m%d-%H%M%S')"
VALIDATION_ROOT="$REPO_ROOT/$OUT_DIR"
LATEST_DIR="$VALIDATION_ROOT/latest"
STATE_PATH="$LATEST_DIR/state.json"
RUN_DIR="$VALIDATION_ROOT/$RUN_ID"
REPORT_JSON="$RUN_DIR/pre-release-report.json"
REPORT_MD="$RUN_DIR/pre-release-report.md"

EFFECTIVE_SKIP_NODE="$SKIP_NODE"
EFFECTIVE_SKIP_SCALE="$SKIP_SCALE"
EFFECTIVE_INCLUDE_DOCKER="$INCLUDE_DOCKER"
EFFECTIVE_INCLUDE_STANDARD_SCALE="$INCLUDE_STANDARD_SCALE"
EFFECTIVE_BUILD_INSTALLERS="$BUILD_INSTALLERS"

if [[ "$QUICK" == true ]]; then
    EFFECTIVE_SKIP_NODE=true
    EFFECTIVE_SKIP_SCALE=true
    EFFECTIVE_INCLUDE_DOCKER=false
    EFFECTIVE_INCLUDE_STANDARD_SCALE=false
    EFFECTIVE_BUILD_INSTALLERS=false
fi

# ---------------------------------------------------------------------------
# Colors
# ---------------------------------------------------------------------------
RED=$'\033[0;31m'
GREEN=$'\033[0;32m'
CYAN=$'\033[0;36m'
YELLOW=$'\033[1;33m'
GRAY=$'\033[0;90m'
BOLD=$'\033[1m'
RESET=$'\033[0m'

print_plan_phase() {
    local index="$1"
    local name="$2"
    local command="$3"
    local reason="$4"

    printf "%2s. %s\n" "$index" "$name"
    printf "    %s\n" "$command"
    printf "    %s\n" "$reason"
}

show_pre_release_plan() {
    echo "${CYAN}${BOLD}Pre-release validation plan${RESET}"
    echo "Configuration: $CONFIGURATION"
    echo "Quick: $QUICK; IncludeSlt: $INCLUDE_SLT; Docker: $EFFECTIVE_INCLUDE_DOCKER; StandardScale: $EFFECTIVE_INCLUDE_STANDARD_SCALE; BuildInstallers: $EFFECTIVE_BUILD_INSTALLERS"
    echo

    local i=1
    print_plan_phase "$i" "Asset drift check" "node ./scripts/sync-assets.js -Check" "Shared report runtime files must match generated host copies."; i=$((i + 1))
    print_plan_phase "$i" "Shell script line endings check" "node scripts/check-shell-line-endings.js" "Shell scripts (.sh) must use LF line endings to avoid bash syntax errors."; i=$((i + 1))
    print_plan_phase "$i" "Secret scan" "node scripts/scan-secrets.js" "No real credentials (keys/provider tokens) reach the public repo — early local tripwire ahead of GitGuardian."; i=$((i + 1))
    print_plan_phase "$i" "Dotnet restore" "dotnet restore ETL-SQL.slnx; dotnet tool restore" "Package graph and repository-local release tools resolve before build, tests, and coverage reporting."; i=$((i + 1))
    print_plan_phase "$i" "Dependency-audit self-test" "./scripts/Test-DependencyAudit.ps1 (via pwsh)" "The dependency-audit helpers behave correctly (reliable fallback + hard failure)."; i=$((i + 1))
    print_plan_phase "$i" "NuGet dependency audit" "scripts/lib/DependencyAudit.ps1 Invoke-NuGetDependencyAudit (via pwsh)" "Release should not ship known vulnerable or deprecated packages."; i=$((i + 1))
    print_plan_phase "$i" "SBOM generation" "node scripts/generate-sbom.js" "The released SBOM generates and its component version matches Directory.Build.props."; i=$((i + 1))
    print_plan_phase "$i" "Third-party inventory drift" "node scripts/generate-third-party-inventory.js --check" "THIRD-PARTY-INVENTORY.md matches the current package graph, so the licence review and NOTICES reflect what actually ships."; i=$((i + 1))
    print_plan_phase "$i" "Dotnet build" "dotnet build ETL-SQL.slnx --configuration $CONFIGURATION --no-restore" "All projects compile in the release configuration."; i=$((i + 1))
    print_plan_phase "$i" "Test structure audit" "./scripts/Get-TestLaneInventory.ps1 -FailOnIssues (via pwsh)" "Lane ownership and semantic test organization remain explicit."; i=$((i + 1))
    print_plan_phase "$i" "Format verify" "dotnet format ETL-SQL.slnx --verify-no-changes --no-restore (auto-applies 'dotnet format' on drift)" "Code formatting (whitespace + import ordering) matches .editorconfig — same check the CI format gate runs. On drift the fix is applied automatically; commit it and re-run."; i=$((i + 1))
    if [[ "$EFFECTIVE_SKIP_SCALE" != true ]]; then
        print_plan_phase "$i" "Scale certification smoke" "./scripts/test-scale-certification.sh --tier Smoke" "Small certification workload still meets baseline before the long test lanes heat the machine."; i=$((i + 1))
        print_plan_phase "$i" "Cert baseline regression check (smoke)" "./scripts/Compare-CertBaseline.ps1 -MarkdownReport <run>/cert-baseline-smoke.md (via pwsh)" "Smoke certification metrics have not regressed; warning evidence is preserved in validation artifacts."; i=$((i + 1))
    fi
    if [[ "$EFFECTIVE_INCLUDE_STANDARD_SCALE" == true ]]; then
        print_plan_phase "$i" "Scale certification standard" "./scripts/test-scale-certification.sh --tier Standard" "Release-size certification workload still meets baseline before the long test lanes heat the machine."; i=$((i + 1))
        print_plan_phase "$i" "Cert baseline regression check (standard)" "./scripts/Compare-CertBaseline.ps1 -MarkdownReport <run>/cert-baseline-standard.md (via pwsh)" "Standard certification metrics have not regressed; warning evidence is preserved in validation artifacts."; i=$((i + 1))
        print_plan_phase "$i" "Spill allocation budget (10M)" "./scripts/Test-SpillAllocProfile.ps1 -Rows 10000000 -SkipBuild (via pwsh)" "Gate F round-trip allocation, GC, and peak-memory containment stay within the checked-in budget."; i=$((i + 1))
    fi
    print_plan_phase "$i" "Smoke lane" "./scripts/test-lane.sh --lane smoke" "Critical startup, security, report, and portal checks."; i=$((i + 1))
    print_plan_phase "$i" "Fast lane" "./scripts/test-lane.sh --lane fast" "Bounded quick-feedback lane: smoke coverage plus language-server tests."; i=$((i + 1))
    print_plan_phase "$i" "Engine lane and coverage gate" "./scripts/Test-CoverageGate.ps1 -RunEngineLane -MinimumLineCoverage 70 (via pwsh)" "Broad engine/parser/evaluator coverage is collected once and must meet the fail-closed 70% release threshold."; i=$((i + 1))
    print_plan_phase "$i" "Portal lane" "./scripts/test-lane.sh --lane portal" "Portal API and browser-side smoke coverage remain explicit without slowing the default fast lane."; i=$((i + 1))
    print_plan_phase "$i" "N->N+1 upgrade-path drill" "dotnet test tests/ETL-SQL.Portal.Tests --filter FullyQualifiedName~UpgradePathDrillTests" "In-place EF migration over a live release-N catalog keeps data intact (release gate)."; i=$((i + 1))
    print_plan_phase "$i" "Sample scripts" "./scripts/test-all-samples.sh" "Published samples remain runnable."; i=$((i + 1))
    print_plan_phase "$i" "HA soak contract gate" "./scripts/Test-HaSoakContracts.ps1 (via pwsh)" "PostgreSQL HA soak topology, workload, metrics, diagnostics, runbook, evidence validation, and fault/soak plan contracts stay usable before release."; i=$((i + 1))

    if [[ "$INCLUDE_SLT" == true ]]; then
        print_plan_phase "$i" "SLT lane" "./scripts/test-lane.sh --lane slt" "SQL logic corpus checks parser/evaluator compatibility."; i=$((i + 1))
    fi

    if [[ "$EFFECTIVE_SKIP_NODE" != true ]]; then
        print_plan_phase "$i" "VS Code npm ci" "npm ci" "Extension dependencies install from lockfile."; i=$((i + 1))
        print_plan_phase "$i" "VS Code UI npm ci" "npm ci" "UI package dependencies install from lockfile."; i=$((i + 1))
        print_plan_phase "$i" "VS Code npm audit" "npm outdated / npm audit" "Extension dependency risk is visible before release."; i=$((i + 1))
        print_plan_phase "$i" "VS Code compile" "npm run compile" "TypeScript extension compiles."; i=$((i + 1))
        print_plan_phase "$i" "VS Code lint" "npm run lint" "Production extension lint warnings fail the release gate."; i=$((i + 1))
        print_plan_phase "$i" "VS Code UI lint" "npm run lint" "UI package lint warnings fail the release gate."; i=$((i + 1))
        print_plan_phase "$i" "VS Code UI build" "npm run build" "UI TypeScript and Vite bundle compile."; i=$((i + 1))
        print_plan_phase "$i" "VS Code UI unit tests" "npm run test:unit" "UI package unit tests pass before release."; i=$((i + 1))
        print_plan_phase "$i" "VS Code VSIX package" "npx @vscode/vsce package --target linux-x64" "VSIX packages cleanly — same vsce step release.yml runs; catches manifest/engine errors before the release build."; i=$((i + 1))
        print_plan_phase "$i" "VS Code unit tests" "npm run test:unit" "Extension unit tests pass."; i=$((i + 1))
    fi

    if [[ "$EFFECTIVE_INCLUDE_DOCKER" == true ]]; then
        print_plan_phase "$i" "Docker integration lane" "./scripts/test-lane.sh --lane integration" "External connector boundaries pass against local containers."; i=$((i + 1))
    fi

    if [[ "$EFFECTIVE_BUILD_INSTALLERS" == true ]]; then
        print_plan_phase "$i" "Release publish artifacts" "./scripts/publish-release.sh --platforms $PLATFORMS" "Release binaries can be published for target platforms."; i=$((i + 1))
    fi
}

if [[ "$EXPLAIN" == true ]]; then
    show_pre_release_plan
    exit 0
fi

mkdir -p "$RUN_DIR"

# ---------------------------------------------------------------------------
# JSON string escaping (no external tools required)
# ---------------------------------------------------------------------------
json_str() {
    local s="$1"
    s="${s//\\/\\\\}"
    s="${s//\"/\\\"}"
    s="${s//$'\n'/\\n}"
    s="${s//$'\r'/\\r}"
    s="${s//$'\t'/\\t}"
    printf '"%s"' "$s"
}

npm_json_command() {
    local cwd="$1"
    shift
    local output status

    set +e
    output="$(cd "$cwd" && npm "$@" 2>&1)"
    status=$?
    set -e

    NPM_JSON_OUTPUT="$output"
    NPM_JSON_STATUS="$status"
}

npm_print_outdated_summary() {
    local label="$1"
    local cwd="$2"
    local output

    npm_json_command "$cwd" outdated --json
    output="$NPM_JSON_OUTPUT"

    if [[ -z "${output//[[:space:]]/}" ]]; then
        echo "[$label] Outdated packages: 0"
        return 0
    fi

    NPM_LABEL="$label" NPM_JSON="$output" node -e '
const label = process.env.NPM_LABEL;
const data = process.env.NPM_JSON ? JSON.parse(process.env.NPM_JSON) : {};
const entries = Object.entries(data).filter(([, value]) => value && value.latest);
console.log(`[${label}] Outdated packages: ${entries.length}`);
for (const [name, value] of entries.slice(0, 20)) {
  const location = value.location ? ` @ ${value.location}` : "";
  const type = value.type ? ` [${value.type}]` : "";
  console.log(`  - ${name}${type}${location}: ${value.current} -> ${value.wanted} (latest ${value.latest})`);
}
if (entries.length > 20) {
  console.log(`  - ... and ${entries.length - 20} more`);
}
'
}

npm_print_audit_summary() {
    local label="$1"
    local cwd="$2"
    local output status

    npm_json_command "$cwd" audit --json
    output="$NPM_JSON_OUTPUT"
    status="$NPM_JSON_STATUS"

    if [[ -z "${output//[[:space:]]/}" ]]; then
        echo "[$label] Vulnerabilities: total=0, low=0, moderate=0, high=0, critical=0"
        return 0
    fi

    set +e
    NPM_LABEL="$label" NPM_JSON="$output" node -e '
const label = process.env.NPM_LABEL;
const data = process.env.NPM_JSON ? JSON.parse(process.env.NPM_JSON) : {};
const metadata = (data.metadata && data.metadata.vulnerabilities) || {};
const total = Number(metadata.total || 0);
const low = Number(metadata.low || 0);
const moderate = Number(metadata.moderate || 0);
const high = Number(metadata.high || 0);
const critical = Number(metadata.critical || 0);
const vulnerabilities = data.vulnerabilities || {};
const names = Object.keys(vulnerabilities);

console.log(`[${label}] Vulnerabilities: total=${total}, low=${low}, moderate=${moderate}, high=${high}, critical=${critical}`);
for (const name of names.slice(0, 20)) {
  const value = vulnerabilities[name] || {};
  let severity = value.severity || "";
  if (!severity && Array.isArray(value.via)) {
    const viaSeverity = value.via.find((item) => item && item.severity);
    severity = viaSeverity ? viaSeverity.severity : "";
  }
  if (!severity) {
    severity = "unknown";
  }
  console.log(`  - ${name}: ${severity}`);
}
if (names.length > 20) {
  console.log(`  - ... and ${names.length - 20} more`);
}

if (total > 0 || names.length > 0) {
  process.exit(1);
}
'
    status=$?
    set -e

    return "$status"
}

npm_dependency_audit_phase() {
    local any_failed=0

    npm_print_outdated_summary "src/etl-sql-vscode" "$REPO_ROOT/src/etl-sql-vscode"
    npm_print_outdated_summary "src/etl-sql-vscode/ui" "$REPO_ROOT/src/etl-sql-vscode/ui"

    if ! npm_print_audit_summary "src/etl-sql-vscode" "$REPO_ROOT/src/etl-sql-vscode"; then
        any_failed=1
    fi

    if ! npm_print_audit_summary "src/etl-sql-vscode/ui" "$REPO_ROOT/src/etl-sql-vscode/ui"; then
        any_failed=1
    fi

    return "$any_failed"
}

# ---------------------------------------------------------------------------
# Format phase: verify (CI parity) and auto-fix on drift
# Runs the read-only --verify-no-changes check first; on drift it applies
# 'dotnet format' automatically and fails with an actionable message so the
# reformatted files are reviewed and committed (and thus reach CI), then re-run.
# Mirrors the Format verify phase in Test-PreRelease.ps1.
# ---------------------------------------------------------------------------
format_verify_phase() {
    if dotnet format "ETL-SQL.slnx" "--verify-no-changes" "--no-restore"; then
        return 0
    fi
    echo "Formatting drift detected. Applying 'dotnet format' to fix it..."
    if ! dotnet format "ETL-SQL.slnx" "--no-restore"; then
        echo "dotnet format failed to apply fixes." >&2
        return 1
    fi
    echo "Formatting drift was found and automatically fixed in the working tree. Review and commit the reformatted files, then re-run (use --resume)." >&2
    return 1
}

# ---------------------------------------------------------------------------
# VSIX packaging validation: runs the same 'vsce package' as release.yml so
# manifest/engine errors are caught locally, not in the cross-platform build.
# ---------------------------------------------------------------------------
vsce_package_phase() {
    local out="${TMPDIR:-/tmp}/etl-sql-vsce-validate.vsix"
    ( cd "$REPO_ROOT/src/etl-sql-vscode" && npx @vscode/vsce package --target linux-x64 --out "$out" )
    local code=$?
    rm -f "$out"
    return "$code"
}

# ---------------------------------------------------------------------------
# PowerShell bridge: the dependency-audit, HA soak contract, and cert-baseline phases reuse the
# canonical PowerShell helpers (scripts/lib/DependencyAudit.ps1,
# Compare-CertBaseline.ps1, Test-DependencyAudit.ps1, Test-HaSoakContracts.ps1) so there is a single
# source of truth shared with Test-PreRelease.ps1 (no parallel bash port to
# drift). PowerShell 7+ (pwsh) is cross-platform; these phases require it.
# ---------------------------------------------------------------------------
resolve_pwsh() {
    local p
    p="$(command -v pwsh || command -v powershell || true)"
    if [[ -z "$p" ]]; then
        echo "PowerShell (pwsh) is required for this phase but was not found on PATH. Install PowerShell 7+ or run the PowerShell gate (Test-PreRelease.ps1)." >&2
        return 1
    fi
    printf '%s' "$p"
}

dependency_audit_selftest_phase() {
    local pwsh; pwsh="$(resolve_pwsh)" || return 1
    "$pwsh" -NoProfile -File ./scripts/Test-DependencyAudit.ps1
}

nuget_dependency_audit_phase() {
    local pwsh; pwsh="$(resolve_pwsh)" || return 1
    "$pwsh" -NoProfile -Command "\$ErrorActionPreference='Stop'; . ./scripts/lib/DependencyAudit.ps1; Invoke-NuGetDependencyAudit -RepoRoot '$REPO_ROOT' -Solution 'ETL-SQL.slnx' | Where-Object { \$_ -is [string] }"
}

cert_baseline_phase() {
    local report_path="${1:-}"
    local pwsh; pwsh="$(resolve_pwsh)" || return 1
    if [[ -n "$report_path" ]]; then
        "$pwsh" -NoProfile -File ./scripts/Compare-CertBaseline.ps1 -MarkdownReport "$report_path"
    else
        "$pwsh" -NoProfile -File ./scripts/Compare-CertBaseline.ps1
    fi
}

ha_soak_contract_phase() {
    local pwsh; pwsh="$(resolve_pwsh)" || return 1
    "$pwsh" -NoProfile -File ./scripts/Test-HaSoakContracts.ps1
}

spill_allocation_budget_phase() {
    local pwsh; pwsh="$(resolve_pwsh)" || return 1
    "$pwsh" -NoProfile -File ./scripts/Test-SpillAllocProfile.ps1 -Rows 10000000 -SkipBuild
}

# ---------------------------------------------------------------------------
# SBOM generation: the release attaches sbom.json; generate it and assert its
# component version matches Directory.Build.props (single source of truth) so a
# broken generator or a re-hardcoded version is caught before release.
# ---------------------------------------------------------------------------
sbom_generation_phase() {
    node scripts/generate-sbom.js || return 1
    local expected actual
    expected="$(grep -oE '<VersionPrefix>[0-9.]+</VersionPrefix>' Directory.Build.props | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')"
    actual="$(node -e "console.log(require('./release/sbom.json').metadata.component.version)")"
    if [[ "$expected" != "$actual" ]]; then
        echo "SBOM version '$actual' does not match Directory.Build.props '$expected'." >&2
        return 1
    fi
    echo "SBOM component version $actual matches Directory.Build.props."
}

# ---------------------------------------------------------------------------
# Source fingerprint — SHA-256 of HEAD + working-tree status
# ---------------------------------------------------------------------------
get_fingerprint() {
    local head status text
    head="$(git -C "$REPO_ROOT" rev-parse HEAD 2>/dev/null || true)"
    status="$(git -C "$REPO_ROOT" status --short 2>/dev/null || true)"
    text="$head"$'\n'"$status"
    if command -v sha256sum &>/dev/null; then
        printf '%s' "$text" | sha256sum | cut -d' ' -f1
    elif command -v shasum &>/dev/null; then
        printf '%s' "$text" | shasum -a 256 | cut -d' ' -f1
    else
        echo "ERROR: sha256sum or shasum is required." >&2; exit 1
    fi
}

FINGERPRINT="$(get_fingerprint)"

# ---------------------------------------------------------------------------
# State helpers
# ---------------------------------------------------------------------------

# Read a single key from state.json using grep (no jq required)
state_get() {
    local key="$1"
    [[ -f "$STATE_PATH" ]] || { echo ""; return; }
    grep -oP "\"${key}\"\\s*:\\s*\"\\K[^\"]*" "$STATE_PATH" 2>/dev/null | head -1 || true
}

# Check if a named phase has status "Passed" in state.json
is_phase_passed() {
    local name="$1"
    [[ -f "$STATE_PATH" ]] || return 1
    # Look for "name": "<name>" followed eventually by "status": "Passed" in same block
    grep -qP "\"name\"\\s*:\\s*\"$(echo "$name" | sed 's/[.[\*^$]/\\&/g')\"" "$STATE_PATH" 2>/dev/null || return 1
    # Simplified: scan for the phase entry and check status
    local in_block=false found_name=false
    while IFS= read -r line; do
        if echo "$line" | grep -qP "\"name\"\\s*:\\s*\"$(echo "$name" | sed 's/[.[\*^$]/\\&/g')\""; then
            found_name=true
        fi
        if [[ "$found_name" == true ]]; then
            if echo "$line" | grep -qP '"status"\s*:\s*"Passed"'; then
                return 0
            fi
            # Stop at next phase entry (another "name" key)
            if echo "$line" | grep -qP '"name"\s*:' && [[ "$(echo "$line" | grep -oP '"name"')" ]] && [[ "$found_name" == true ]]; then
                local this_name
                this_name="$(echo "$line" | grep -oP '"name"\s*:\s*"\K[^"]*')"
                [[ "$this_name" != "$name" ]] && break
            fi
        fi
    done < "$STATE_PATH"
    return 1
}

# Accumulate phase results as JSON object strings
RESULT_OBJECTS=()

save_state() {
    local final_status="$1"
    mkdir -p "$LATEST_DIR"
    {
        echo "{"
        echo "  \"generatedAt\": \"$(date -u +"%Y-%m-%dT%H:%M:%SZ")\","
        echo "  \"runId\": \"$RUN_ID\","
        echo "  \"status\": \"$final_status\","
        echo "  \"sourceFingerprint\": \"$FINGERPRINT\","
        echo "  \"configuration\": \"$CONFIGURATION\","
        echo "  \"phases\": ["
        local first=true
        for obj in "${RESULT_OBJECTS[@]+"${RESULT_OBJECTS[@]}"}"; do
            [[ "$first" == true ]] && first=false || echo "    ,"
            echo "    $obj"
        done
        echo "  ]"
        echo "}"
    } > "$STATE_PATH"
}

# ---------------------------------------------------------------------------
# Phase runner
# ---------------------------------------------------------------------------
START_EPOCH="$(date +%s)"

run_phase() {
    local name="$1"
    local command_label="$2"
    shift 2
    # Remaining args: command + arguments to execute

    # Resume: skip if this phase passed in the previous run with the same fingerprint
    if [[ "$RESUME" == true ]] && is_phase_passed "$name"; then
        echo "${GRAY}SKIP $name${RESET}"
        RESULT_OBJECTS+=("{\"name\":$(json_str "$name"),\"command\":$(json_str "$command_label"),\"status\":\"Skipped\",\"elapsedSeconds\":0,\"log\":\"\",\"note\":\"Skipped by --resume; previous phase passed for this source fingerprint.\"}")
        save_state "Running"
        return
    fi

    echo ""
    echo "${CYAN}==> $name${RESET}"
    echo "${GRAY}    $command_label${RESET}"

    local safe_name="${name//[^A-Za-z0-9_.-]/_}"
    local phase_log="$RUN_DIR/${safe_name}.log"
    local t_start t_end elapsed status note

    t_start="$(date +%s)"

    set +e
    (cd "$REPO_ROOT" && "$@") 2>&1 | tee "$phase_log"
    local cmd_exit="${PIPESTATUS[0]}"
    set -e

    t_end="$(date +%s)"
    elapsed=$((t_end - t_start))

    if [[ "$cmd_exit" -eq 0 ]]; then
        status="Passed"
        note=""
    else
        status="Failed"
        note="Command exited with code $cmd_exit."
    fi

    RESULT_OBJECTS+=("{\"name\":$(json_str "$name"),\"command\":$(json_str "$command_label"),\"status\":\"$status\",\"elapsedSeconds\":$elapsed,\"log\":$(json_str "$phase_log"),\"note\":$(json_str "$note")}")

    if [[ "$status" == "Failed" ]]; then
        save_state "Failed"
        write_reports "Failed"
        echo ""
        echo "${RED}FAILED $name${RESET}"
        echo "${YELLOW}Log: $phase_log${RESET}"
        echo ""
        echo "Pre-release validation failed at phase '$name'. Fix the issue and rerun with --resume." >&2
        exit 1
    fi

    save_state "Running"
    echo "${GREEN}PASS $name (${elapsed}s)${RESET}"
}

# ---------------------------------------------------------------------------
# Report writer
# ---------------------------------------------------------------------------
write_reports() {
    local final_status="$1"
    local t_end elapsed
    t_end="$(date +%s)"
    elapsed=$((t_end - START_EPOCH))
    local generated
    generated="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

    # JSON report
    {
        echo "{"
        echo "  \"generatedAt\": \"$generated\","
        echo "  \"runId\": \"$RUN_ID\","
        echo "  \"status\": \"$final_status\","
        echo "  \"sourceFingerprint\": \"$FINGERPRINT\","
        echo "  \"configuration\": \"$CONFIGURATION\","
        echo "  \"elapsedSeconds\": $elapsed,"
        echo "  \"phases\": ["
        local first=true
        for obj in "${RESULT_OBJECTS[@]+"${RESULT_OBJECTS[@]}"}"; do
            [[ "$first" == true ]] && first=false || echo "    ,"
            echo "    $obj"
        done
        echo "  ]"
        echo "}"
    } > "$REPORT_JSON"

    # Markdown report
    {
        echo "# ETL-SQL Pre-Release Validation"
        echo ""
        echo "Run: \`$RUN_ID\`"
        echo ""
        echo "Status: **$final_status**"
        echo ""
        echo "Generated: $(date -u '+%Y-%m-%d %H:%M:%S') UTC"
        echo ""
        echo "Configuration: \`$CONFIGURATION\`"
        echo ""
        echo "Source fingerprint: \`$FINGERPRINT\`"
        echo ""
        echo "| Phase | Status | Seconds | Command | Log |"
        echo "| :--- | :---: | ---: | :--- | :--- |"
        local last_failure_name="" last_failure_note=""
        for obj in "${RESULT_OBJECTS[@]+"${RESULT_OBJECTS[@]}"}"; do
            local n c s e l no
            n="$(echo "$obj" | grep -oP '"name"\s*:\s*"\K[^"]*')"
            c="$(echo "$obj" | grep -oP '"command"\s*:\s*"\K[^"]*')"
            s="$(echo "$obj" | grep -oP '"status"\s*:\s*"\K[^"]*')"
            e="$(echo "$obj" | grep -oP '"elapsedSeconds"\s*:\s*\K[0-9]+')"
            l="$(echo "$obj" | grep -oP '"log"\s*:\s*"\K[^"]*')"
            no="$(echo "$obj" | grep -oP '"note"\s*:\s*"\K[^"]*')"
            c="${c//|/\\|}"
            echo "| $n | $s | $e | \`$c\` | \`$l\` |"
            if [[ "$s" == "Failed" ]]; then
                last_failure_name="$n"
                last_failure_note="$no"
            fi
        done
        echo ""
        if [[ -n "$last_failure_name" ]]; then
            echo "Last failure: **$last_failure_name**"
            echo ""
            echo "$last_failure_note"
        fi
    } > "$REPORT_MD"
}

# ---------------------------------------------------------------------------
# Resume guard
# ---------------------------------------------------------------------------
if [[ "$RESUME" == true ]]; then
    if [[ ! -f "$STATE_PATH" ]]; then
        echo "${RED}--resume was specified, but no previous state exists at $STATE_PATH.${RESET}" >&2
        exit 1
    fi
    prev_fp="$(state_get "sourceFingerprint")"
    if [[ "$FORCE_RESUME" != true && "$prev_fp" != "$FINGERPRINT" ]]; then
        echo "${RED}Source fingerprint changed since the previous run. Rerun without --resume, or use --force-resume to override.${RESET}" >&2
        exit 1
    fi
fi

# ---------------------------------------------------------------------------
# Phases
# ---------------------------------------------------------------------------
run_phase "Asset drift check" \
    "node ./scripts/sync-assets.js -Check" \
    node "./scripts/sync-assets.js" "-Check"

run_phase "Shell script line endings check" \
    "node scripts/check-shell-line-endings.js" \
    node scripts/check-shell-line-endings.js

# Early, fast tripwire: catch real credentials before they reach the public repo.
run_phase "Secret scan" \
    "node scripts/scan-secrets.js" \
    node scripts/scan-secrets.js

restore_dotnet_phase() {
    dotnet restore "ETL-SQL.slnx"
    dotnet tool restore
}

run_phase "Dotnet restore" \
    "dotnet restore ETL-SQL.slnx; dotnet tool restore" \
    restore_dotnet_phase

run_phase "Dependency-audit self-test" \
    "./scripts/Test-DependencyAudit.ps1 (via pwsh)" \
    dependency_audit_selftest_phase

run_phase "NuGet dependency audit" \
    "dotnet list ETL-SQL.slnx package --outdated/--deprecated/--vulnerable (via scripts/lib/DependencyAudit.ps1)" \
    nuget_dependency_audit_phase

run_phase "SBOM generation" \
    "node scripts/generate-sbom.js (component version must match Directory.Build.props)" \
    sbom_generation_phase

run_phase "Third-party inventory drift" \
    "node scripts/generate-third-party-inventory.js --check" \
    node scripts/generate-third-party-inventory.js --check

run_phase "Dotnet build" \
    "dotnet build ETL-SQL.slnx --configuration $CONFIGURATION --no-restore" \
    dotnet build "ETL-SQL.slnx" "--configuration" "$CONFIGURATION" "--no-restore"

run_phase "Test structure audit" \
    "./scripts/Get-TestLaneInventory.ps1 -FailOnIssues" \
    pwsh -NoProfile -File "./scripts/Get-TestLaneInventory.ps1" -FailOnIssues

# Matches the CI 'dotnet format --verify-no-changes' gate so formatting drift fails locally
# (a fast static check) before the long test lanes run. On drift, format_verify_phase applies the
# fix automatically and fails with an actionable message to commit the reformatted files, then re-run.
run_phase "Format verify" \
    "dotnet format ETL-SQL.slnx --verify-no-changes --no-restore (auto-applies 'dotnet format' on drift)" \
    format_verify_phase

if [[ "$EFFECTIVE_SKIP_SCALE" != true ]]; then
    run_phase "Scale certification smoke" \
        "./scripts/test-scale-certification.sh --tier Smoke" \
        bash "./scripts/test-scale-certification.sh" "--tier" "Smoke"

    run_phase "Cert baseline regression check (smoke)" \
        "./scripts/Compare-CertBaseline.ps1 -MarkdownReport $RUN_DIR/cert-baseline-smoke.md (via pwsh)" \
        cert_baseline_phase "$RUN_DIR/cert-baseline-smoke.md"
fi

if [[ "$EFFECTIVE_INCLUDE_STANDARD_SCALE" == true ]]; then
    run_phase "Scale certification standard" \
        "./scripts/test-scale-certification.sh --tier Standard" \
        bash "./scripts/test-scale-certification.sh" "--tier" "Standard"

    run_phase "Cert baseline regression check (standard)" \
        "./scripts/Compare-CertBaseline.ps1 -MarkdownReport $RUN_DIR/cert-baseline-standard.md (via pwsh)" \
        cert_baseline_phase "$RUN_DIR/cert-baseline-standard.md"

    run_phase "Spill allocation budget (10M)" \
        "./scripts/Test-SpillAllocProfile.ps1 -Rows 10000000 -SkipBuild (via pwsh)" \
        spill_allocation_budget_phase
fi

run_phase "Smoke lane" \
    "./scripts/test-lane.sh --lane smoke --configuration $CONFIGURATION --no-restore --no-build" \
    bash "./scripts/test-lane.sh" "--lane" "smoke" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"

run_phase "Fast lane" \
    "./scripts/test-lane.sh --lane fast --configuration $CONFIGURATION --no-restore --no-build" \
    bash "./scripts/test-lane.sh" "--lane" "fast" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"

run_phase "Engine lane and coverage gate" \
    "./scripts/Test-CoverageGate.ps1 -RunEngineLane -CoverageDirectory $OUT_DIR/$RUN_ID/coverage -MinimumLineCoverage 70 -Configuration $CONFIGURATION -NoRestore -NoBuild" \
    pwsh -NoProfile -File "./scripts/Test-CoverageGate.ps1" -RunEngineLane -CoverageDirectory "$OUT_DIR/$RUN_ID/coverage" -MinimumLineCoverage 70 -Configuration "$CONFIGURATION" -NoRestore -NoBuild

run_phase "Portal lane" \
    "./scripts/test-lane.sh --lane portal --configuration $CONFIGURATION --no-restore --no-build" \
    bash "./scripts/test-lane.sh" "--lane" "portal" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"

# Explicit release gate: prove the in-place N->N+1 upgrade drill independently so this named phase
# makes the upgrade gate visible and separately logged.
run_phase "N->N+1 upgrade-path drill" \
    "dotnet test tests/ETL-SQL.Portal.Tests --filter FullyQualifiedName~UpgradePathDrillTests" \
    dotnet test "tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj" "--filter" "FullyQualifiedName~UpgradePathDrillTests" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"

run_phase "Sample scripts" \
    "./scripts/test-all-samples.sh" \
    bash "./scripts/test-all-samples.sh"

run_phase "HA soak contract gate" \
    "./scripts/Test-HaSoakContracts.ps1 (via pwsh)" \
    ha_soak_contract_phase

if [[ "$INCLUDE_SLT" == true ]]; then
    run_phase "SLT lane" \
        "./scripts/test-lane.sh --lane slt --configuration $CONFIGURATION --no-restore --no-build" \
        bash "./scripts/test-lane.sh" "--lane" "slt" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"
fi

if [[ "$EFFECTIVE_SKIP_NODE" != true ]]; then
    run_phase "VS Code npm ci" \
        "npm ci (src/etl-sql-vscode)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode' && npm ci"

    run_phase "VS Code UI npm ci" \
        "npm ci (src/etl-sql-vscode/ui)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode/ui' && npm ci"

    run_phase "VS Code npm audit" \
        "npm outdated / npm audit (src/etl-sql-vscode, src/etl-sql-vscode/ui)" \
        npm_dependency_audit_phase

    run_phase "VS Code compile" \
        "npm run compile (src/etl-sql-vscode)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode' && npm run compile"

    run_phase "VS Code lint" \
        "npm run lint (src/etl-sql-vscode)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode' && npm run lint"

    run_phase "VS Code UI lint" \
        "npm run lint (src/etl-sql-vscode/ui)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode/ui' && npm run lint"

    run_phase "VS Code UI build" \
        "npm run build (src/etl-sql-vscode/ui)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode/ui' && npm run build"

    run_phase "VS Code UI unit tests" \
        "npm run test:unit (src/etl-sql-vscode/ui)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode/ui' && npm run test:unit"

    # Exercise the same 'vsce package' the tag-triggered release.yml runs (via publish-vsix.ps1),
    # so packaging/manifest errors are caught locally instead of failing the release build.
    run_phase "VS Code VSIX package" \
        "npx @vscode/vsce package --target linux-x64 (manifest/packaging validation)" \
        vsce_package_phase

    run_phase "VS Code unit tests" \
        "npm run test:unit (src/etl-sql-vscode)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode' && npm run test:unit"
fi

if [[ "$EFFECTIVE_INCLUDE_DOCKER" == true ]]; then
    run_phase "Docker integration lane" \
        "./scripts/test-lane.sh --lane integration --configuration $CONFIGURATION --no-restore --no-build" \
        bash "./scripts/test-lane.sh" "--lane" "integration" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"
fi

if [[ "$EFFECTIVE_BUILD_INSTALLERS" == true ]]; then
    run_phase "Release publish artifacts" \
        "./scripts/publish-release.sh --platforms $PLATFORMS" \
        bash "./scripts/publish-release.sh" "--platforms" "$PLATFORMS"

    if [[ "$PLATFORMS" == *"linux"* ]]; then
        run_phase "Linux packages" \
            "./scripts/build-linux-packages.sh" \
            bash "./scripts/build-linux-packages.sh"
    fi

    if [[ "$PLATFORMS" == *"osx"* ]]; then
        run_phase "macOS DMG" \
            "./scripts/build-mac-dmg.sh" \
            bash "./scripts/build-mac-dmg.sh"
    fi
fi

# ---------------------------------------------------------------------------
# Final report
# ---------------------------------------------------------------------------
save_state "Passed"
write_reports "Passed"

echo ""
echo "${GREEN}${BOLD}Pre-release validation PASSED.${RESET}"
echo "${CYAN}Report: $REPORT_MD${RESET}"
