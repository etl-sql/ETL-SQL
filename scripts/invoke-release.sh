#!/usr/bin/env bash
#
# Mechanical release driver for ETL-SQL (POSIX/bash port of Invoke-Release.ps1).
#
# Runs the mechanical Phases 3-5 of Docs/Release_Checklist.md AFTER Test-PreRelease.ps1 has
# passed and the version bump + CHANGELOG entry are committed on the release branch. It does
# NOT bump versions, author the CHANGELOG, or build artifacts locally (the Release workflow
# builds them in the cloud on the tag).
#
# Usage:
#   ./scripts/invoke-release.sh --version 0.12.0 [options]
#
# Options:
#   --version X.Y.Z            (required) release version, no leading 'v'
#   --notes-file PATH          curated release notes (default: Docs/ReleaseNotes/vX.Y.Z.md, release-notes-vX.Y.Z.md, or CHANGELOG)
#   --remote NAME              git remote (default: origin)
#   --branch NAME              release branch (default: main)
#   --dry-run                  print mutating actions without performing them
#   --force                    override fingerprint mismatch / existing tag guards
#   --skip-prerelease-gate     do not require a Passed Test-PreRelease state
#   --skip-ci-wait             do not wait for CI before tagging
#   --prune-merged-branches    after a verified release: prune stale remote-tracking refs,
#                              safe-delete LOCAL branches merged into --branch, list merged remote
#                              branches (never touches main / dev / release/*)
#   --sign-tag                 sign the release tag (git tag -s)
#   --ci-timeout-minutes N     (default: 30)
#   --release-timeout-minutes N (default: 45)
#   -h, --help                 show this help

set -euo pipefail

VERSION=""
NOTES_FILE=""
REMOTE="origin"
BRANCH="main"
DRY_RUN=0
FORCE=0
SKIP_GATE=0
SKIP_CI=0
PRUNE=0
SIGN_TAG=0
CI_TIMEOUT=30
REL_TIMEOUT=45

usage() { sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'; exit "${1:-0}"; }

while [ $# -gt 0 ]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --notes-file) NOTES_FILE="$2"; shift 2 ;;
    --remote) REMOTE="$2"; shift 2 ;;
    --branch) BRANCH="$2"; shift 2 ;;
    --dry-run) DRY_RUN=1; shift ;;
    --force) FORCE=1; shift ;;
    --skip-prerelease-gate) SKIP_GATE=1; shift ;;
    --skip-ci-wait) SKIP_CI=1; shift ;;
    --prune-merged-branches) PRUNE=1; shift ;;
    --sign-tag) SIGN_TAG=1; shift ;;
    --ci-timeout-minutes) CI_TIMEOUT="$2"; shift 2 ;;
    --release-timeout-minutes) REL_TIMEOUT="$2"; shift 2 ;;
    -h|--help) usage 0 ;;
    *) echo "Unknown argument: $1" >&2; usage 1 ;;
  esac
done

# ---- colors / output ------------------------------------------------------
if [ -t 1 ]; then C_CYAN='\033[36m'; C_GRAY='\033[90m'; C_GREEN='\033[32m'; C_YELLOW='\033[33m'; C_RST='\033[0m'
else C_CYAN=''; C_GRAY=''; C_GREEN=''; C_YELLOW=''; C_RST=''; fi
step() { printf "\n${C_CYAN}==> %s${C_RST}\n" "$1"; }
info() { printf "${C_GRAY}    %s${C_RST}\n" "$1"; }
ok()   { printf "${C_GREEN}    OK  %s${C_RST}\n" "$1"; }
warn() { printf "${C_YELLOW}    WARN %s${C_RST}\n" "$1"; }
die()  { printf "Error: %s\n" "$1" >&2; exit 1; }

[ -n "$VERSION" ] || die "--version is required."
echo "$VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$' || die "Invalid version '$VERSION'."
TAG="v$VERSION"

