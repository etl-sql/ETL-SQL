param([Parameter(Mandatory)][string]$RepoRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $RepoRoot 'scripts/Release.Helpers.ps1')
$nativeGit = (Get-Command git -CommandType Application).Source
function Invoke-FixtureGit {
    param([string[]]$Arguments)
    $null = & $nativeGit -C $RepoRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Fixture Git command failed.' }
}
function Assert-Rejected {
    param([string]$Name, [scriptblock]$Action)
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw "Accepted invalid certification source: $Name" }
    Write-Host "Rejected $Name"
}
$file = Join-Path $RepoRoot 'tracked.txt'
$baseline = [IO.File]::ReadAllText($file)
$initial = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
if ($initial.dirtyPaths.Count -ne 0 -or $initial.commit -cnotmatch '^[a-f0-9]{40}$') { throw 'Fixture must start clean.' }
Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $initial

[IO.File]::WriteAllText($file, 'tracked mutation')
Assert-Rejected 'tracked mutation' { Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $initial }
$dirty = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $dirty
[IO.File]::AppendAllText($file, ' changed again')
Assert-Rejected 'different content with the same dirty path' { Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $dirty }
Invoke-FixtureGit @('reset', '--hard', 'HEAD')

$untracked = Join-Path $RepoRoot 'extra-é.txt'
[IO.File]::WriteAllText($untracked, 'untracked mutation')
Assert-Rejected 'new untracked source' { Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $initial }
$dirty = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
[IO.File]::AppendAllText($untracked, ' changed again')
Assert-Rejected 'untracked content mutation' { Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $dirty }
[IO.File]::Delete($untracked)

[IO.File]::WriteAllText($file, 'staged mutation')
Invoke-FixtureGit @('add', 'tracked.txt')
[IO.File]::WriteAllText($file, $baseline)
if ((Get-ReleaseSourceFingerprint -RepoRoot $RepoRoot) -cne $initial.fingerprint) { throw 'Expected the net working tree bytes to match HEAD.' }
Assert-Rejected 'index changed while working tree bytes match HEAD' { Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $initial }
Invoke-FixtureGit @('reset', '--hard', 'HEAD')

Invoke-FixtureGit @('commit', '--quiet', '--allow-empty', '-m', 'New fixture candidate')
Assert-Rejected 'new commit' { Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $initial }
Invoke-FixtureGit @('reset', '--hard', $initial.commit)
Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $initial

$global:CertificationGitFailure = ''
$global:CertificationMutateOnStatus = $false
function git {
    if ($args -ccontains $global:CertificationGitFailure) {
        $global:LASTEXITCODE = 53
        return
    }
    & $nativeGit @args
    if ($global:CertificationMutateOnStatus -and $args -ccontains 'status') {
        $global:CertificationMutateOnStatus = $false
        [IO.File]::AppendAllText($file, ' changed while capturing source')
    }
    $global:LASTEXITCODE = $LASTEXITCODE
}
foreach ($operation in @('rev-parse', 'status', 'diff', 'ls-files')) {
    $global:CertificationGitFailure = $operation
    Assert-Rejected "failed $operation lookup" { Get-ReleaseCertificationSource -RepoRoot $RepoRoot }
}
$global:CertificationGitFailure = ''
$global:CertificationMutateOnStatus = $true
Assert-Rejected 'source changed between cleanliness and fingerprint capture' { Get-ReleaseCertificationSource -RepoRoot $RepoRoot }
Write-Host 'Certification source snapshots: clean/dirty stability, content/index/commit drift and Git failures verified.'
