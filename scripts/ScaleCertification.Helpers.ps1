function Get-ScaleCapturePlan {
    param([Parameter(Mandatory)][string]$Tier, [string]$Scenario = '')

    $methods = [ordered]@{
        ExternalSort = 'Cert_Smoke_ExternalSort_50kRows_AllRowsMaterialized'
        ExternalAggregate = 'Cert_Smoke_ExternalAggregate_100kRows_CorrectSums'
        ExternalJoin = 'Cert_Smoke_ExternalJoin_50kRows_CorrectResults'
        TempTableSpill = 'Cert_Smoke_TempTableSpill_50kRows_CorrectCount'
        StreamingSelect = 'Cert_Smoke_StreamingSelect_ResultCapEnforced'
        WindowFunction = 'Cert_Smoke_WindowFunction_50kRows_CorrectRankValues'
        CsvIngest = 'Cert_Smoke_CsvIngest_50kRows_CorrectChecksum'
        ParquetRoundTrip = 'Cert_Smoke_ParquetRoundTrip_50kRows_CorrectChecksum'
        ReportDatasetSnapshotReload = 'Cert_Smoke_ReportDatasetSnapshotReload_50kRows_CorrectChecksum'
        CubeGroupingSets = 'Cert_Smoke_CubeGroupingSets_50kRows_CorrectExpansionAndChecksum'
        ScalarSubqueryCache = 'Cert_Smoke_ScalarSubqueryCache_50kRows_ReusesRepeatedKeys'
        SpillCleanupSuccess = 'Cert_Smoke_SpillCleanup_AfterSuccessfulTempSpill_RemovesNonPersistentFiles'
        SpillCleanupFailure = 'Cert_Smoke_SpillCleanup_AfterFailedTempSpill_RemovesNonPersistentFiles'
    }
    if ($Tier -notin @('Smoke','Standard','Stress','Huge','Provider','All') -or
        ($Scenario -and -not $methods.Contains($Scenario))) { throw 'Unknown scale tier or scenario.' }
    if ($Scenario -or $Tier -in @('Smoke','Standard')) {
        $names = if ($Scenario) { @($methods.Keys | Where-Object { $_ -ieq $Scenario }) } else { @($methods.Keys) }
        foreach ($name in $names) {
            [pscustomobject]@{
                Name = $name
                Filter = "FullyQualifiedName=ETL_SQL.Tests.Scale.ScaleCertificationTests.$($methods[$name])"
                MetricPrefix = "${name}_"
            }
        }
    } else {
        [pscustomobject]@{
            Name = $Tier
            Filter = if ($Tier -eq 'All') { 'Category=ScaleCertification' } else { "Category=ScaleCertification&Tier=$Tier" }
            MetricPrefix = $null
        }
    }
}

function Assert-ScaleChildMetrics {
    param([Parameter(Mandatory)][object]$Plan, [AllowEmptyCollection()][object[]]$Metrics)

    if (@($Metrics).Count -eq 0 -or ($Plan.MetricPrefix -and
        (@($Metrics).Count -ne 1 -or -not ([string]$Metrics[0].scenario).StartsWith($Plan.MetricPrefix, [StringComparison]::Ordinal)))) {
        throw "Scale child $($Plan.Name) did not emit exactly its expected scenario metric."
    }
}