need() { command -v "$1" >/dev/null 2>&1 || die "Required tool '$1' is not on PATH."; }
need git; need gh
REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

sha256_hex() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then shasum -a 256 | awk '{print $1}'
  else die "Need sha256sum or shasum for the pre-release gate."; fi
}

# Run a mutating command, or just describe it under --dry-run.
mut() {
  local desc="$1"; shift
  if [ "$DRY_RUN" -eq 1 ]; then info "[dry-run] would: $desc"; return 0; fi
  "$@"
}

# ---- 1. pre-flight --------------------------------------------------------
step "Pre-flight"
# Check the ACTIVE account via the API: `gh auth status` exits non-zero if any configured account
# has a bad token, even when the active one is fine, so it is not a reliable gate.
GH_USER="$(gh api user --jq .login 2>/dev/null || true)"
[ -n "$GH_USER" ] || die "gh is not authenticated for the active account (run: gh auth login)."
ok "git + gh present (gh user: $GH_USER)"

CUR_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
[ "$CUR_BRANCH" = "$BRANCH" ] || die "On branch '$CUR_BRANCH' but release branch is '$BRANCH'."
[ -z "$(git status --porcelain)" ] || die "Working tree is not clean. Commit or stash first."
ok "clean working tree on '$BRANCH'"

git fetch "$REMOTE" --tags >/dev/null 2>&1 || true
LOCAL_SHA="$(git rev-parse HEAD)"
if git rev-parse "$REMOTE/$BRANCH" >/dev/null 2>&1; then
  BEHIND="$(git rev-list --count "HEAD..$REMOTE/$BRANCH")"
  [ "$BEHIND" -eq 0 ] || die "Local '$BRANCH' is $BEHIND commit(s) behind '$REMOTE/$BRANCH'. Pull first."
fi
ok "release commit ${LOCAL_SHA:0:8}"

# ---- 2. pre-release gate --------------------------------------------------
step "Pre-release validation gate"
STATE="release-validation/latest/state.json"
if [ "$SKIP_GATE" -eq 1 ]; then
  warn "skipped (--skip-prerelease-gate)"
elif [ ! -f "$STATE" ]; then
  die "No pre-release state at $STATE. Run Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration first (or --skip-prerelease-gate)."
else
  if command -v jq >/dev/null 2>&1; then
    STATUS="$(jq -r '.status' "$STATE")"
    SAVED_FP="$(jq -r '.sourceFingerprint' "$STATE")"
  else
    STATUS="$(grep -o '"status"[[:space:]]*:[[:space:]]*"[^"]*"' "$STATE" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')"
    SAVED_FP="$(grep -o '"sourceFingerprint"[[:space:]]*:[[:space:]]*"[^"]*"' "$STATE" | head -1 | sed 's/.*"\([^"]*\)"$/\1/')"
  fi
  [ "$STATUS" = "Passed" ] || die "Latest pre-release status is '$STATUS', not 'Passed'. Re-run Test-PreRelease.ps1."
  HEAD_LINE="$(git rev-parse HEAD)"
  STATUS_LINE="$(git status --short)"
  CUR_FP="$(printf '%s\n%s' "$HEAD_LINE" "$STATUS_LINE" | sha256_hex)"
  if [ "$SAVED_FP" != "$CUR_FP" ]; then
    if [ "$FORCE" -eq 1 ]; then warn "fingerprint mismatch [overridden by --force]"
    else die "Pre-release state was recorded for a different source state. Re-validate this commit, or use --force."; fi
  else
    ok "validation Passed for this commit"
  fi
fi

