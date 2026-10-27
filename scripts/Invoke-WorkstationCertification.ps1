# Run the independent Windows gate locally. Uploading its evidence is a separate operator action.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Workstation certification requires Windows x64.'
}
if ($env:GITHUB_ACTIONS -eq 'true' -or $env:GITHUB_RUN_ID) { throw 'Run this check manually on the workstation, outside GitHub Actions.' }
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Release.Helpers.ps1')
Push-Location $repoRoot
try {
    $source = Get-ReleaseCertificationSource -RepoRoot $repoRoot
    if ($source.dirtyPaths.Count -ne 0) { throw 'Start workstation certification on a clean candidate.' }
    $evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
    if (Test-Path -LiteralPath $evidence) { throw 'Evidence directory already exists. Preserve the previous run and choose a new directory.' }
    $nodeVersion = (& node --version) -join ''
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v22\.') { throw 'Use the pinned Node.js 22 toolchain.' }
    New-Item -ItemType Directory -Path $evidence | Out-Null
    $started = [datetimeoffset]::UtcNow.ToString('o')
    @{
        executionMode = 'manual-workstation'; separateFromHostedCI = $true
        sourceSha = $source.commit; configuration = 'Release'; startedUtc = $started
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'invocation.json') -Encoding utf8
    $exitCode = 1
    try {
        & (Join-Path $PSScriptRoot 'Invoke-CertificationProcess.ps1') -ScriptPath scripts/Test-PreRelease.ps1 `
            -ScriptArguments @('-Configuration', 'Release', '-IncludeSlt', '-IncludeDockerIntegration', '-IncludeStandardScale',
                '-OutDir', (Join-Path $evidence 'validation')) -EvidenceDirectory (Join-Path $evidence 'process') -TimeoutSeconds 24000
        $exitCode = $LASTEXITCODE
        Assert-ReleaseCertificationSourceUnchanged -RepoRoot $repoRoot -Initial $source
    } catch {
        $exitCode = 1
        throw
    } finally {
        @{
            executionMode = 'manual-workstation'; sourceSha = $source.commit
            status = $(if ($exitCode -eq 0) { 'passed' } else { 'failed' }); exitCode = $exitCode
            startedUtc = $started; completedUtc = [datetimeoffset]::UtcNow.ToString('o')
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'process-result.json') -Encoding utf8
    }
    if ($exitCode -ne 0) { throw "Workstation certification failed. Preserve diagnostics at $evidence." }
    New-Item -ItemType Directory -Path (Join-Path $evidence 'portable') | Out-Null
    & node (Join-Path $PSScriptRoot 'workstation-release-evidence.mjs') create `
        (Join-Path $evidence 'validation/latest/state.json') (Join-Path $evidence 'invocation.json') `
        (Join-Path $evidence 'process-result.json') (Join-Path $evidence 'portable/bundle')
    if ($LASTEXITCODE -ne 0) { throw 'Workstation evidence verification failed; original diagnostics are retained.' }
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $evidence 'portable'), (Join-Path $evidence 'windows-workstation-evidence.zip'))
    Write-Host "Independent evidence ready: $(Join-Path $evidence 'windows-workstation-evidence.zip')"
} finally {
    Pop-Location
}