# Scale measurements include result formatting and output. Capture bytes without write-through
# file handles or per-line flushes, so the runner does not inject physical-disk latency into them.
function Start-ScaleCapturedProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$StandardOutputPath,
        [Parameter(Mandatory)][string]$StandardErrorPath,
        [string]$WorkingDirectory = $PWD.Path,
        [hashtable]$ChildEnvironment = @{}
    )

    $outputPath = [IO.Path]::GetFullPath($StandardOutputPath)
    $errorPath = [IO.Path]::GetFullPath($StandardErrorPath)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ($outputPath.Equals($errorPath, $comparison)) { throw 'Output and error capture paths must differ.' }

    $outputStream = $null
    $errorStream = $null
    $process = $null
    try {
        $outputStream = [IO.FileStream]::new($outputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write,
            [IO.FileShare]::ReadWrite, 65536, [IO.FileOptions]::Asynchronous)
        $errorStream = [IO.FileStream]::new($errorPath, [IO.FileMode]::Create, [IO.FileAccess]::Write,
            [IO.FileShare]::ReadWrite, 65536, [IO.FileOptions]::Asynchronous)
        $startInfo = [Diagnostics.ProcessStartInfo]::new($FileName)
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.WorkingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
        # Hidden Windows launchers can leave stdin attached to a console even with piped output.
        # A closed input pipe makes the test tree noninteractive and keeps console writes comparable.
        $startInfo.RedirectStandardInput = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($name in $ChildEnvironment.Keys) { $startInfo.Environment[$name] = [string]$ChildEnvironment[$name] }
        foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::Start($startInfo)
        $process.StandardInput.Close()
        $outputCopy = $process.StandardOutput.BaseStream.CopyToAsync($outputStream)
        $errorCopy = $process.StandardError.BaseStream.CopyToAsync($errorStream)
        return [pscustomobject]@{
            Process = $process
            OutputStream = $outputStream
            ErrorStream = $errorStream
            CopyTask = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($outputCopy, $errorCopy))
        }
    } catch {
        if ($null -ne $process) {
            if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
            $process.Dispose()
        }
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($null -ne $errorStream) { $errorStream.Dispose() }
        throw
    }
}

function Complete-ScaleCapturedProcess {
    param([Parameter(Mandatory)][object]$Capture)

    try {
        $Capture.Process.WaitForExit()
        $null = $Capture.CopyTask.GetAwaiter().GetResult()
        return $Capture.Process.ExitCode
    } finally {
        $Capture.OutputStream.Dispose()
        $Capture.ErrorStream.Dispose()
        $Capture.Process.Dispose()
    }
}

