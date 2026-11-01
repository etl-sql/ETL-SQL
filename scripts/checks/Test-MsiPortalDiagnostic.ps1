$ErrorActionPreference = 'Stop'
$producer = Join-Path $PSScriptRoot '../Prepare-MsiPortalDiagnostic.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root = Join-Path $tempRoot ('msi-diagnostic-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$names = @('GITHUB_EVENT_NAME', 'GITHUB_REF', 'GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_OUTPUT')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$global:MsiDiagnosticFixtureMode = 'normal'
function gh {
    param($Api, $Route)
    $global:LASTEXITCODE = 0
    if ($global:MsiDiagnosticFixtureMode -eq 'api-error') { $global:LASTEXITCODE = 1; return '{}' }
    $value = switch -Wildcard ($Route) {
        '*/actions/artifacts/123' {
            @{ id = 123; name = 'msi-inputs-456-2'; digest = ('sha256:' + ('a' * 64)); expired = $false; workflow_run = @{ id = 456 } }
        }
        '*/actions/runs/456' {
            @{ repository = @{ full_name = 'owner/repo' }; head_repository = @{ full_name = 'owner/repo' }
                status = 'completed'; path = '.github/workflows/msi-upgrade.yml'; run_attempt = 2; head_sha = ('b' * 40) }
        }
        '*/attempts/2/jobs?per_page=100' {
            @{ total_count = 1; jobs = @(@{ name = 'Build MSI certification inputs'; conclusion = 'success' }) }
        }
        default { throw "Unexpected diagnostic API route: $Route" }
    }
    if ($Route -like '*/actions/artifacts/*') {
        switch ($global:MsiDiagnosticFixtureMode) {
            'expired' { $value.expired = $true }
            'wrong-name' { $value.name = 'msi-certified-456-2' }
            'wrong-run' { $value.workflow_run.id = 457 }
            'wrong-id' { $value.id = 124 }
            'missing-digest' { $value.digest = $null }
        }
    } elseif ($Route -like '*/actions/runs/456') {
        switch ($global:MsiDiagnosticFixtureMode) {
            'fork' { $value.head_repository.full_name = 'other/repo' }
            'foreign-repo' { $value.repository.full_name = 'other/repo' }
            'active' { $value.status = 'in_progress' }
            'wrong-workflow' { $value.path = '.github/workflows/other.yml' }
            'wrong-attempt' { $value.run_attempt = 1 }
        }
    } else {
        switch ($global:MsiDiagnosticFixtureMode) {
            'truncated' { $value.total_count = 101 }
            'failed-build' { $value.jobs[0].conclusion = 'failure' }
            'duplicate-build' { $value.jobs += $value.jobs[0]; $value.total_count = 2 }
        }
    }
    $value | ConvertTo-Json -Depth 8 -Compress
}
try {
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'; $env:GITHUB_REF = 'refs/heads/fix/msi'
    $env:GITHUB_REPOSITORY = 'owner/repo'; $env:GITHUB_SHA = 'c' * 40
    $env:GITHUB_OUTPUT = Join-Path $root 'outputs'
    $good = Join-Path $root 'good'
    & $producer -ArtifactId 123 -OutputDirectory $good
    $origin = Get-Content (Join-Path $good 'origin.json') -Raw | ConvertFrom-Json
    if ((Get-Content $env:GITHUB_OUTPUT) -cnotcontains 'origin_run_id=456') { throw 'Cross-run download lost its original run ID.' }
    if ($origin.scope -cne 'DiagnosticOnly' -or $origin.inputRunId -cne '456' -or $origin.inputAttempt -ne 2 -or
        $origin.inputRunHead -cne ('b' * 40) -or $origin.scriptSha -cne ('c' * 40) -or
        (Test-Path (Join-Path $good 'certification.json'))) { throw 'Diagnostic origin lost its separate scope.' }
    foreach ($mode in @('expired', 'wrong-name', 'wrong-run', 'wrong-id', 'missing-digest', 'fork', 'foreign-repo', 'active', 'wrong-workflow', 'wrong-attempt', 'truncated', 'failed-build', 'duplicate-build', 'api-error')) {
        $global:MsiDiagnosticFixtureMode = $mode; $path = Join-Path $root $mode; $rejected = $false
        try { & $producer -ArtifactId 123 -OutputDirectory $path } catch { $rejected = $true }
        if (-not $rejected -or (Test-Path -LiteralPath $path)) { throw "Unsafe diagnostic origin created output: $mode" }
    }
    $global:MsiDiagnosticFixtureMode = 'normal'
    foreach ($branch in @('refs/heads/main', 'refs/heads/dev', 'refs/heads/release/v0.22.0', 'refs/tags/v0.22.0')) {
        $env:GITHUB_REF = $branch; $rejected = $false
        try { & $producer -ArtifactId 123 -OutputDirectory (Join-Path $root 'protected') } catch { $rejected = $true }
        if (-not $rejected) { throw 'Diagnostic dispatch accepted a release/tag branch.' }
    }
    Write-Host 'MSI diagnostic: original successful input build and immutable origin required; forks, stale/foreign/truncated inputs and protected branches reject before output. No installer executed.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Variable MsiDiagnosticFixtureMode -Scope Global
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^msi-diagnostic-[a-f0-9]{32}$') { throw 'Refusing cleanup outside fixture.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
