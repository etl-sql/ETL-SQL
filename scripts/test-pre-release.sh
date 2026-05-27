#!/usr/bin/env bash
# test-pre-release.sh — local-first pre-release validation gate
# Bash equivalent of Test-PreRelease.ps1
#
# Usage:
#   ./scripts/test-pre-release.sh
#   ./scripts/test-pre-release.sh --resume
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
INCLUDE_STANDARD_SCALE=false
BUILD_INSTALLERS=false
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
        --include-standard-scale)    INCLUDE_STANDARD_SCALE=true; shift ;;
        --build-installers)          BUILD_INSTALLERS=true; shift ;;
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

mkdir -p "$RUN_DIR"

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

run_phase "Dotnet restore" \
    "dotnet restore ETL-SQL.slnx" \
    dotnet restore "ETL-SQL.slnx"

run_phase "Dotnet build" \
    "dotnet build ETL-SQL.slnx --configuration $CONFIGURATION --no-restore" \
    dotnet build "ETL-SQL.slnx" "--configuration" "$CONFIGURATION" "--no-restore"

run_phase "Smoke lane" \
    "./scripts/test-lane.sh --lane smoke --configuration $CONFIGURATION --no-restore --no-build" \
    bash "./scripts/test-lane.sh" "--lane" "smoke" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"

run_phase "Fast lane" \
    "./scripts/test-lane.sh --lane fast --configuration $CONFIGURATION --no-restore --no-build" \
    bash "./scripts/test-lane.sh" "--lane" "fast" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"

if [[ "$SKIP_NODE" != true ]]; then
    run_phase "VS Code npm ci" \
        "npm ci (src/etl-sql-vscode)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode' && npm ci"

    run_phase "VS Code npm audit" \
        "npm outdated / npm audit (src/etl-sql-vscode, src/etl-sql-vscode/ui)" \
        npm_dependency_audit_phase

    run_phase "VS Code compile" \
        "npm run compile (src/etl-sql-vscode)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode' && npm run compile"

    run_phase "VS Code unit tests" \
        "npm run test:unit (src/etl-sql-vscode)" \
        bash -c "cd '$REPO_ROOT/src/etl-sql-vscode' && npm run test:unit"
fi

if [[ "$SKIP_SCALE" != true ]]; then
    run_phase "Scale certification smoke" \
        "./scripts/test-scale-certification.sh --tier Smoke" \
        bash "./scripts/test-scale-certification.sh" "--tier" "Smoke"
fi

if [[ "$INCLUDE_DOCKER" == true ]]; then
    run_phase "Docker integration lane" \
        "./scripts/test-lane.sh --lane integration --configuration $CONFIGURATION --no-restore --no-build" \
        bash "./scripts/test-lane.sh" "--lane" "integration" "--configuration" "$CONFIGURATION" "--no-restore" "--no-build"
fi

if [[ "$INCLUDE_STANDARD_SCALE" == true ]]; then
    run_phase "Scale certification standard" \
        "./scripts/test-scale-certification.sh --tier Standard" \
        bash "./scripts/test-scale-certification.sh" "--tier" "Standard"
fi

if [[ "$BUILD_INSTALLERS" == true ]]; then
    run_phase "Release publish artifacts" \
        "./scripts/publish_release.sh --platforms $PLATFORMS" \
        bash "./scripts/publish_release.sh" "--platforms" "$PLATFORMS"

    if [[ "$PLATFORMS" == *"linux"* ]]; then
        run_phase "Linux packages" \
            "./scripts/build_linux_packages.sh" \
            bash "./scripts/build_linux_packages.sh"
    fi

    if [[ "$PLATFORMS" == *"osx"* ]]; then
        run_phase "macOS DMG" \
            "./scripts/build_mac_dmg.sh" \
            bash "./scripts/build_mac_dmg.sh"
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
