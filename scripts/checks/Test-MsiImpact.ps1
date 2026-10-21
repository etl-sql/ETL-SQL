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
    'docs/releases/payload.js'
)) {
    if (-not (Get-MsiUpgradeImpact -ChangedPaths @($path)).ShouldRun) { throw "Missed impact: $path" }
}
foreach ($paths in @(@(), @('TODO.md'), @('docs/releases/v1.2.3.md', 'docs/architecture/engine.md'))) {
    if ((Get-MsiUpgradeImpact -ChangedPaths $paths).ShouldRun) { throw "Unexpected impact: $paths" }
}
$mixed = Get-MsiUpgradeImpact -ChangedPaths @('TODO.md', 'src/ETL-SQL.Portal/Program.cs')
if (-not $mixed.ShouldRun -or $mixed.RelevantPaths.Count -ne 1) { throw 'Mixed change did not select the runtime path.' }

# Exercise the workflow's real comparison/fallback block with an unavailable base.
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$workflow = Get-Content (Join-Path $root '.github/workflows/msi-upgrade.yml') -Raw
$start = $workflow.IndexOf('          if ([string]::IsNullOrWhiteSpace($baseSha)')
$end = $workflow.IndexOf('          if ($shouldRun)', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate workflow impact block.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('msi-impact-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$previousOutput = $env:GITHUB_OUTPUT
try {
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
    # This exact private directory was created above; no caller-supplied paths are removed.
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
Write-Host 'MSI impact: runtime/shared/unknown paths certify; known docs skip; unavailable ranges certify without a last-commit fallback.'