# ---- 3. version consistency ----------------------------------------------
step "Version consistency ($VERSION)"
check_src() { # file needle
  [ -f "$1" ] || { echo "$1 (missing)"; return; }
  grep -Fq -- "$2" "$1" || echo "$1"
}
MISS=""
MISS+="$(check_src 'Directory.Build.props'           "<VersionPrefix>$VERSION</VersionPrefix>")
$(check_src 'src/etl-sql-vscode/package.json'        "\"version\": \"$VERSION\"")
$(check_src 'scripts/build-msi.ps1'                  "} else { \"$VERSION\" }")
$(check_src 'scripts/build-vsix.ps1'                 "\$Version = \"$VERSION\"")
$(check_src 'scripts/publish-release.ps1'            "} else { \"$VERSION\" }")
$(check_src 'scripts/Master-Release.ps1'             "\$Version = \"$VERSION\"")"
MISS="$(printf '%s\n' "$MISS" | grep -v '^[[:space:]]*$' || true)"
if [ -n "$MISS" ]; then
  die "Version $VERSION not found in:
$MISS
Run Set-Version.ps1 -Version $VERSION."
fi
ok "all six version sources read $VERSION"

# ---- 4. resolve release notes --------------------------------------------
step "Release notes"
TEMP_NOTES=""
if [ -n "$NOTES_FILE" ]; then
  [ -f "$NOTES_FILE" ] || die "--notes-file '$NOTES_FILE' not found."
  NOTES_PATH="$NOTES_FILE"; ok "using $NOTES_FILE"
elif [ -f "Docs/ReleaseNotes/$TAG.md" ]; then
  NOTES_PATH="Docs/ReleaseNotes/$TAG.md"; ok "using Docs/ReleaseNotes/$TAG.md"
elif [ -f "release-notes-$TAG.md" ]; then
  NOTES_PATH="release-notes-$TAG.md"; ok "using release-notes-$TAG.md"
else
  [ -f CHANGELOG.md ] || die "No --notes-file, no release-notes-$TAG.md, and no CHANGELOG.md."
  # Extract the '## [VERSION]' section up to the next '## ['.
  BODY="$(awk -v v="[$VERSION]" '
    /^##[[:space:]]+\[/ { if (insec) exit; if (index($0,v)) { insec=1; next } }
    insec { print }
  ' CHANGELOG.md | sed -e 's/[[:space:]]*$//')"
  BODY="$(printf '%s' "$BODY" | sed -e :a -e '/^\n*$/{$d;N;ba}')"  # trim trailing blank lines
  [ -n "$BODY" ] || die "Could not find a '## [$VERSION]' section in CHANGELOG.md."
  NOTES_PATH="$(mktemp -t "etlsql-notes-$TAG.XXXXXX.md")"
  TEMP_NOTES="$NOTES_PATH"
  { printf '## ETL-SQL %s\n\n' "$TAG"; printf '%s\n' "$BODY"; } > "$NOTES_PATH"
  ok "extracted CHANGELOG [$VERSION] section"
fi

# ---- 5. stale ref guard ---------------------------------------------------
step "Tag/branch guard for $TAG"
REMOTE_TAG="$(git ls-remote --tags "$REMOTE" "refs/tags/$TAG" 2>/dev/null)"
if [ -n "$REMOTE_TAG" ]; then
  [ "$FORCE" -eq 1 ] || die "Tag $TAG already exists on $REMOTE (already released?). Re-run with --force to continue a partial release."
  warn "tag $TAG already on $REMOTE [continuing under --force]"
fi
if [ -n "$(git branch --list "$TAG")" ]; then
  UNMERGED="$(git rev-list --count "$BRANCH..refs/heads/$TAG" 2>/dev/null || echo 0)"
  if [ "$UNMERGED" -eq 0 ]; then
    mut "delete fully-merged stale branch '$TAG'" git branch -D "$TAG"
    ok "deleted stale local branch '$TAG'"
  else
    die "Local branch '$TAG' has $UNMERGED unmerged commit(s); it collides with the tag."
  fi
fi
LOCAL_TAG="$(git tag --list "$TAG")"
if [ -n "$LOCAL_TAG" ]; then
  [ "$FORCE" -eq 1 ] || die "Local tag $TAG already exists. Delete it or use --force."
  warn "local tag $TAG already exists [continuing under --force]"
