[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$attemptClock = [Diagnostics.Stopwatch]::StartNew()
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

$identity = [ordered]@{
    repository = $env:GITHUB_REPOSITORY; sha = $env:GITHUB_SHA
    runId = [long]$env:GITHUB_RUN_ID; attempt = [int]$env:GITHUB_RUN_ATTEMPT
    runnerName = $env:RUNNER_NAME; toolchain = Get-ReleaseCertificationToolchain
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
    $remainingSeconds = 24000 - [int][Math]::Ceiling($attemptClock.Elapsed.TotalSeconds)
    if ($remainingSeconds -le 0) { throw 'Controlled input verification exhausted the execution budget; retain the checkpoint diagnostics.' }
    & (Join-Path $PSScriptRoot 'Invoke-CertificationProcess.ps1') -ScriptPath scripts/Test-PreRelease.ps1 `
        -ScriptArguments $arguments -EvidenceDirectory (Join-Path $sessionRoot "process-$env:GITHUB_RUN_ATTEMPT") -TimeoutSeconds $remainingSeconds
    $exitCode = $LASTEXITCODE
} finally {
    Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $source
    $identity.toolchain = Get-ReleaseCertificationToolchain
    $finalInputs = Join-Path $sessionRoot "attempt-$env:GITHUB_RUN_ATTEMPT.final-inputs.json"
    $identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $finalInputs -Encoding utf8
    $checkpointText = & node (Join-Path $PSScriptRoot 'hosted-local-resume.mjs') finish $RepoRoot $finalInputs $exitCode
    if ($LASTEXITCODE -ne 0) { throw 'Controlled local attempt could not close its checkpoint; inspect original diagnostics before recovery.' }
    $checkpoint = ($checkpointText -join "`n") | ConvertFrom-Json
    Write-Host "Checkpoint retained: $($checkpoint.cache.files) cached files, $($checkpoint.cache.bytes) bytes, $($checkpoint.cache.elapsedMilliseconds) ms."
}
exit $exitCode
