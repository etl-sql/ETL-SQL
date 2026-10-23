$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../MsiImpact.Helpers.ps1')

foreach ($path in @(
    'src/ETL-SQL.Orchestrator.Service/Program.cs',
    'src/ETL-SQL.Portal/Program.cs', 'src/ETL-SQL.Engine/Evaluator.cs',
    'src/ETL-SQL.Connectors/NewConnector.cs', 'src/appsettings.json',
    'src/ETL-SQL.ReportRuntime/Resources/TypeScript/runtime.ts',
    'Directory.Build.targets', 'Directory.Packages.props', 'global.json',
    '.github/workflows/ci.yml', 'scripts/MsiImpact.Helpers.ps1',
    'docs/reference/statements/new.md', 'LICENSE.md', 'unknown/new-file',
    'docs/releases/payload.js', 'src/etl-sql-vscode/ui/src/App.tsx',
    'src/etl-sql-vscode/src/extension.ts', 'scripts/Test-MsiUpgrade.ps1',
    'tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj',
    '../docs/releases/file.md', 'docs\releases\file.md'
)) {
    if (-not (Get-MsiUpgradeImpact -ChangedPaths @($path)).ShouldRun) { throw "Missed impact: $path" }
}
foreach ($paths in @(@(), @('TODO.md'), @('docs/releases/v1.2.3.md', 'docs/architecture/engine.md'),
    @('tests/ETL-SQL.Portal.BrowserTests/NavigationTests.cs'),
    @('TODO.md', 'tests/ETL-SQL.Portal.BrowserTests/Fixtures/PortalFixture.cs'))) {
    if ((Get-MsiUpgradeImpact -ChangedPaths $paths).ShouldRun) { throw "Unexpected impact: $paths" }
}
$mixed = Get-MsiUpgradeImpact -ChangedPaths @('TODO.md', 'src/ETL-SQL.Portal/Program.cs')
if (-not $mixed.ShouldRun -or $mixed.RelevantPaths.Count -ne 1) { throw 'Mixed change did not select the runtime path.' }
$mixedBrowser = Get-MsiUpgradeImpact -ChangedPaths @('tests/ETL-SQL.Portal.BrowserTests/NavigationTests.cs', 'src/ETL-SQL.Portal/Program.cs')
if (-not $mixedBrowser.ShouldRun -or $mixedBrowser.RelevantPaths[0] -cne 'src/ETL-SQL.Portal/Program.cs') { throw 'Browser/runtime change skipped the payload.' }

# Exercise the workflow's real comparison/fallback block with an unavailable base.
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workflow = Get-Content (Join-Path $root '.github/workflows/msi-upgrade.yml') -Raw
$start = $workflow.IndexOf('          if ([string]::IsNullOrWhiteSpace($baseSha)')
$end = $workflow.IndexOf('          if ($shouldRun)', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate workflow impact block.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('msi-impact-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$previousOutput = $env:GITHUB_OUTPUT
$previousEvent = $env:EVENT_NAME
$previousCall = $env:RELEASE_CALL
$previousCandidate = $env:CERTIFICATION_SHA
try {
    $policyPath = Join-Path $fixture 'policy.json'
    $policy = Get-Content (Join-Path $PSScriptRoot '../ci-impact.json') -Raw
    foreach ($invalid in @('{}', '{', ($policy -replace '"nativePackages": false', '"nativePackages": "false"'),
        ($policy -replace '"pattern": "\^"', '"pattern": "^["'),
        ($policy -replace '"lanes": "\*",\s*"nativePackages": true', '"lanes": "*", "nativePackages": false'))) {
        Set-Content -LiteralPath $policyPath -Value $invalid
        $rejected = $false
        try { $null = Get-MsiUpgradeImpact -ChangedPaths @('TODO.md') -ManifestPath $policyPath }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'Invalid package-impact policy was accepted.' }
    }
    $noFallback = $policy | ConvertFrom-Json
    $noFallback.rules = @($noFallback.rules | Select-Object -SkipLast 1)
    $noFallback | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $policyPath
    if (-not (Get-MsiUpgradeImpact -ChangedPaths @('unknown/file') -ManifestPath $policyPath).ShouldRun) { throw 'Unknown paths without a catch-all skipped MSI.' }
    $forceStart = $workflow.IndexOf('$actualSha = (git rev-parse HEAD).Trim()')
    $forceEnd = $workflow.IndexOf('$baseSha = if', $forceStart)
    if ($forceStart -lt 0 -or $forceEnd -le $forceStart) { throw 'Cannot locate MSI force/identity guard.' }
    $forceScript = Join-Path $fixture 'force.ps1'
    Set-Content -LiteralPath $forceScript -Value ($workflow.Substring($forceStart, $forceEnd - $forceStart) -replace '(?m)^          ', '')
    $env:GITHUB_OUTPUT = Join-Path $fixture 'output.txt'
    $env:CERTIFICATION_SHA = 'a' * 40
    function git { $global:LASTEXITCODE = 0; $env:CERTIFICATION_SHA }
    foreach ($case in @(
        @{ Event = 'workflow_dispatch'; Call = 'false'; Forced = $true },
        @{ Event = 'push'; Call = 'true'; Forced = $true },
        @{ Event = 'pull_request'; Call = 'false'; Forced = $false }
    )) {
        $env:EVENT_NAME = $case.Event
        $env:RELEASE_CALL = $case.Call
        Set-Content -LiteralPath $env:GITHUB_OUTPUT -Value ''
        & $forceScript
        $forced = (Get-Content -LiteralPath $env:GITHUB_OUTPUT -Raw) -match 'should_run=true'
        if ($forced -ne $case.Forced) { throw 'Manual/release MSI certification scope changed.' }
    }
    $scriptFile = Join-Path $fixture 'classify.ps1'
    Set-Content $scriptFile ($workflow.Substring($start, $end - $start))
    $env:GITHUB_OUTPUT = Join-Path $fixture 'output.txt'
    function git { $global:LASTEXITCODE = 1; $global:MsiImpactGitCalls++ }
    foreach ($baseSha in @('', ('0' * 40), 'unavailable-base')) {
        $global:MsiImpactGitCalls = 0
        Set-Content $env:GITHUB_OUTPUT ''
        & $scriptFile
        if ((Get-Content $env:GITHUB_OUTPUT -Raw) -notmatch 'should_run=true') { throw 'Unavailable base skipped certification.' }
        $expectedCalls = if ($baseSha -eq 'unavailable-base') { 1 } else { 0 }
        if ($global:MsiImpactGitCalls -ne $expectedCalls) { throw 'Comparison retried against an incomplete range.' }
    }
} finally {
    $env:GITHUB_OUTPUT = $previousOutput
    $env:EVENT_NAME = $previousEvent
    $env:RELEASE_CALL = $previousCall
    $env:CERTIFICATION_SHA = $previousCandidate
    # This exact private directory was created above; no caller-supplied paths are removed.
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
Write-Host 'MSI impact: shared package rules skip docs/browser tests; shipped/shared/unknown paths and unavailable ranges certify; invalid policy is rejected.'