fi
{ [ -z "$REMOTE_TAG" ] && [ -z "$LOCAL_TAG" ] && ok "no conflicting refs"; } || true

# ---- 6. push branch + wait for CI ----------------------------------------
step "Push '$BRANCH' and wait for CI"
mut "git push $REMOTE $BRANCH" git push "$REMOTE" "$BRANCH"
[ "$DRY_RUN" -eq 1 ] || ok "pushed $BRANCH"

wait_for_run() { # workflow sha timeout_min label  -> echoes "id" ; returns 0 ok / 1 fail
  local workflow="$1" sha="$2" tmin="$3" label="$4"
  local deadline=$(( $(date +%s) + tmin*60 ))
  local line id status conclusion
  while [ "$(date +%s)" -lt "$deadline" ]; do
    line="$(gh run list --workflow "$workflow" --json databaseId,headSha,status,conclusion -L 30 \
      --jq ".[] | select(.headSha==\"$sha\") | \"\(.databaseId)\t\(.status)\t\(.conclusion)\"" 2>/dev/null | head -1)"
    if [ -n "$line" ]; then
      id="$(printf '%s' "$line" | cut -f1)"
      status="$(printf '%s' "$line" | cut -f2)"
      conclusion="$(printf '%s' "$line" | cut -f3)"
      if [ "$status" = "completed" ]; then
        printf '%s' "$id"
        [ "$conclusion" = "success" ] && return 0 || return 1
      fi
      info "$label run $id : $status..."
    else
      info "$label run not registered yet..."
    fi
    sleep 30
  done
  printf '%s' "${id:-}"; return 1
}

if [ "$SKIP_CI" -eq 1 ]; then
  warn "CI wait skipped (--skip-ci-wait)"
elif [ "$DRY_RUN" -eq 1 ]; then
  info "[dry-run] would wait for CI on ${LOCAL_SHA:0:8}"
else
  if CI_ID="$(wait_for_run ci.yml "$LOCAL_SHA" "$CI_TIMEOUT" CI)"; then
    ok "CI green (run $CI_ID)"
  else
    die "CI did not pass (run ${CI_ID:-?}). Aborting before tag."
  fi
fi

# ---- 7. tag + push tag (idempotent for re-runs) --------------------------
step "Tag $TAG"
if EXIST_SHA="$(git rev-list -n 1 "$TAG" 2>/dev/null)"; then
  if [ "$EXIST_SHA" = "$LOCAL_SHA" ]; then info "tag $TAG already at release commit; not recreating"
  else die "tag $TAG exists at a different commit ${EXIST_SHA:0:8}. Delete it before releasing."; fi
else
  # Sign only when explicitly requested. A configured signing key is not enough: machines often
  # have stale git signing config without an available private key or agent, and release tagging
  # should not fail unexpectedly.
  if [ "$SIGN_TAG" -eq 1 ]; then TAG_FLAG="-s"; else TAG_FLAG="-a"; fi
  mut "git tag $TAG_FLAG $TAG ${LOCAL_SHA:0:8}" git tag "$TAG_FLAG" "$TAG" -m "ETL-SQL $TAG" "$LOCAL_SHA"
fi
mut "git push $REMOTE refs/tags/$TAG" git push "$REMOTE" "refs/tags/$TAG"
[ "$DRY_RUN" -eq 1 ] || ok "tag $TAG pushed (Release workflow triggered)"

# ---- 8. wait for draft + apply notes -------------------------------------
step "Apply curated release notes"
if [ "$DRY_RUN" -eq 1 ]; then
  info "[dry-run] would wait for draft and run: gh release edit $TAG --notes-file $NOTES_PATH"