function Assert-ScaleCaptureCompatibility {
    param([Parameter(Mandatory)][object]$BaselineReport, [Parameter(Mandatory)][object]$CurrentReport)

    $baselineMethod = $BaselineReport.config.outputCapture
    $currentMethod = $CurrentReport.config.outputCapture
    if (-not $baselineMethod) { $baselineMethod = 'start-process-redirect-v1' }
    if (-not $currentMethod) { $currentMethod = 'start-process-redirect-v1' }
    if ($baselineMethod -ne $currentMethod) {
        throw "Output-capture methods differ ($baselineMethod vs $currentMethod). Retain the old reference and explicitly calibrate a known-good clean commit with the corrected harness before regression decisions."
    }
    $baselineEnumeration = $BaselineReport.config.preEnumerateTheories
    $currentEnumeration = $CurrentReport.config.preEnumerateTheories
    if ($null -eq $baselineEnumeration) { $baselineEnumeration = $true }
    if ($null -eq $currentEnumeration) { $currentEnumeration = $true }
    if ($baselineEnumeration -ne $currentEnumeration) {
        throw 'Theory discovery settings differ. Calibrate matching measurement fixtures before regression decisions.'
    }
    $baselineStorage = $BaselineReport.config.temporaryStorage
    $currentStorage = $CurrentReport.config.temporaryStorage
    if (-not $baselineStorage) { $baselineStorage = 'shared-process-temp-v1' }
    if (-not $currentStorage) { $currentStorage = 'shared-process-temp-v1' }
    if ($baselineStorage -ne $currentStorage) {
        throw 'Temporary-storage fixtures differ. Calibrate matching measurement fixtures before regression decisions.'
    }
    # Reports from before per-scenario warm-up measured whatever earlier scenarios left warm.
    $baselineWarmUp = $BaselineReport.config.scenarioWarmUp
    $currentWarmUp = $CurrentReport.config.scenarioWarmUp
    if (-not $baselineWarmUp) { $baselineWarmUp = 'none' }
    if (-not $currentWarmUp) { $currentWarmUp = 'none' }
    if ($baselineWarmUp -ne $currentWarmUp) {
        throw "Scenario warm-up methods differ ($baselineWarmUp vs $currentWarmUp). Recalibrate the baseline with the same warm-up before regression decisions."
    }
    $baselineDiscardedWarmUp = $BaselineReport.config.discardedProcessWarmUp
    $currentDiscardedWarmUp = $CurrentReport.config.discardedProcessWarmUp
    if ($null -ne $baselineDiscardedWarmUp -or $null -ne $currentDiscardedWarmUp) {
        if ($baselineDiscardedWarmUp -isnot [bool] -or $currentDiscardedWarmUp -isnot [bool] -or
            -not $baselineDiscardedWarmUp -or -not $currentDiscardedWarmUp -or
            $baselineWarmUp -cne 'own-path-untimed-v1' -or $currentWarmUp -cne 'own-path-untimed-v1') {
            throw 'Baseline comparisons require recorded discarded-process and own-path warm-up; diagnostic or incomplete captures cannot qualify.'
        }
    }
    $baselineIsolation = $BaselineReport.config.processIsolation
    $currentIsolation = $CurrentReport.config.processIsolation
    if (-not $baselineIsolation) { $baselineIsolation = 'shared-testhost-v1' }
    if (-not $currentIsolation) { $currentIsolation = 'shared-testhost-v1' }
    if ($baselineIsolation -cne $currentIsolation) {
        throw 'Scale process isolation differs. Recalibrate matching per-scenario captures before regression decisions.'
    }
    $baselineFixture = $BaselineReport.config.testFixture
    $currentFixture = $CurrentReport.config.testFixture
    if (-not $baselineFixture) { $baselineFixture = 'full-test-assembly-v1' }
    if (-not $currentFixture) { $currentFixture = 'full-test-assembly-v1' }
    if ($baselineFixture -ne $currentFixture -or
        ($currentFixture -eq 'isolated-scale-v1' -and
            ([string]::IsNullOrWhiteSpace($BaselineReport.config.fixtureDefinitionSha256) -or
             [string]::IsNullOrWhiteSpace($CurrentReport.config.fixtureDefinitionSha256))) -or
        $BaselineReport.config.fixtureDefinitionSha256 -ne $CurrentReport.config.fixtureDefinitionSha256) {
        throw 'Scale test fixtures differ. Calibrate the same isolated fixture before regression decisions.'
    }
}

function Copy-ScaleCertificationFixture {
    param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$Destination)

    New-Item -ItemType Directory -Path $Destination -ErrorAction Stop | Out-Null
    foreach ($file in @('ETL-SQL.Scale.Tests.csproj','ScaleCertificationTests.cs','ScaleTestHelpers.cs')) {
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot "tests/ETL-SQL.Scale.Tests/$file") -Destination (Join-Path $Destination $file)
    }
    $support = Join-Path $Destination 'support'
    New-Item -ItemType Directory -Path $support | Out-Null
    foreach ($file in @('ScenarioResourceSampler.cs','StreamingRowSource.cs')) {
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot "tests/ETL-SQL.Tests/Scale/$file") -Destination (Join-Path $support $file)
    }
}

function Assert-ScaleComparisonEvidence {
    param([Parameter(Mandatory)][object]$Report)

    if ($Report.testsPassed -isnot [bool] -or -not $Report.testsPassed -or @($Report.scenarios).Count -eq 0) {
        throw 'Baseline comparison requires an explicitly passing, nonempty current scale report.'
    }
    foreach ($scenario in @($Report.scenarios)) {
        if ($scenario.passed -isnot [bool] -or -not $scenario.passed -or
            [string]::IsNullOrWhiteSpace($scenario.scenario) -or $scenario.samples -lt 2) {
            throw 'Baseline comparison requires passing named scenarios with at least two samples each.'
        }
    }
}
