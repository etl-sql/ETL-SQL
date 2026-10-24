param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$TemplateRoot, [string]$Mutation)
$ErrorActionPreference = 'Stop'
$nativeGit = (Get-Command git -CommandType Application).Source
$global:CertificationGitFailure = if ($Mutation -eq 'initial-status-error') { 'status' } else { '' }
function git {
    if ($args -ccontains $global:CertificationGitFailure) {
        $global:LASTEXITCODE = 53
        return
    }
    & $nativeGit @args
    $global:LASTEXITCODE = $LASTEXITCODE
}
function dotnet {
    foreach ($name in @('production-canary-report.json', 'production-canary-provisioning.json', 'production-canary-credential-lifecycle.json')) {
        Copy-Item -LiteralPath (Join-Path $TemplateRoot $name) -Destination $env:ETLSQL_CANARY_EVIDENCE_DIR
    }
    switch ($Mutation) {
        'tracked' { [IO.File]::AppendAllText((Join-Path $RepoRoot 'tracked.txt'), ' changed during test') }
        'untracked' { [IO.File]::WriteAllText((Join-Path $RepoRoot 'extra.txt'), 'new source during test') }
        'commit' {
            & $nativeGit -C $RepoRoot commit --quiet --allow-empty -m 'New candidate during test'
            if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed.' }
        }
        'final-status-error' { $global:CertificationGitFailure = 'status' }
    }
    $global:LASTEXITCODE = 0
    'Passed: 18'
}
& (Join-Path $RepoRoot 'scripts/Test-ProductionCanaryCertification.ps1') -NoBuild -OutputRoot 'out'