else
  deadline=$(( $(date +%s) + 15*60 )); found=0
  while [ "$(date +%s)" -lt "$deadline" ]; do
    if gh release view "$TAG" >/dev/null 2>&1; then found=1; break; fi
    info "waiting for draft release..."; sleep 30
  done
  if [ "$found" -eq 1 ]; then
    gh release edit "$TAG" --notes-file "$NOTES_PATH" >/dev/null
    ok "curated notes applied"
  else
    warn "draft not seen yet; set later: gh release edit $TAG --notes-file <file>"
  fi
fi

# ---- 9. watch Release workflow + verify assets ---------------------------
step "Watch Release workflow"
RELEASE_OK=1
if [ "$DRY_RUN" -eq 1 ]; then
  info "[dry-run] would watch release.yml and verify assets"
else
  if REL_ID="$(wait_for_run release.yml "$LOCAL_SHA" "$REL_TIMEOUT" Release)"; then
    ok "Release workflow succeeded (run $REL_ID)"
  else
    warn "Release workflow did not succeed (run ${REL_ID:-?})."; RELEASE_OK=0
  fi

  step "Verify published release"
  IS_DRAFT="$(gh release view "$TAG" --json isDraft --jq .isDraft 2>/dev/null || echo true)"
  [ "$IS_DRAFT" = "false" ] && ok "release is published" || warn "release is still a DRAFT."
  ASSETS="$(gh release view "$TAG" --json assets --jq '.assets[].name' 2>/dev/null || true)"
  REQUIRED="ETL-SQL-$TAG-win-x64.zip
ETL-SQL-$TAG-x64-Setup.msi
ETL-SQL-$TAG-linux-x64.zip
etl-sql_${VERSION}_amd64.deb
ETL-SQL-$TAG-osx-arm64.zip"
  MISSING=""
  while IFS= read -r req; do
    [ -z "$req" ] && continue
    printf '%s\n' "$ASSETS" | grep -Fxq "$req" || MISSING="$MISSING $req"
  done <<EOF
$REQUIRED
EOF
  printf '%s\n' "$ASSETS" | grep -q '\.vsix$' || MISSING="$MISSING *.vsix"
  if [ -n "$MISSING" ]; then warn "missing required assets:$MISSING"; RELEASE_OK=0
  else ok "all required assets present"; fi
  for opt in "ETL-SQL-$TAG-osx-x64.zip" "ETL-SQL_${TAG}.dmg"; do
    printf '%s\n' "$ASSETS" | grep -Fxq "$opt" || info "best-effort asset absent (ok): $opt"
  done
fi

# ---- 9b. attach verification assets (sha256sums + sbom) -------------------
# release.yml uploads only the platform binaries; attach the CycloneDX SBOM (version-stamped
# locally from Directory.Build.props) and a checksum manifest over the cloud-built assets so the
# published release carries both verification assets (Release_Checklist Phase 5).
step "Attach sha256sums + sbom"
if [ "$DRY_RUN" -eq 1 ]; then
  info "[dry-run] would generate sbom.json + sha256sums.txt and upload them to $TAG"
elif [ "$RELEASE_OK" -ne 1 ]; then
  warn "release not verified; skipping checksum/sbom attach"
else
  node scripts/generate-sbom.js || die "generate-sbom.js failed."
  [ -f "$REPO_ROOT/release/sbom.json" ] || die "generate-sbom.js did not produce release/sbom.json."
  WORK="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf '$WORK'" EXIT
  gh release download "$TAG" --dir "$WORK" \
    --pattern '*.zip' --pattern '*.msi' --pattern '*.deb' --pattern '*.dmg' --pattern '*.vsix' \
    || die "failed to download published $TAG assets."
  (
    cd "$WORK" || die "cannot enter $WORK"
    # Bash glob expansion is already collation-sorted; skip the two files we are creating.
    for f in *; do
      [ -f "$f" ] || continue
      case "$f" in sha256sums.txt | sbom.json) continue ;; esac
      printf '%s  %s\n' "$(sha256_hex < "$f")" "$f"
    done > sha256sums.txt
  )
  gh release upload "$TAG" "$WORK/sha256sums.txt" "$REPO_ROOT/release/sbom.json" --clobber \
    || die "failed to upload sha256sums.txt + sbom.json."
  rm -rf "$WORK"; trap - EXIT
  ok "attached sha256sums.txt + sbom.json"
