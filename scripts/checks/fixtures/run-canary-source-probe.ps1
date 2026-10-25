param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$TemplateRoot, [string]$Mutation)
$ErrorActionPreference = 'Stop'
$nativeGit = (Get-Command git -CommandType Application | Select-Object -First 1).Source
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
        'provisioning-tenant' {
            $path = Join-Path $env:ETLSQL_CANARY_EVIDENCE_DIR 'production-canary-provisioning.json'
            $value = Get-Content $path -Raw | ConvertFrom-Json
            $value.observed.tenantId = 'wrong-synthetic-tenant'
            $value | ConvertTo-Json -Depth 12 | Set-Content $path
        }
        'provisioning-cost' {
            $path = Join-Path $env:ETLSQL_CANARY_EVIDENCE_DIR 'production-canary-provisioning.json'
            $value = Get-Content $path -Raw | ConvertFrom-Json
            $value.observed.enforcedMonthlyCostLimitUsd = 100000
            $value | ConvertTo-Json -Depth 12 | Set-Content $path
        }
        'credential-identity' {
            $path = Join-Path $env:ETLSQL_CANARY_EVIDENCE_DIR 'production-canary-credential-lifecycle.json'
            $value = Get-Content $path -Raw | ConvertFrom-Json
            $value.scheduledRotation.current.credentialId = $value.scheduledRotation.previous.credentialId
            $value | ConvertTo-Json -Depth 12 | Set-Content $path
        }
        'credential-reason' {
            $path = Join-Path $env:ETLSQL_CANARY_EVIDENCE_DIR 'production-canary-credential-lifecycle.json'
            $value = Get-Content $path -Raw | ConvertFrom-Json
            $value.compromiseResponse.reason = 'scheduled'
            $value | ConvertTo-Json -Depth 12 | Set-Content $path
        }
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
exit $LASTEXITCODE
