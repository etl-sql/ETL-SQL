$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../MsiReuse.Helpers.ps1')
$now = [datetimeoffset]'2026-10-19T12:00:00Z'
$sha = 'a' * 40
$repo = 'example/etl-sql'
function New-Run {
    return @{
        id = 123; run_attempt = 2; head_sha = $sha; path = '.github/workflows/msi-upgrade.yml'
        head_repository = @{ full_name = $repo }; head_branch = 'release/v1.2.3'; event = 'push'
        status = 'completed'; conclusion = 'success'; created_at = $now.AddDays(-1).ToString('o')
    }
}
function New-Receipt {
    return @{
        schemaVersion = 1; sourceSha = $sha; repository = $repo; runId = '123'; runAttempt = '2'
        previousTag = 'v1.2.2'; workstation = 'success'; portal = 'success'; artifactId = '456'
    }
}
function Assert-ReceiptRejected($Receipt, $Artifacts) {
    $rejected = $false
    try { Get-MsiReusableArtifact -Receipt $Receipt -Run (New-Run) -Artifacts $Artifacts -SourceSha $sha -Repository $repo -PreviousTag 'v1.2.2' | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid certification receipt/artifact was accepted.' }
}
if (-not (Test-MsiReusableRun -Run (New-Run) -SourceSha $sha -Repository $repo -Now $now)) { throw 'Valid run rejected.' }
$nightly = New-Run
$nightly.path = '.github/workflows/nightly-release.yml'
$nightly.event = 'workflow_dispatch'
if (-not (Test-MsiReusableRun -Run $nightly -SourceSha $sha -Repository $repo -Now $now)) { throw 'Valid nightly run rejected.' }
$nightly.event = 'push'
if (Test-MsiReusableRun -Run $nightly -SourceSha $sha -Repository $repo -Now $now) { throw 'Unexpected nightly trigger accepted.' }
$cases = @(
    @{ head_sha = ('b' * 40) }, @{ path = '.github/workflows/other.yml' },
    @{ head_repository = @{ full_name = 'fork/etl-sql' } }, @{ head_branch = 'feature/test' },
    @{ event = 'pull_request' }, @{ status = 'in_progress' }, @{ conclusion = 'failure' },
    @{ conclusion = 'cancelled' }, @{ conclusion = 'skipped' }, @{ created_at = 'invalid' },
    @{ created_at = $now.AddDays(-8).ToString('o') }, @{ created_at = $now.AddDays(1).ToString('o') }
)
foreach ($change in $cases) {
    $run = New-Run
    foreach ($key in $change.Keys) { $run[$key] = $change[$key] }
    if (Test-MsiReusableRun -Run $run -SourceSha $sha -Repository $repo -Now $now) { throw "Invalid run accepted: $($change.Keys)" }
}
# A later successful attempt may reuse the original build artifact from attempt 1.
$artifacts = @(@{ id = 456; name = 'msi-inputs-123-1'; expired = $false })
$id = Get-MsiReusableArtifact -Receipt (New-Receipt) -Run (New-Run) -Artifacts $artifacts -SourceSha $sha -Repository $repo -PreviousTag 'v1.2.2'
if ($id -cne '456') { throw 'Original build artifact was not retained across retry.' }
foreach ($change in @(
    @{ schemaVersion = 2 }, @{ sourceSha = ('b' * 40) }, @{ repository = 'fork/etl-sql' },
    @{ runId = '999' }, @{ runAttempt = '1' }, @{ previousTag = 'v1.2.1' },
    @{ workstation = 'skipped' }, @{ portal = 'failure' }, @{ artifactId = '999' }
)) {
    $receipt = New-Receipt
    foreach ($key in $change.Keys) { $receipt[$key] = $change[$key] }
    Assert-ReceiptRejected $receipt $artifacts
}
Assert-ReceiptRejected (New-Receipt) @()
Assert-ReceiptRejected (New-Receipt) @($artifacts[0], $artifacts[0])
Assert-ReceiptRejected (New-Receipt) @(@{ id = 456; name = 'msi-inputs-123-1'; expired = $true })
Assert-ReceiptRejected (New-Receipt) @(@{ id = 456; name = 'msi-inputs-999-1'; expired = $false })
Write-Host 'MSI reuse policy passed: exact commit, trusted source, freshness, successful profiles, predecessor and retained artifact.'