fi

# ---- 9c. optional: prune merged branches (opt-in) ------------------------
# Sweep up the sprint's branches so they don't pile up release over release. Non-destructive by
# default: prunes stale remote-tracking refs, safe-deletes LOCAL branches already merged into
# $BRANCH ('git branch -d' refuses unmerged), and only PRINTS the delete command for merged REMOTE
# branches (deleting a shared ref stays a deliberate, reviewed action). Protected: the release
# branch itself, main, dev, and anything under release/.
if [ "$PRUNE" -eq 1 ]; then
  step "Prune merged branches"
  if [ "$RELEASE_OK" -ne 1 ] && [ "$DRY_RUN" -ne 1 ]; then
    warn "release not verified; skipping branch prune"
  else
    is_protected() { # name -> 0 (true) if protected
      case "$1" in
        "$BRANCH" | main | dev) return 0 ;;
        release/*) return 0 ;;
        *) return 1 ;;
      esac
    }

    mut "git remote prune $REMOTE" git remote prune "$REMOTE"
    [ "$DRY_RUN" -eq 1 ] || ok "pruned stale remote-tracking refs for $REMOTE"

    ANY_LOCAL=0
    while IFS= read -r b; do
      [ -n "$b" ] || continue
      is_protected "$b" && continue
      mut "delete local merged branch '$b'" git branch -d "$b"
      [ "$DRY_RUN" -eq 1 ] || ok "deleted local branch '$b'"
      ANY_LOCAL=1
    done <<EOF
$(git branch --merged "$BRANCH" --format '%(refname:short)')
EOF
    [ "$ANY_LOCAL" -eq 1 ] || info "no local merged branches to delete"

    ANY_REMOTE=0
    while IFS= read -r rb; do
      [ -n "$rb" ] || continue
      case "$rb" in */HEAD) continue ;; esac
      b="${rb#"$REMOTE"/}"
      is_protected "$b" && continue
      if [ "$ANY_REMOTE" -eq 0 ]; then warn "remote branches merged into $BRANCH (review, then delete):"; ANY_REMOTE=1; fi
      info "git push $REMOTE --delete $b"
    done <<EOF
$(git branch -r --merged "$REMOTE/$BRANCH" --format '%(refname:short)')
EOF
    [ "$ANY_REMOTE" -eq 1 ] || info "no remote merged branches to review"
  fi
fi

[ -n "$TEMP_NOTES" ] && rm -f "$TEMP_NOTES" || true

# ---- 10. summary ----------------------------------------------------------
printf "\n${C_CYAN}=======================================================${C_RST}\n"
if [ "$DRY_RUN" -eq 1 ]; then STATE_TXT="(dry-run)"; elif [ "$RELEASE_OK" -eq 1 ]; then STATE_TXT="COMPLETE"; else STATE_TXT="NEEDS ATTENTION"; fi
printf "${C_CYAN} RELEASE %s %s${C_RST}\n" "$TAG" "$STATE_TXT"
printf "${C_CYAN}=======================================================${C_RST}\n\n"
printf "${C_YELLOW}Manual tail (see Docs/Release_Checklist.md Phase 5):${C_RST}\n"
echo "  - Spot-install one artifact (MSI on Windows) and confirm it launches / services start."
echo "  - Review run annotations (e.g. macOS Intel skipped) and accept/reject."
echo "  - Announce the release and update any external links."
echo ""
echo "Release page: $(gh release view "$TAG" --json url --jq .url 2>/dev/null || echo "(pending)")"

[ "$RELEASE_OK" -eq 1 ] || [ "$DRY_RUN" -eq 1 ] || exit 1
exit 0
