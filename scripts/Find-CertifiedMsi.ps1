[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[a-f0-9]{40}$')] [string]$SourceSha,
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')] [string]$Repository,
    [Parameter(Mandatory)] [string]$PreviousTag,
    [Parameter(Mandatory)] [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MsiReuse.Helpers.ps1')

function Read-GitHubPages([string]$Endpoint) {
    $json = & gh api --paginate --slurp $Endpoint
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read MSI certification metadata from GitHub.' }
    return $json | ConvertFrom-Json
}

function Read-CompleteInventory($Pages, [string]$Property) {
    $items = @($Pages | ForEach-Object { $_.$Property })
    if (@($Pages).Count -eq 0) { throw 'Missing GitHub inventory pages.' }
    foreach ($page in $Pages) {
        if ($null -eq $page.$Property -or $null -eq $page.total_count -or $page.total_count -ne $items.Count) {
            throw 'Incomplete GitHub inventory.'
        }
    }
    $ids = [Collections.Generic.HashSet[string]]::new()
    foreach ($item in $items) {
        if ([string]$item.id -notmatch '^[1-9][0-9]*$' -or -not $ids.Add([string]$item.id)) {
            throw 'Invalid or duplicate GitHub inventory identity.'
        }
    }
    return $items
}

$pages = Read-GitHubPages "repos/$Repository/actions/runs?head_sha=$SourceSha&per_page=100"
$runs = @(Read-CompleteInventory -Pages $pages -Property workflow_runs | Sort-Object id -Descending)
foreach ($run in $runs) {
    if (-not (Test-MsiCandidateRun -Run $run -SourceSha $SourceSha -Repository $Repository)) { continue }
    if (-not (Test-MsiReusableRun -Run $run -SourceSha $SourceSha -Repository $Repository)) {
        throw "Candidate MSI run $($run.id) is $($run.status)/$($run.conclusion). Complete or retry that run before release; no duplicate build was started."
    }
    $artifactPages = Read-GitHubPages "repos/$Repository/actions/runs/$($run.id)/artifacts?per_page=100"
    $artifacts = @(Read-CompleteInventory -Pages $artifactPages -Property artifacts)
    $receiptName = "msi-certified-$($run.id)-$($run.run_attempt)"
    $receipts = @($artifacts | Where-Object { $_.name -ceq $receiptName -and $_.expired -eq $false })
    # Path-skipped and older workflows have no successful certification receipt.
    if ($receipts.Count -eq 0) { continue }
    if ($receipts.Count -ne 1) { throw 'Ambiguous MSI certification receipt.' }
    $directory = Join-Path $OutputDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    & gh run download ([string]$run.id) --repo $Repository --name $receiptName --dir $directory
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download MSI certification receipt.' }
    $receipt = Get-Content -LiteralPath (Join-Path $directory 'certification.json') -Raw | ConvertFrom-Json
    $artifactId = Get-MsiReusableArtifact -Receipt $receipt -Run $run -Artifacts $artifacts -SourceSha $SourceSha -Repository $Repository -PreviousTag $PreviousTag
    $currentJson = & gh api "repos/$Repository/actions/runs/$($run.id)"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot recheck MSI certification run.' }
    $current = $currentJson | ConvertFrom-Json
    if ([string]$current.id -cne [string]$run.id -or $current.run_attempt -ne $run.run_attempt -or
        -not (Test-MsiReusableRun -Run $current -SourceSha $SourceSha -Repository $Repository)) {
        throw 'MSI certification changed during selection.'
    }
    "artifact_id=$artifactId" >> $env:GITHUB_OUTPUT
    "run_id=$($run.id)" >> $env:GITHUB_OUTPUT
    "receipt_id=$($receipts[0].id)" >> $env:GITHUB_OUTPUT
    Write-Host "Reusing MSI certification run $($run.id), attempt $($run.run_attempt), artifact $artifactId."
    exit 0
}
Write-Host 'No eligible pre-tag MSI certification found; the release must build and certify its MSI.'
