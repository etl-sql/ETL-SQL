[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Release.Helpers.ps1')
if ($env:GITHUB_RUN_ID -cnotmatch '^[1-9][0-9]*$' -or $env:GITHUB_RUN_ATTEMPT -cnotmatch '^[1-9][0-9]*$' -or
    $env:GITHUB_SHA -cnotmatch '^[a-f0-9]{40}$' -or [string]::IsNullOrWhiteSpace($env:RUNNER_NAME)) {
    throw 'Controlled local certification requires exact GitHub run, attempt, candidate and runner identity.'
}
$source = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
if ($source.commit -cne $env:GITHUB_SHA -or $source.dirtyPaths.Count -ne 0) { throw 'Controlled local certification requires the clean candidate.' }
$sessionRoot = Join-Path $RepoRoot "release-validation/hosted-local-state/$env:GITHUB_RUN_ID"
New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null

function Get-ControlledToolchain {
    function Read-Tool([string]$Name, [string[]]$Arguments) {
        $output = & $Name @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Cannot snapshot controlled $Name inputs." }
        return ($output -join "`n").Trim()
    }
    function Get-TextHash([string]$Value) {
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
    }
    $executables = foreach ($name in @('node', 'dotnet', 'docker')) {
        $command = Get-Command $name -CommandType Application | Select-Object -First 1
        "$name=$((Get-FileHash -LiteralPath $command.Source -Algorithm SHA256).Hash)"
    }
    $images = (Read-Tool docker @('image', 'ls', '--no-trunc', '--digests', '--format', '{{.Repository}}:{{.Tag}}@{{.Digest}}#{{.ID}}')) -split "`n"
    $loaderEnvironment = Get-ChildItem Env: | Where-Object {
        ($_.Name -match '^(NODE_|DOTNET_|COMPlus_|COREHOST_|MSBUILD)' -or
            $_.Name -in @('PATH', 'NUGET_PACKAGES', 'NUGET_CONFIG_FILE', 'NUGET_FALLBACK_PACKAGES')) -and
        $_.Name -notmatch 'TOKEN|PASSWORD|SECRET|TELEMETRY_SESSION'
    } | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }
    return [ordered]@{
        nodeVersion = Read-Tool node @('--version')
        dotnetVersion = Read-Tool dotnet @('--version')
        runtimesHash = Get-TextHash (Read-Tool dotnet @('--list-runtimes'))
        dockerVersionHash = Get-TextHash (Read-Tool docker @('version', '--format', '{{json .Server}}'))
        dockerImagesHash = Get-TextHash (($images | Sort-Object) -join "`n")
        executablesHash = Get-TextHash ($executables -join "`n")
        environmentHash = Get-TextHash ($loaderEnvironment -join "`n")
        operatingSystem = [Environment]::OSVersion.VersionString
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
}
$identity = [ordered]@{
    repository = $env:GITHUB_REPOSITORY; sha = $env:GITHUB_SHA
    runId = [long]$env:GITHUB_RUN_ID; attempt = [int]$env:GITHUB_RUN_ATTEMPT
    runnerName = $env:RUNNER_NAME; toolchain = Get-ControlledToolchain
}
$identityPath = Join-Path $sessionRoot "attempt-$env:GITHUB_RUN_ATTEMPT.inputs.json"
if (Test-Path -LiteralPath $identityPath) { throw 'This controlled attempt already has inputs; preserve them and use a new workflow attempt.' }
$identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $identityPath -Encoding utf8
$planText = & node (Join-Path $PSScriptRoot 'hosted-local-resume.mjs') begin $RepoRoot $identityPath
if ($LASTEXITCODE -ne 0) { throw 'Controlled local resume preflight failed. No certification work was started.' }
$plan = ($planText -join "`n") | ConvertFrom-Json
$arguments = @('-Configuration', 'Release', '-IncludeSlt', '-IncludeDockerIntegration', '-IncludeStandardScale', '-OutDir', $plan.validationDirectory)
if ($plan.resume) { $arguments += '-Resume' }
Write-Host "Controlled local attempt $env:GITHUB_RUN_ID/$env:GITHUB_RUN_ATTEMPT; resume=$($plan.resume)."
$exitCode = 1
try {
    & (Join-Path $PSScriptRoot 'Invoke-CertificationProcess.ps1') -ScriptPath scripts/Test-PreRelease.ps1 `
        -ScriptArguments $arguments -EvidenceDirectory (Join-Path $sessionRoot "process-$env:GITHUB_RUN_ATTEMPT") -TimeoutSeconds 24000
    $exitCode = $LASTEXITCODE
} finally {
    Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $source
    $identity.toolchain = Get-ControlledToolchain
    $finalInputs = Join-Path $sessionRoot "attempt-$env:GITHUB_RUN_ATTEMPT.final-inputs.json"
    $identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $finalInputs -Encoding utf8
    $checkpointText = & node (Join-Path $PSScriptRoot 'hosted-local-resume.mjs') finish $RepoRoot $finalInputs $exitCode
    if ($LASTEXITCODE -ne 0) { throw 'Controlled local attempt could not close its checkpoint; inspect original diagnostics before recovery.' }
    $checkpoint = ($checkpointText -join "`n") | ConvertFrom-Json
    Write-Host "Checkpoint retained: $($checkpoint.cache.files) cached files, $($checkpoint.cache.bytes) bytes, $($checkpoint.cache.elapsedMilliseconds) ms."
}
exit $exitCode
