# Exercise discovery through a fake GitHub CLI; no network or MSI installation.
$ErrorActionPreference = 'Stop'
$selector = Join-Path $PSScriptRoot '../Find-CertifiedMsi.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $tempRoot ('msi-discovery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$savedOutput = $env:GITHUB_OUTPUT
$env:GITHUB_OUTPUT = Join-Path $fixture 'outputs.txt'
$sha = 'a' * 40

function gh {
    $global:LASTEXITCODE = 0
    if ($global:MsiDiscoveryScenario -eq 'api-failure') { $global:LASTEXITCODE = 1; return '' }
    if ($args[0] -eq 'api') {
        $endpoint = $args[-1]
        if ($endpoint -like '*workflows/*') {
            $run = @{
                id = 123; run_attempt = 2; head_sha = $sha; path = '.github/workflows/msi-upgrade.yml'
                head_repository = @{ full_name = 'example/etl-sql' }; head_branch = 'release/v1.2.3'
                event = 'push'; status = 'completed'; conclusion = 'success'
                created_at = [datetimeoffset]::UtcNow.AddDays(-1).ToString('o')
            }
            if ($global:MsiDiscoveryScenario -eq 'wrong-sha') { $run.head_sha = 'b' * 40 }
            if ($global:MsiDiscoveryScenario -eq 'pending') { $run.status = 'in_progress'; $run.conclusion = $null }
            if ($global:MsiDiscoveryScenario -eq 'failed') { $run.conclusion = 'failure' }
            # The useful run is on page two.
            return ConvertTo-Json -InputObject @(@{workflow_runs=@()}, @{workflow_runs=@($run)}) -Depth 8
        }
        $artifacts = @(@{ id = 456; name = 'msi-inputs-123-1'; expired = ($global:MsiDiscoveryScenario -eq 'expired') })
        if ($global:MsiDiscoveryScenario -ne 'no-receipt') {
            $artifacts += @{ id = 789; name = 'msi-certified-123-2'; expired = $false }
        }
        return ConvertTo-Json -InputObject @(@{artifacts=@()}, @{artifacts=$artifacts}) -Depth 8
    }
    if ($args[0] -ne 'run' -or $args[1] -ne 'download') { throw 'Unexpected CLI command.' }
    $dirIndex = [array]::IndexOf($args, '--dir')
    if ($dirIndex -lt 0) { throw 'Missing receipt destination.' }
    @{
        schemaVersion = 1; sourceSha = $sha; repository = 'example/etl-sql'; runId = '123'
        runAttempt = '2'; artifactId = '456'; previousTag = 'v1.2.2'; workstation = 'success'
        portal = $(if ($global:MsiDiscoveryScenario -eq 'incomplete') { 'skipped' } else { 'success' })
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $args[$dirIndex + 1] 'certification.json')
}

try {
    foreach ($global:MsiDiscoveryScenario in @('valid', 'wrong-sha', 'no-receipt', 'pending', 'failed', 'expired', 'incomplete', 'api-failure')) {
        Set-Content -LiteralPath $env:GITHUB_OUTPUT -Value ''
        $rejected = $false
        try {
            & $selector -SourceSha $sha -Repository 'example/etl-sql' -PreviousTag 'v1.2.2' -OutputDirectory $fixture
        } catch { $rejected = $true }
        $shouldReject = $global:MsiDiscoveryScenario -in @('pending', 'failed', 'expired', 'incomplete', 'api-failure')
        if ($rejected -ne $shouldReject) { throw "Wrong discovery outcome: $global:MsiDiscoveryScenario" }
        $output = Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw
        if ($global:MsiDiscoveryScenario -eq 'valid') {
            foreach ($line in @('artifact_id=456', 'run_id=123', 'receipt_id=789')) {
                if ($output -notmatch [regex]::Escape($line)) { throw "Missing selection output: $line" }
            }
        } elseif (-not [string]::IsNullOrWhiteSpace($output)) { throw 'Rejected/absent evidence produced selection outputs.' }
    }
    Write-Host 'MSI discovery passed: pagination, retained retry inputs, fallback, pending/failed runs, expired/incomplete evidence and API errors.'
} finally {
    $env:GITHUB_OUTPUT = $savedOutput
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^msi-discovery-[a-f0-9]{32}$') {
        throw 'Refusing cleanup outside the test fixture.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
