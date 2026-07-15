<#
.SYNOPSIS
    Mechanical release driver for ETL-SQL: lands main, tags, sets curated notes, and
    watches the GitHub Release workflow to a verified, published result.

.DESCRIPTION
    Runs the mechanical Phases 3-5 of Docs/Release_Checklist.md AFTER Test-PreRelease.ps1
    has passed and the version bump + CHANGELOG entry are committed on main. It does NOT
    bump versions (use Set-Version.ps1), author the CHANGELOG, or build artifacts locally
    (the Release workflow builds them in the cloud on the tag).

    Steps:
      1. Pre-flight guards: tools present, clean tree, on the release branch, in sync with origin.
      2. Pre-release gate: release-validation/latest/state.json is Passed for the current commit.
      3. Version consistency across the six version sources.
      4. Resolve release notes (-NotesFile, release-notes-vX.Y.Z.md, or the CHANGELOG section).
      5. Stale ref guard: no remote/local tag vX.Y.Z; delete a fully-merged stale local branch.
      6. Push the branch; wait for CI to go green for the pushed commit.
      7. Create + push the release tag (full ref to avoid ambiguity). Signed (-s) when -SignTag is
         passed; annotated (-a) otherwise.
      8. Wait for the draft release; apply the curated notes.
      9. Watch the Release workflow to completion; verify expected assets are attached.
      9b. (opt-in, -PruneMergedBranches) after a verified release: prune stale remote-tracking refs,
          safe-delete LOCAL branches already merged into $Branch, and LIST merged remote branches
          for a reviewed manual delete. Never touches main / dev / release/*.
     10. Print a summary + the post-release manual checklist.

    Re-runnable: each mutating step is skipped when already satisfied, so a failed run can be
    re-invoked after fixing the cause.

.EXAMPLE
    .\scripts\Invoke-Release.ps1 -Version 0.12.0

.EXAMPLE
    .\scripts\Invoke-Release.ps1 -Version 0.12.0 -DryRun

.EXAMPLE
    .\scripts\Invoke-Release.ps1 -Version 0.12.0 -PruneMergedBranches

.EXAMPLE
    .\scripts\Invoke-Release.ps1 -Version 0.12.0 -NotesFile .\Docs\ReleaseNotes\v0.12.0.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$NotesFile,
    [string]$Remote = "origin",
    [string]$Branch = "main",

    [switch]$DryRun,
    [switch]$Force,
    [switch]$SkipPreReleaseGate,
    [switch]$SkipCiWait,
    [switch]$PruneMergedBranches,
    [switch]$SignTag,
    [int]$CiTimeoutMinutes = 30,
    [int]$ReleaseTimeoutMinutes = 45
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (& git -C $ScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $RepoRoot) { throw "Not inside a git repository." }
Set-Location $RepoRoot

$Tag = "v$Version"

# ---- output helpers -------------------------------------------------------
function Write-Step { param([string]$m) Write-Host "`n==> $m" -ForegroundColor Cyan }
function Write-Info { param([string]$m) Write-Host "    $m" -ForegroundColor Gray }
function Write-Ok   { param([string]$m) Write-Host "    OK  $m" -ForegroundColor Green }
function Write-WarnLine { param([string]$m) Write-Host "    WARN $m" -ForegroundColor Yellow }
function Fail { param([string]$m) throw $m }

function Test-Tool {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Fail "Required tool '$Name' is not on PATH."
    }
}

# Invoke a native command, honoring -DryRun for mutating calls.
function Invoke-Native {
    param(
        [scriptblock]$Action,
        [string]$Describe,
        [switch]$Mutating
    )
    if ($Mutating -and $DryRun) {
        Write-Info "[dry-run] would: $Describe"
        return ""
    }
    $out = & $Action 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ($out | Out-String) -ForegroundColor DarkGray
        Fail "$Describe failed (exit $LASTEXITCODE)."
    }
    return $out
}

# Replicates Test-PreRelease.ps1 Get-SourceFingerprint exactly so the gate compares like-for-like.
function Get-SourceFingerprint {
    $head = (& git rev-parse HEAD 2>$null) -join "`n"
    $status = (& git status --short 2>$null) -join "`n"
    $text = "$head`n$status"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
        return -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") })
    }
    finally { $sha.Dispose() }
}

# ---- 1. pre-flight guards -------------------------------------------------
Write-Step "Pre-flight"
Test-Tool git
Test-Tool gh
# Check the ACTIVE account can hit the API. `gh auth status` exits non-zero if *any* configured
# account has a bad token, even when the active one is fine, so it's not a reliable gate.
$ghUser = (& gh api user --jq .login 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $ghUser) { Fail "gh is not authenticated for the active account (run: gh auth login)." }
Write-Ok "git + gh present (gh user: $ghUser)"

$currentBranch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($currentBranch -ne $Branch) {
    Fail "On branch '$currentBranch' but release branch is '$Branch'. Checkout '$Branch' first."
}

$dirty = (& git status --porcelain)
if ($dirty) {
    Write-Host ($dirty | Out-String) -ForegroundColor DarkGray
    Fail "Working tree is not clean. Commit or stash before releasing."
}
Write-Ok "clean working tree on '$Branch'"

Invoke-Native { git fetch $Remote --tags } "git fetch $Remote --tags" | Out-Null
$localSha = (& git rev-parse HEAD).Trim()
$remoteSha = (& git rev-parse --verify --quiet "$Remote/$Branch" 2>$null)
if ($LASTEXITCODE -eq 0 -and $remoteSha) {
    $behind = (& git rev-list --count "HEAD..$Remote/$Branch").Trim()
    if ([int]$behind -gt 0) {
        Fail "Local '$Branch' is $behind commit(s) behind '$Remote/$Branch'. Pull/rebase first."
    }
}
Write-Ok "release commit $($localSha.Substring(0,8))"

# ---- 2. pre-release validation gate --------------------------------------
Write-Step "Pre-release validation gate"
$statePath = Join-Path $RepoRoot "release-validation/latest/state.json"
if ($SkipPreReleaseGate) {
    Write-WarnLine "skipped (-SkipPreReleaseGate)"
}
elseif (-not (Test-Path $statePath)) {
    Fail "No pre-release state at $statePath. Run Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration first (or -SkipPreReleaseGate)."
}
else {
    $state = Get-Content $statePath -Raw | ConvertFrom-Json
    if ($state.status -ne "Passed") {
        Fail "Latest pre-release run status is '$($state.status)', not 'Passed'. Fix and re-run Test-PreRelease.ps1."
    }
    $fp = Get-SourceFingerprint
    if ($state.sourceFingerprint -ne $fp) {
        $msg = "Pre-release state was recorded for a different source state (fingerprint mismatch). Re-validate this exact commit."
        if ($Force) { Write-WarnLine "$msg [overridden by -Force]" }
        else { Fail "$msg Use -Force to override." }
    }
    else {
        Write-Ok "validation Passed for this commit (run $($state.runId))"
    }
}

# ---- 3. version consistency ----------------------------------------------
Write-Step "Version consistency ($Version)"
$versionSources = @(
    @{ File = "Directory.Build.props";                 Needle = "<VersionPrefix>$Version</VersionPrefix>" }
    @{ File = "src/etl-sql-vscode/package.json";       Needle = "`"version`": `"$Version`"" }
    @{ File = "scripts/build_msi.ps1";                 Needle = "} else { `"$Version`" }" }
    @{ File = "scripts/build_vsix.ps1";                Needle = "`$Version = `"$Version`"" }
    @{ File = "scripts/publish_release.ps1";           Needle = "} else { `"$Version`" }" }
    @{ File = "scripts/Master-Release.ps1";            Needle = "`$Version = `"$Version`"" }
)
$mismatch = @()
foreach ($s in $versionSources) {
    $path = Join-Path $RepoRoot $s.File
    if (-not (Test-Path $path)) { $mismatch += "$($s.File) (missing)"; continue }
    if (-not (Select-String -LiteralPath $path -SimpleMatch -Pattern $s.Needle -Quiet)) {
        $mismatch += $s.File
    }
}
if ($mismatch.Count -gt 0) {
    Fail "Version $Version not found in: $($mismatch -join ', '). Run Set-Version.ps1 -Version $Version."
}
Write-Ok "all six version sources read $Version"

# ---- 4. resolve release notes --------------------------------------------
Write-Step "Release notes"
$notesPath = $null
$tempNotes = $false
if ($NotesFile) {
    if (-not (Test-Path $NotesFile)) { Fail "-NotesFile '$NotesFile' not found." }
    $notesPath = (Resolve-Path $NotesFile).Path
    Write-Ok "using $NotesFile"
}
elseif (Test-Path (Join-Path $RepoRoot "release-notes-$Tag.md")) {
    $notesPath = Join-Path $RepoRoot "release-notes-$Tag.md"
    Write-Ok "using release-notes-$Tag.md"
}
else {
    # Extract the CHANGELOG [version] section.
    $changelog = Join-Path $RepoRoot "CHANGELOG.md"
    if (-not (Test-Path $changelog)) { Fail "No -NotesFile, no release-notes-$Tag.md, and no CHANGELOG.md to extract from." }
    $lines = Get-Content $changelog
    $section = New-Object System.Collections.Generic.List[string]
    $inSection = $false
    foreach ($line in $lines) {
        if ($line -match '^##\s+\[') {
            if ($inSection) { break }
            if ($line -match "^##\s+\[$([regex]::Escape($Version))\]") { $inSection = $true; continue }
        }
        if ($inSection) { $section.Add($line) }
    }
    $body = ($section -join "`n").Trim()
    if (-not $body) { Fail "Could not find a '## [$Version]' section in CHANGELOG.md." }
    $notesPath = Join-Path ([System.IO.Path]::GetTempPath()) "etlsql-notes-$Tag.md"
    "## ETL-SQL $Tag`n`n$body" | Set-Content -Path $notesPath -Encoding UTF8
    $tempNotes = $true
    Write-Ok "extracted CHANGELOG [$Version] section"
}

# ---- 5. stale ref guard ---------------------------------------------------
Write-Step "Tag/branch guard for $Tag"
$remoteTag = (& git ls-remote --tags $Remote "refs/tags/$Tag" 2>$null)
if ($remoteTag) {
    if (-not $Force) { Fail "Tag $Tag already exists on $Remote (already released?). Re-run with -Force to continue a partial release." }
    Write-WarnLine "tag $Tag already on $Remote [continuing under -Force]"
}
$localBranchSameName = (& git branch --list $Tag)
if ($localBranchSameName) {
    $unmerged = (& git rev-list --count "$Branch..refs/heads/$Tag" 2>$null)
    if (([int]$unmerged) -eq 0) {
        Invoke-Native { git branch -D $Tag } "delete fully-merged stale branch '$Tag'" -Mutating | Out-Null
        Write-Ok "deleted stale local branch '$Tag' (fully merged into $Branch)"
    }
    else {
        Fail "A local branch named '$Tag' has $unmerged unmerged commit(s); it collides with the tag. Resolve it manually."
    }
}
$localTag = (& git tag --list $Tag)
if ($localTag) {
    if (-not $Force) { Fail "Local tag $Tag already exists. Delete it or use -Force." }
    Write-WarnLine "local tag $Tag already exists [continuing under -Force]"
}
if (-not $remoteTag -and -not $localTag) { Write-Ok "no conflicting refs" }

# ---- 6. push branch + wait for CI ----------------------------------------
Write-Step "Push '$Branch' and wait for CI"
Invoke-Native { git push $Remote $Branch } "git push $Remote $Branch" -Mutating | Out-Null
if (-not $DryRun) { Write-Ok "pushed $Branch" }

function Wait-ForRun {
    param([string]$Workflow, [string]$Sha, [int]$TimeoutMinutes, [string]$Label)
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $runId = $null
    while ((Get-Date) -lt $deadline) {
        $runs = gh run list --workflow $Workflow --json databaseId,headSha,status,conclusion,event -L 30 2>$null | ConvertFrom-Json
        $run = $runs | Where-Object { $_.headSha -eq $Sha } | Select-Object -First 1
        if ($run) {
            $runId = $run.databaseId
            if ($run.status -eq "completed") {
                if ($run.conclusion -eq "success") { return @{ Id = $runId; Ok = $true } }
                return @{ Id = $runId; Ok = $false; Conclusion = $run.conclusion }
            }
            Write-Info "$Label run $runId : $($run.status)..."
        }
        else {
            Write-Info "$Label run not registered yet..."
        }
        Start-Sleep -Seconds 30
    }
    return @{ Id = $runId; Ok = $false; Conclusion = "timeout" }
}

if ($SkipCiWait) {
    Write-WarnLine "CI wait skipped (-SkipCiWait)"
}
elseif ($DryRun) {
    Write-Info "[dry-run] would wait for CI on $($localSha.Substring(0,8))"
}
else {
    $ci = Wait-ForRun -Workflow "ci.yml" -Sha $localSha -TimeoutMinutes $CiTimeoutMinutes -Label "CI"
    if (-not $ci.Ok) { Fail "CI did not pass (conclusion: $($ci.Conclusion), run $($ci.Id)). Aborting before tag." }
    Write-Ok "CI green (run $($ci.Id))"
}

# ---- 7. tag + push tag (idempotent for re-runs) --------------------------
Write-Step "Tag $Tag"
$existingTagSha = (& git rev-list -n 1 $Tag 2>$null)
if ($LASTEXITCODE -eq 0 -and $existingTagSha) {
    if ($existingTagSha.Trim() -eq $localSha) { Write-Info "tag $Tag already at release commit; not recreating" }
    else { Fail "tag $Tag exists at a different commit ($($existingTagSha.Substring(0,8))). Delete it before releasing." }
}
else {
    # Sign only when explicitly requested. A configured signing key is not enough: machines often
    # have stale git signing config without an available private key or agent, and release tagging
    # should not fail unexpectedly.
    $tagFlag = if ($SignTag) { "-s" } else { "-a" }
    Invoke-Native { git tag $tagFlag $Tag -m "ETL-SQL $Tag" $localSha } "git tag $tagFlag $Tag $($localSha.Substring(0,8))" -Mutating | Out-Null
}
Invoke-Native { git push $Remote "refs/tags/$Tag" } "git push $Remote refs/tags/$Tag" -Mutating | Out-Null
if (-not $DryRun) { Write-Ok "tag $Tag pushed (Release workflow triggered)" }

# ---- 8. wait for draft + apply notes -------------------------------------
Write-Step "Apply curated release notes"
if ($DryRun) {
    Write-Info "[dry-run] would wait for draft release and run: gh release edit $Tag --notes-file $notesPath"
}
else {
    $deadline = (Get-Date).AddMinutes(15)
    $found = $false
    while ((Get-Date) -lt $deadline) {
        gh release view $Tag *> $null
        if ($LASTEXITCODE -eq 0) { $found = $true; break }
        Write-Info "waiting for draft release..."
        Start-Sleep -Seconds 30
    }
    if (-not $found) { Write-WarnLine "draft release not seen yet; set notes later: gh release edit $Tag --notes-file <file>" }
    else {
        Invoke-Native { gh release edit $Tag --notes-file $notesPath } "gh release edit $Tag --notes-file" | Out-Null
        Write-Ok "curated notes applied"
    }
}

# ---- 9. watch Release workflow + verify assets ---------------------------
Write-Step "Watch Release workflow"
$releaseOk = $false
if ($DryRun) {
    Write-Info "[dry-run] would watch release.yml and verify assets"
    $releaseOk = $true
}
else {
    $rel = Wait-ForRun -Workflow "release.yml" -Sha $localSha -TimeoutMinutes $ReleaseTimeoutMinutes -Label "Release"
    if (-not $rel.Ok) { Write-WarnLine "Release workflow did not succeed (conclusion: $($rel.Conclusion), run $($rel.Id))." }
    else { Write-Ok "Release workflow succeeded (run $($rel.Id))"; $releaseOk = $true }
}

Write-Step "Verify published release"
if (-not $DryRun) {
    $rv = gh release view $Tag --json isDraft,assets 2>$null | ConvertFrom-Json
    $assetNames = @($rv.assets | ForEach-Object { $_.name })
    if ($rv.isDraft) { Write-WarnLine "release is still a DRAFT." } else { Write-Ok "release is published" }

    $required = @(
        "ETL-SQL-$Tag-win-x64.zip",
        "ETL-SQL-$Tag-x64-Setup.msi",
        "ETL-SQL-$Tag-linux-x64.zip",
        "etl-sql_${Version}_amd64.deb",
        "ETL-SQL-$Tag-osx-arm64.zip"
    )
    $missing = $required | Where-Object { $assetNames -notcontains $_ }
    $hasVsix = ($assetNames | Where-Object { $_ -like "*.vsix" }).Count -gt 0
    if (-not $hasVsix) { $missing += "*.vsix" }
    if ($missing.Count -gt 0) {
        Write-WarnLine "missing required assets: $($missing -join ', ')"
        $releaseOk = $false
    }
    else { Write-Ok "all required assets present ($($assetNames.Count) total)" }

    foreach ($opt in @("ETL-SQL-$Tag-osx-x64.zip", "ETL-SQL_${Tag}.dmg")) {
        if ($assetNames -notcontains $opt) { Write-Info "best-effort asset absent (ok): $opt" }
    }
}

# ---- 9b. Attach verification assets (sha256sums + sbom) -------------------
# release.yml uploads only the platform binaries; the CycloneDX SBOM and the checksum manifest
# are attached here so the published release carries both verification assets (Release_Checklist
# Phase 5). The SBOM is version-stamped locally from Directory.Build.props; the checksums must
# cover the cloud-built published assets, so those are downloaded and hashed.
Write-Step "Attach sha256sums + sbom"
if ($DryRun) {
    Write-Info "[dry-run] would generate sbom.json + sha256sums.txt and upload them to $Tag"
}
elseif (-not $releaseOk) {
    Write-WarnLine "release not verified; skipping checksum/sbom attach"
}
else {
    Invoke-Native { node scripts/generate-sbom.js } "node scripts/generate-sbom.js" | Out-Null
    $sbom = Join-Path $RepoRoot "release/sbom.json"
    if (-not (Test-Path $sbom)) { Fail "generate-sbom.js did not produce release/sbom.json." }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) "etlsql-relassets-$Tag"
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        Invoke-Native {
            gh release download $Tag --dir $work --pattern '*.zip' --pattern '*.msi' `
                --pattern '*.deb' --pattern '*.dmg' --pattern '*.vsix'
        } "download published $Tag assets" | Out-Null

        $sumsPath = Join-Path $work "sha256sums.txt"
        Get-ChildItem $work -File |
            Where-Object { $_.Name -notin @("sha256sums.txt", "sbom.json") } |
            Sort-Object Name |
            ForEach-Object { "$((Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLower())  $($_.Name)" } |
            Set-Content -Path $sumsPath -Encoding ASCII

        Invoke-Native { gh release upload $Tag $sumsPath $sbom --clobber } "gh release upload sha256sums.txt + sbom.json" | Out-Null
        Write-Ok "attached sha256sums.txt + sbom.json"
    }
    finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ---- 9c. optional: prune merged branches (opt-in) ------------------------
# Sweep up the sprint's branches so they don't pile up release over release. Non-destructive by
# default: prunes stale remote-tracking refs, safe-deletes LOCAL branches already merged into
# $Branch ('git branch -d' refuses unmerged), and only PRINTS the delete command for merged REMOTE
# branches (deleting a shared ref stays a deliberate, reviewed action). Protected from deletion:
# the release branch itself, main, dev, and anything under release/.
if ($PruneMergedBranches) {
    Write-Step "Prune merged branches"
    if (-not $releaseOk -and -not $DryRun) {
        Write-WarnLine "release not verified; skipping branch prune"
    }
    else {
        $protectedBranches = @($Branch, 'main', 'dev')
        function Test-BranchProtected {
            param([string]$Name)
            if ($protectedBranches -contains $Name) { return $true }
            if ($Name -like 'release/*') { return $true }
            return $false
        }

        # Prune remote-tracking refs whose remote branch is already gone (safe: touches only refs).
        Invoke-Native { git remote prune $Remote } "git remote prune $Remote" -Mutating | Out-Null
        if (-not $DryRun) { Write-Ok "pruned stale remote-tracking refs for $Remote" }

        # Local branches fully merged into $Branch.
        $localMerged = @(& git branch --merged $Branch --format '%(refname:short)') |
            Where-Object { $_ -and -not (Test-BranchProtected $_) }
        if ($localMerged.Count -gt 0) {
            foreach ($b in $localMerged) {
                Invoke-Native { git branch -d $b } "delete local merged branch '$b'" -Mutating | Out-Null
                if (-not $DryRun) { Write-Ok "deleted local branch '$b'" }
            }
        }
        else { Write-Info "no local merged branches to delete" }

        # Remote branches merged into $Remote/$Branch — listed only (deletion is a reviewed step).
        $remoteMerged = @(& git branch -r --merged "$Remote/$Branch" --format '%(refname:short)') |
            Where-Object { $_ -and $_ -notlike '*/HEAD' } |
            ForEach-Object { $_ -replace "^$([regex]::Escape($Remote))/", '' } |
            Where-Object { -not (Test-BranchProtected $_) } |
            Select-Object -Unique
        if ($remoteMerged.Count -gt 0) {
            Write-WarnLine "remote branches merged into $Branch (review, then delete):"
            foreach ($b in $remoteMerged) { Write-Info "git push $Remote --delete $b" }
        }
        else { Write-Info "no remote merged branches to review" }
    }
}

if ($tempNotes -and (Test-Path $notesPath)) { Remove-Item $notesPath -Force -ErrorAction SilentlyContinue }

# ---- 10. summary + manual tail -------------------------------------------
Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " RELEASE $Tag $(if ($DryRun) { '(dry-run)' } elseif ($releaseOk) { 'COMPLETE' } else { 'NEEDS ATTENTION' })" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Manual tail (see Docs/Release_Checklist.md Phase 5):" -ForegroundColor Yellow
Write-Host "  - Spot-install one artifact (MSI on Windows) and confirm it launches / services start."
Write-Host "  - Review run annotations (e.g. macOS Intel skipped) and accept/reject."
Write-Host "  - Announce the release and update any external links."
Write-Host ""
Write-Host "Release page: " -NoNewline; Write-Host (gh release view $Tag --json url --jq .url 2>$null) -ForegroundColor Cyan

if (-not $releaseOk -and -not $DryRun) { exit 1 }
exit 0
