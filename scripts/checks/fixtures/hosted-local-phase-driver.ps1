param([string]$Configuration, [switch]$IncludeSlt, [switch]$IncludeDockerIntegration, [switch]$IncludeStandardScale, [string]$OutDir, [switch]$Resume)
$ErrorActionPreference = 'Stop'
if ($Configuration -cne 'Release' -or -not $IncludeSlt -or -not $IncludeDockerIntegration -or -not $IncludeStandardScale) { throw 'Incomplete fixture driver scope.' }
$RepoRoot = $PWD.Path
. ./scripts/Release.Helpers.ps1
$functions = Get-Content ./scripts/phase-functions.ps1 -Raw
. ([scriptblock]::Create($functions))
$plan = @(Get-Content ./phase-plan.json -Raw | ConvertFrom-Json)
$RunDir = Join-Path $RepoRoot "$OutDir/attempt-$env:GITHUB_RUN_ATTEMPT"
$StatePath = Join-Path $RepoRoot "$OutDir/latest/state.json"
New-Item -ItemType Directory -Path $RunDir,(Split-Path -Parent $StatePath) -Force | Out-Null
$source = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
$previousPhaseMap = @{}
if ($Resume) { foreach ($phase in (Get-Content $StatePath -Raw | ConvertFrom-Json).phases) { $previousPhaseMap[$phase.name] = $phase } }
function Start-PhaseWatchdog { return $null }
function Stop-PhaseWatchdog { return $null }
function Save-State {
    param([object[]]$Results, [string]$Status, [string]$Fingerprint)
    [ordered]@{
        status = $Status; configuration = 'Release'; evidenceSchemaVersion = 1; commitHash = $source.commit
        sourceFingerprint = $Fingerprint; generatedAt = [DateTimeOffset]::UtcNow.ToString('o'); plannedPhases = $plan
        scope = @{ quick = $false; forceResume = $false; skipNode = $false; skipScale = $false
            includeSlt = $true; includeDockerIntegration = $true; includeStandardScale = $true }
        phases = @($Results)
    } | ConvertTo-Json -Depth 12 | Set-Content $StatePath -Encoding utf8
}
$results = [Collections.Generic.List[object]]::new()
foreach ($phaseName in $plan) {
    $action = {
        $phaseName | Add-Content (Join-Path $RepoRoot "$OutDir/actions.txt")
        if ($phaseName -eq 'Dotnet build') {
            $null = New-Item -ItemType Directory -Path ./src/Engine/bin/Release -Force
            [IO.File]::WriteAllText((Join-Path $RepoRoot 'src/Engine/bin/Release/engine.dll'), 'Unchanged compiled candidate')
        }
        if ($phaseName -eq $env:FIXTURE_FAILED_PHASE) { 'Original synthetic phase failure'; $global:LASTEXITCODE = 7 }
        else { "Synthetic action $phaseName"; $global:LASTEXITCODE = 0 }
    }
    $always = $phaseName -in @('Source preflight', 'Asset preparation', 'Asset drift check', 'Asset ownership check')
    Invoke-LoggedPhase -Name $phaseName -Command "fixture $phaseName" -Action $action -PreviousPhaseMap $previousPhaseMap `
        -Fingerprint $source.fingerprint -Results $results -AlwaysRun:$always | Out-Host
}
$status = if (@($results | Where-Object status -ne Passed).Count -eq 0) { 'Passed' } else { 'Failed' }
Save-State -Results $results.ToArray() -Status $status -Fingerprint $source.fingerprint
if ($status -ne 'Passed') { exit 1 }
exit 0
