# Run the independent Windows gate locally. Uploading its evidence is a separate operator action.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$EvidenceDirectory,
    [switch]$Resume
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
    $nodeVersion = (& node --version) -join ''
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v22\.') { throw 'Use the pinned Node.js 22 toolchain.' }
    $identity = @{
        sourceSha = $source.commit; toolchain = Get-ReleaseCertificationToolchain
        host = @{ platform = 'win32'; architecture = 'x64'; name = [Environment]::MachineName }
    }
    $identityFile = [IO.Path]::GetTempFileName()
    $planFile = [IO.Path]::GetTempFileName()
    try {
        $identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $identityFile -Encoding utf8
        $mode = if ($Resume) { 'resume' } else { 'begin' }
        $planText = & node (Join-Path $PSScriptRoot 'workstation-resume.mjs') $mode $repoRoot $evidence $identityFile
        if ($LASTEXITCODE -ne 0) { throw 'Workstation checkpoint verification failed. No certification work was started.' }
        $plan = ($planText -join "`n") | ConvertFrom-Json
        # Preserve timestamp strings exactly; JSON date conversion can trim fractional zeros.
        ($planText -join "`n") | Set-Content -LiteralPath $planFile -Encoding utf8
        $arguments = @('-Configuration', 'Release', '-IncludeSlt', '-IncludeDockerIntegration', '-IncludeStandardScale',
            '-OutDir', (Join-Path $evidence 'validation'))
        if ($plan.resume) { $arguments += '-Resume' }
        Write-Host "Independent workstation attempt $($plan.attempt); resume=$($plan.resume)."
        $exitCode = 1
        try {
            & (Join-Path $PSScriptRoot 'Invoke-CertificationProcess.ps1') -ScriptPath scripts/Test-PreRelease.ps1 `
                -ScriptArguments $arguments -EvidenceDirectory (Join-Path $evidence $plan.processDirectory) -TimeoutSeconds 24000
            $exitCode = $LASTEXITCODE
            Assert-ReleaseCertificationSourceUnchanged -RepoRoot $repoRoot -Initial $source
        } catch {
            $exitCode = 1
            throw
        } finally {
            try {
                Assert-ReleaseCertificationSourceUnchanged -RepoRoot $repoRoot -Initial $source
                $identity.toolchain = Get-ReleaseCertificationToolchain
                $identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $identityFile -Encoding utf8
                & node (Join-Path $PSScriptRoot 'workstation-resume.mjs') finish $repoRoot $evidence $identityFile $planFile $exitCode
                if ($LASTEXITCODE -ne 0) { throw 'Workstation checkpoint could not close; inspect retained diagnostics before recovery.' }
            } finally {
                $resultFile = Join-Path $evidence $plan.resultFile
                if (-not (Test-Path -LiteralPath $resultFile)) {
                    @{
                        executionMode = 'manual-workstation'; sourceSha = $source.commit; status = 'failed'; exitCode = 1
                        startedUtc = $plan.startedUtc; completedUtc = [datetimeoffset]::UtcNow.ToString('o')
                    } | ConvertTo-Json | Set-Content -LiteralPath $resultFile -Encoding utf8
                }
            }
        }
        if ($exitCode -ne 0) { throw "Workstation certification failed. Preserve diagnostics at $evidence." }
        New-Item -ItemType Directory -Path (Join-Path $evidence 'portable') | Out-Null
        & node (Join-Path $PSScriptRoot 'workstation-release-evidence.mjs') create `
            (Join-Path $evidence 'validation/latest/state.json') (Join-Path $evidence 'invocation.json') `
            (Join-Path $evidence $plan.resultFile) (Join-Path $evidence 'portable/bundle')
        if ($LASTEXITCODE -ne 0) { throw 'Workstation evidence verification failed; original diagnostics are retained.' }
        [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $evidence 'portable'), (Join-Path $evidence 'windows-workstation-evidence.zip'))
        Write-Host "Independent evidence ready: $(Join-Path $evidence 'windows-workstation-evidence.zip')"
    } finally {
        Remove-Item -LiteralPath $identityFile, $planFile -Force -ErrorAction SilentlyContinue
    }
} finally {
    Pop-Location
}
