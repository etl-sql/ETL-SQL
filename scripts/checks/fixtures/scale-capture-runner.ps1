function Test-ScaleRunnerFixture {
    param([string]$Tier = 'Smoke', [string]$Mode = 'success', [switch]$NoWarmUp)

    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
    $root = Join-Path ([IO.Path]::GetTempPath()) ('scale_runner_' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    try {
        $scripts = Join-Path $root 'scripts'
        New-Item -ItemType Directory -Path $scripts | Out-Null
        foreach ($name in @('Test-ScaleCertification.ps1','ScaleCertification.Helpers.ps1','Release.Helpers.ps1')) {
            Copy-Item -LiteralPath (Join-Path $repository "scripts/$name") -Destination $scripts
        }
        $project = Join-Path $root 'tests/ETL-SQL.Scale.Tests'
        $support = Join-Path $root 'tests/ETL-SQL.Tests/Scale'
        New-Item -ItemType Directory -Path $project,$support | Out-Null
        foreach ($name in @('ETL-SQL.Scale.Tests.csproj','ScaleCertificationTests.cs','ScaleTestHelpers.cs')) {
            Set-Content -LiteralPath (Join-Path $project $name) -Value 'Fixture source; compilation is replaced in this runner test.'
        }
        foreach ($name in @('ScenarioResourceSampler.cs','StreamingRowSource.cs')) {
            Set-Content -LiteralPath (Join-Path $support $name) -Value 'Fixture support source.'
        }
        $binary = Join-Path $project 'bin/Release/net10.0/ETL-SQL.Scale.Tests.dll'
        New-Item -ItemType Directory -Path (Split-Path -Parent $binary) | Out-Null
        Set-Content -LiteralPath $binary -Value 'No binary is executed.'
        [IO.File]::SetLastWriteTimeUtc($binary, [datetime]::UtcNow.AddMinutes(1))
        @'
function Get-ReleaseSourceFingerprint {
    param($RepoRoot)
    if ($env:SCALE_FIXTURE_MODE -eq 'drift' -and (Test-Path (Join-Path $RepoRoot 'sample-started'))) { return 'changed-source' }
    return 'fixture-source'
}
'@ | Add-Content -LiteralPath (Join-Path $scripts 'Release.Helpers.ps1')
        @'
$global:scaleRunnerFixtureRoot = Split-Path -Parent $PSScriptRoot
function Get-Process {
    [CmdletBinding()]param($Name, $Id)
    if ($Name -eq 'testhost') { return }
    Microsoft.PowerShell.Management\Get-Process @PSBoundParameters
}
function Start-ScaleCapturedProcess {
    param($FileName, $Arguments, $StandardOutputPath, $StandardErrorPath, $WorkingDirectory, $ChildEnvironment)
    $filter = $Arguments[[Array]::IndexOf($Arguments, '--filter') + 1]
    $plan = @(Get-ScaleCapturePlan -Tier Smoke) | Where-Object Filter -CEQ $filter
    if (@($plan).Count -ne 1) { throw 'Runner did not select exactly one scenario.' }
    $measured = [IO.Path]::GetFileName($StandardOutputPath).StartsWith('raw-output-sample')
    if ($measured) { Set-Content (Join-Path $global:scaleRunnerFixtureRoot 'sample-started') -Value 'started' }
    @{ name=$plan.Name; filter=$filter; rowScale=$env:CERT_ROW_SCALE; tier=$env:CERT_CERTIFICATION_TIER; measured=$measured } |
        ConvertTo-Json -Compress | Add-Content (Join-Path $global:scaleRunnerFixtureRoot 'launches.jsonl')
    $target = $measured -and $plan.Name -eq 'ExternalAggregate'
    $exitCode = if ($target -and $env:SCALE_FIXTURE_MODE -eq 'failed') { 7 } else { 0 }
    $metric = @{scenario=$plan.MetricPrefix+'rows'; tier=$env:CERT_CERTIFICATION_TIER;
        rowCount=([int](50000 * [double]$env:CERT_ROW_SCALE)); elapsedMs=10; resultRows=10; checksum=55;
        passed=($exitCode -eq 0); serverGcEnabled=$true; runtimeVersion='.NET 10.0.0'}
    if ($target -and $env:SCALE_FIXTURE_MODE -eq 'wrong') { $metric.scenario='ExternalSort_rows' }
    $lines = @('Passed: 1')
    if (-not ($target -and $env:SCALE_FIXTURE_MODE -eq 'missing')) {
        $lines += 'CERT_METRIC:' + ($metric | ConvertTo-Json -Compress)
        if ($target -and $env:SCALE_FIXTURE_MODE -eq 'duplicate') { $lines += $lines[-1] }
    }
    [IO.File]::WriteAllText($StandardOutputPath, ($lines -join "`n"))
    [IO.File]::WriteAllText($StandardErrorPath, '')
    $process = [pscustomobject]@{ ExitCode=$exitCode }
    $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($Milliseconds) return $true }
    [pscustomobject]@{ Process=$process }
}
function Complete-ScaleCapturedProcess { param($Capture) return $Capture.Process.ExitCode }
'@ | Add-Content -LiteralPath (Join-Path $scripts 'ScaleCertification.Helpers.ps1')
        $oldMode = $env:SCALE_FIXTURE_MODE
        try {
            $env:SCALE_FIXTURE_MODE = $Mode
            $arguments = @('-NoProfile','-File',(Join-Path $scripts 'Test-ScaleCertification.ps1'),'-Tier',$Tier,'-Samples','2','-SkipBuild',
                '-RepositoryRoot',$root,'-OutDir',(Join-Path $root 'output'))
            if ($NoWarmUp) { $arguments += '-NoWarmUp' }
            & pwsh @arguments *> (Join-Path $root 'runner.log')
            $exitCode = $LASTEXITCODE
        } finally { $env:SCALE_FIXTURE_MODE = $oldMode }
        $reportPath = Join-Path $root 'output/cert-report.json'
        if (-not (Test-Path $reportPath)) { throw (Get-Content (Join-Path $root 'runner.log') -Raw) }
        $report = Get-Content $reportPath -Raw | ConvertFrom-Json
        $success = $Mode -eq 'success'
        if (($exitCode -eq 0) -ne $success -or $report.testsPassed -ne $success) { throw "Wrong result: $exitCode; $(Get-Content (Join-Path $root 'runner.log') -Raw)" }
        if ($report.config.processIsolation -cne 'single-scenario-process-v1' -or $report.config.filters.Count -ne 13) { throw 'Missing process plan.' }
        if ($report.config.discardedProcessWarmUp -isnot [bool] -or $report.config.discardedProcessWarmUp -ne (-not $NoWarmUp)) { throw 'Discarded warm-up scope was not recorded.' }
        $launches = @(Get-Content (Join-Path $root 'launches.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
        $scale = if ($Tier -eq 'Standard') { 10 } else { 1 }
        $expectedLaunches = if ($NoWarmUp) { 26 } else { 39 }
        if ($launches.Count -ne $expectedLaunches -or @($launches | Where-Object { $_.tier -cne $Tier -or [double]$_.rowScale -ne $scale }).Count) { throw 'Incorrect tier routing or row scale.' }
        if ($report.sampleReports.Count -ne 2 -or @($report.sampleReports | Where-Object { $_.children.Count -ne 13 }).Count) { throw 'Missing child execution records.' }
        if ($success -and ($report.scenarios.Count -ne 13 -or @($report.scenarios | Where-Object samples -NE 2).Count)) { throw 'Missing aggregated metrics.' }
        if ($Mode -in @('missing','wrong','duplicate','failed') -and @($report.sampleReports | Where-Object exitCode -EQ 0).Count) { throw 'Child failure lost from sample status.' }
        "Validated $Tier/$Mode"
    } finally {
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if (-not ([IO.Path]::GetFullPath($root)).StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar) -or
            (Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Fixture cleanup escaped its temporary directory.' }
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
