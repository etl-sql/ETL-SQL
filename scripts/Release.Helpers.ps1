# Side-effect-free release policy shared by the tag and MSI workflows.

function Test-ReleaseTestOutputExecuted {
    param([AllowEmptyString()][string]$Output)

    return $Output -match 'Passed:\s*[1-9][0-9]*\b'
}

function Test-ReleaseTrxComplete {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    try {
        [xml]$result = Get-Content -LiteralPath $Path -Raw
        $counters = $result.TestRun.ResultSummary.Counters
        return [int]$counters.total -gt 0 -and [int]$counters.executed -eq [int]$counters.total -and
            [int]$counters.passed -eq [int]$counters.total
    } catch { return $false }
}

function Test-PreReleasePhaseReusable {
    param([object]$PreviousPhase, [Parameter(Mandatory)][string]$Command)

    $eligible = $null -ne $PreviousPhase -and $PreviousPhase.status -eq 'Passed' -and
        $PreviousPhase.command -eq $Command -and
        -not [string]::IsNullOrWhiteSpace($PreviousPhase.log) -and
        (Test-Path -LiteralPath $PreviousPhase.log -PathType Leaf)
    if (-not $eligible -or (Get-Item -LiteralPath $PreviousPhase.log).Length -eq 0) { return $false }
    $completed = [datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse([string]$PreviousPhase.completedAt, [ref]$completed)) { return $false }
    $now = [datetimeoffset]::UtcNow
    if ($completed -gt $now -or $completed -lt $now.AddDays(-7)) { return $false }
    foreach ($artifact in $PreviousPhase.artifacts) {
        if (-not (Test-Path -LiteralPath $artifact)) { return $false }
    }
    return $true
}

function Get-ReleaseSourceFingerprint {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $previousConsoleEncoding = [Console]::OutputEncoding
    $previousPipeEncoding = $OutputEncoding
    try {
        # Git emits UTF-8 paths; the Windows console's OEM encoding must not change their names.
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $OutputEncoding = [Console]::OutputEncoding
        $head = & git -C $RepoRoot rev-parse HEAD 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot fingerprint the candidate commit.' }
        $diff = & git -C $RepoRoot diff --binary HEAD -- 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot fingerprint tracked candidate changes.' }
        $untracked = @(& git -C $RepoRoot -c core.quotePath=false ls-files --others --exclude-standard 2>$null)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot fingerprint untracked candidate files.' }
        $entries = @($head; $diff; foreach ($path in $untracked) {
            $hash = (Get-FileHash -LiteralPath (Join-Path $RepoRoot $path) -Algorithm SHA256).Hash
            "$path $hash"
        })
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($entries -join "`n")
        return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    } finally {
        [Console]::OutputEncoding = $previousConsoleEncoding
        $OutputEncoding = $previousPipeEncoding
    }
}

function Get-ReleaseCertificationSource {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $commit = ((& git -C $RepoRoot rev-parse HEAD 2>$null) -join '').Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[a-f0-9]{40}$') {
        throw 'Cannot establish the certification candidate commit.'
    }
    $dirtyPaths = @(& git -C $RepoRoot status --porcelain=v1 --untracked-files=all 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot establish certification candidate cleanliness.' }
    $fingerprint = Get-ReleaseSourceFingerprint -RepoRoot $RepoRoot
    if ($dirtyPaths.Count -eq 0) {
        $cleanFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
                [Text.Encoding]::UTF8.GetBytes($commit))).ToLowerInvariant()
        if ($fingerprint -cne $cleanFingerprint) { throw 'Certification source changed while capturing its identity.' }
    }
    return [pscustomobject]@{ commit = $commit; dirtyPaths = $dirtyPaths; fingerprint = $fingerprint }
}

function Assert-ReleaseCertificationSourceUnchanged {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][object]$Initial
    )

    $current = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
    if ($current.commit -cne $Initial.commit -or $current.fingerprint -cne $Initial.fingerprint -or
        (ConvertTo-Json -InputObject @($current.dirtyPaths) -Compress) -cne
        (ConvertTo-Json -InputObject @($Initial.dirtyPaths) -Compress)) {
        throw 'Candidate source changed during certification; retained partial results cannot certify this candidate.'
    }
}

function ConvertTo-StableReleaseVersion {
    param([Parameter(Mandatory)][string]$Tag)

    if ($Tag -notmatch '^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        return $null
    }
    $value = $null
    if ([version]::TryParse($Tag.TrimStart('v'), [ref]$value)) { return $value }
    return $null
}

function Get-PublishedStableReleases {
    param([AllowEmptyCollection()][object[]]$Releases = @())

    foreach ($release in $Releases) {
        # Consume the GitHub API shape. Missing flags must not turn an unknown record into a release.
        if ($release.draft -isnot [bool] -or $release.prerelease -isnot [bool]) {
            throw 'GitHub release record is missing boolean draft/prerelease flags.'
        }
        if ($release.draft -or $release.prerelease) { continue }
        $version = ConvertTo-StableReleaseVersion -Tag $release.tag_name
        if ($null -ne $version) {
            [pscustomobject]@{ TagName = $release.tag_name; Version = $version }
        }
    }
}

function Select-PreviousStableRelease {
    param(
        [Parameter(Mandatory)][string]$CurrentVersion,
        [AllowEmptyCollection()][object[]]$Releases = @()
    )

    $current = ConvertTo-StableReleaseVersion -Tag $CurrentVersion
    if ($null -eq $current) { throw "Invalid stable release version '$CurrentVersion'." }
    $previous = Get-PublishedStableReleases -Releases $Releases |
        Where-Object { $_.Version -lt $current } |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($null -eq $previous) { throw "No prior stable GitHub release found before $CurrentVersion." }
    return $previous.TagName
}

function Get-ReleaseLatestValue {
    param(
        [Parameter(Mandatory)][string]$CandidateTag,
        [AllowEmptyCollection()][object[]]$Releases = @()
    )

    $candidate = ConvertTo-StableReleaseVersion -Tag $CandidateTag
    if ($null -eq $candidate) { return 'false' }
    $newer = @(Get-PublishedStableReleases -Releases $Releases |
        Where-Object { $_.Version -gt $candidate })
    if ($newer.Count -gt 0) { return 'false' }
    return 'true'
}

function Assert-ReleaseTagReachable {
    param([Parameter(Mandatory)][string]$Tag)

    $commit = & git rev-parse --verify --end-of-options "$Tag^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Release tag '$Tag' does not exist." }
    $refs = @(& git for-each-ref '--format=%(refname)' refs/remotes/origin/main refs/remotes/origin/release/)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate remote release branches.' }
    $refs = @($refs | Where-Object {
        $_ -eq 'refs/remotes/origin/main' -or $_ -like 'refs/remotes/origin/release/*'
    })
    foreach ($ref in $refs) {
        & git merge-base --is-ancestor $commit $ref
        if ($LASTEXITCODE -eq 0) { return $ref }
        if ($LASTEXITCODE -ne 1) { throw "Cannot check release ancestry against '$ref'." }
    }
    throw "Release tag '$Tag' must be reachable from origin/main or origin/release/**."
}
