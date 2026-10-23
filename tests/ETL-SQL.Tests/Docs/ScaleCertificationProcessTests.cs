using System.Text.Json;

namespace ETL_SQL.Tests.Docs;

public sealed class ScaleCertificationProcessTests
{
    [Fact]
    public async Task Runner_RecordsDiagnosticSkippedProcessWarmUp()
    {
        var result = await ReleasePolicyHelperTests.RunHelper(
            "Test-ScaleRunnerFixture -Tier Smoke -Mode success -NoWarmUp", helperName: "checks/fixtures/scale-capture-runner.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Validated Smoke/success", result.Output);
    }

    [Theory]
    [InlineData("missing", "missing", true)]
    [InlineData("true", "true", true)]
    [InlineData("true", "missing", false)]
    [InlineData("missing", "true", false)]
    [InlineData("false", "false", false)]
    [InlineData("false", "true", false)]
    [InlineData("true", "false", false)]
    [InlineData("string", "true", false)]
    [InlineData("true", "disabled-own", false)]
    [InlineData("disabled-own", "disabled-own", false)]
    public async Task BaselineComparison_RejectsDiagnosticOrUnrecordedWarmUpScope(string baseline, string current, bool accepted)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            function Config($kind) {
                $config = @{scenarioWarmUp='own-path-untimed-v1'}
                if ($kind -ne 'missing') { $config.discardedProcessWarmUp = $kind -notin @('false','string') }
                if ($kind -eq 'string') { $config.discardedProcessWarmUp = 'true' }
                if ($kind -eq 'disabled-own') { $config.scenarioWarmUp='none' }
                $config
            }
            Assert-ScaleCaptureCompatibility -BaselineReport @{config=(Config '{{baseline}}')} -CurrentReport @{config=(Config '{{current}}')}
            'Compatible'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(accepted, result.ExitCode == 0);
        Assert.Contains(accepted ? "Compatible" : "warm-up", result.Output);
    }

    [Theory]
    [InlineData("Smoke", "success")]
    [InlineData("Standard", "success")]
    [InlineData("Smoke", "missing")]
    [InlineData("Smoke", "wrong")]
    [InlineData("Smoke", "duplicate")]
    [InlineData("Smoke", "failed")]
    [InlineData("Smoke", "drift")]
    public async Task Runner_RetainsCompleteIsolatedSamplesAndRejectsIncompleteEvidence(string tier, string mode)
    {
        var result = await ReleasePolicyHelperTests.RunHelper(
            $"Test-ScaleRunnerFixture -Tier {tier} -Mode {mode}", helperName: "checks/fixtures/scale-capture-runner.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains($"Validated {tier}/{mode}", result.Output);
    }

    [Theory]
    [InlineData("Smoke")]
    [InlineData("Standard")]
    public async Task CapturePlan_IsolatesEveryScenarioWithTheSameFilterAsAnIndividualRun(string tier)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $plan = @(Get-ScaleCapturePlan -Tier '{{tier}}')
            if ($plan.Count -ne 13 -or @($plan.Name | Select-Object -Unique).Count -ne 13) { throw 'Incomplete tier.' }
            foreach ($child in $plan) {
                $single = @(Get-ScaleCapturePlan -Tier '{{tier}}' -Scenario $child.Name)
                if ($single.Count -ne 1 -or $single[0].Filter -cne $child.Filter -or
                    $child.Filter -notlike 'FullyQualifiedName=ETL_SQL.Tests.Scale.ScaleCertificationTests.*') { throw 'Different capture paths.' }
                Assert-ScaleChildMetrics -Plan $child -Metrics @(@{scenario=$child.MetricPrefix + 'rows'})
            }
            'Complete isolated tier'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Complete isolated tier", result.Output);
    }

    [Fact]
    public async Task CapturePlan_NormalizesCaseInsensitiveScenarioInputBeforeCheckingMetrics()
    {
        var result = await ReleasePolicyHelperTests.RunHelper("""
            foreach ($expected in @(Get-ScaleCapturePlan -Tier Smoke)) {
                $actual = @(Get-ScaleCapturePlan -Tier smoke -Scenario $expected.Name.ToLowerInvariant())[0]
                if ($actual.Name -cne $expected.Name -or $actual.Filter -cne $expected.Filter -or
                    $actual.MetricPrefix -cne $expected.MetricPrefix) { throw 'Scenario identity changed.' }
                Assert-ScaleChildMetrics -Plan $actual -Metrics @(@{scenario=$expected.MetricPrefix + 'rows'})
            }
            'Canonical scenario identity'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Canonical scenario identity", result.Output);
    }

    [Theory]
    [InlineData("Stress")]
    [InlineData("Huge")]
    [InlineData("Provider")]
    [InlineData("All")]
    public async Task CapturePlan_PreservesTheOtherTierEntryPoints(string tier)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $plan = @(Get-ScaleCapturePlan -Tier '{{tier}}')
            if ($plan.Count -ne 1 -or $plan[0].MetricPrefix) { throw 'Unexpected isolation.' }
            $plan[0].Filter
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(tier == "All" ? "Category=ScaleCertification" : $"Tier={tier}", result.Output);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("wrong-scenario")]
    public async Task CapturePlan_RejectsMissingDuplicateOrDifferentChildMetrics(string fixture)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $plan = @(Get-ScaleCapturePlan -Tier Smoke -Scenario ExternalSort)[0]
            $metrics = @(@{scenario='ExternalSort_50000_DESC'})
            switch ('{{fixture}}') {
                'empty' { $metrics=@() }
                'duplicate' { $metrics += $metrics[0] }
                'wrong-scenario' { $metrics[0].scenario='ExternalJoin_50000_equality' }
            }
            Assert-ScaleChildMetrics -Plan $plan -Metrics $metrics
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("expected scenario metric", result.Output);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("shared-testhost-v1", "shared-testhost-v1", true)]
    [InlineData("single-scenario-process-v1", "single-scenario-process-v1", true)]
    [InlineData("", "single-scenario-process-v1", false)]
    [InlineData("single-scenario-process-v1", "shared-testhost-v1", false)]
    public async Task BaselineComparison_RequiresMatchingProcessIsolation(string baseline, string current, bool compatible)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $baselineConfig = @{processIsolation='{{baseline}}'}
            $currentConfig = @{processIsolation='{{current}}'}
            Assert-ScaleCaptureCompatibility -BaselineReport @{config=$baselineConfig} -CurrentReport @{config=$currentConfig}
            'Compatible'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(compatible, result.ExitCode == 0);
        Assert.Contains(compatible ? "Compatible" : "Scale process isolation differs", result.Output);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("", "current", false)]
    [InlineData("current", "current", true)]
    [InlineData("previous", "current", false)]
    [InlineData("missing", "missing", false)]
    public async Task BaselineComparison_RequiresMatchingFixtureDefinitions(string baseline, string current, bool compatible)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $baselineConfig = @{}
            $currentConfig = @{}
            if ('{{baseline}}') { $baselineConfig.testFixture='isolated-scale-v1'; $baselineConfig.fixtureDefinitionSha256='{{baseline}}' }
            if ('{{current}}') { $currentConfig.testFixture='isolated-scale-v1'; $currentConfig.fixtureDefinitionSha256='{{current}}' }
            if ('{{baseline}}' -eq 'missing') { $baselineConfig.Remove('fixtureDefinitionSha256') }
            if ('{{current}}' -eq 'missing') { $currentConfig.Remove('fixtureDefinitionSha256') }
            Assert-ScaleCaptureCompatibility -BaselineReport @{config=$baselineConfig} -CurrentReport @{config=$currentConfig}
            'Compatible'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(compatible, result.ExitCode == 0);
        Assert.Contains(compatible ? "Compatible" : "Scale test fixtures differ", result.Output);
    }

    [Theory]
    [InlineData("passed", true)]
    [InlineData("failed-report", false)]
    [InlineData("empty", false)]
    [InlineData("missing-status", false)]
    [InlineData("failed-scenario", false)]
    [InlineData("single-sample", false)]
    [InlineData("missing-samples", false)]
    public async Task BaselineComparison_RejectsIncompleteCurrentEvidence(string fixture, bool accepted)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $scenario = @{ scenario='ExternalSort_500000_DESC'; passed=$true; samples=3 }
            $report = @{ testsPassed=$true; scenarios=@($scenario) }
            switch ('{{fixture}}') {
                'failed-report' { $report.testsPassed=$false }
                'empty' { $report.scenarios=@() }
                'missing-status' { $report.Remove('testsPassed') }
                'failed-scenario' { $scenario.passed=$false }
                'single-sample' { $scenario.samples=1 }
                'missing-samples' { $scenario.Remove('samples') }
            }
            Assert-ScaleComparisonEvidence -Report $report
            'Accepted'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(accepted, result.ExitCode == 0);
        Assert.Contains(accepted ? "Accepted" : "Baseline comparison requires", result.Output);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("run-owned-process-temp-v1", "run-owned-process-temp-v1", true)]
    [InlineData("", "run-owned-process-temp-v1", false)]
    public async Task BaselineComparison_RequiresMatchingTemporaryStorage(string baseline, string current, bool compatible)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $baselineConfig = @{temporaryStorage='{{baseline}}'}
            $currentConfig = @{temporaryStorage='{{current}}'}
            Assert-ScaleCaptureCompatibility -BaselineReport @{config=$baselineConfig} `
                -CurrentReport @{config=$currentConfig}
            'Compatible'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(compatible, result.ExitCode == 0);
        Assert.Contains(compatible ? "Compatible" : "Temporary-storage fixtures differ", result.Output);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("buffered-byte-stream-v1", "buffered-byte-stream-v1", true)]
    [InlineData("", "buffered-byte-stream-v1", false)]
    [InlineData("buffered-byte-stream-v1", "", false)]
    public async Task BaselineComparison_RequiresMatchingOutputCapture(string baseline, string current, bool compatible)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $baselineConfig = @{outputCapture='{{baseline}}'}
            $currentConfig = @{outputCapture='{{current}}'}
            Assert-ScaleCaptureCompatibility -BaselineReport @{config=$baselineConfig} `
                -CurrentReport @{config=$currentConfig}
            'Compatible'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(compatible, result.ExitCode == 0);
        Assert.Contains(compatible ? "Compatible" : "Output-capture methods differ", result.Output);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("own-path-untimed-v1", "own-path-untimed-v1", true)]
    [InlineData("none", "", true)]
    [InlineData("", "own-path-untimed-v1", false)]
    [InlineData("own-path-untimed-v1", "none", false)]
    public async Task BaselineComparison_RequiresMatchingScenarioWarmUp(string baseline, string current, bool compatible)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $baselineConfig = @{scenarioWarmUp='{{baseline}}'}
            $currentConfig = @{scenarioWarmUp='{{current}}'}
            Assert-ScaleCaptureCompatibility -BaselineReport @{config=$baselineConfig} `
                -CurrentReport @{config=$currentConfig}
            'Compatible'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(compatible, result.ExitCode == 0);
        Assert.Contains(compatible ? "Compatible" : "Scenario warm-up methods differ", result.Output);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task BaselineComparison_RequiresMatchingTheoryDiscovery(bool baseline, bool current, bool compatible)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $baselineFlag = ${{baseline.ToString().ToLowerInvariant()}}
            $currentFlag = ${{current.ToString().ToLowerInvariant()}}
            $baselineConfig = @{preEnumerateTheories=$baselineFlag}
            $currentConfig = @{preEnumerateTheories=$currentFlag}
            Assert-ScaleCaptureCompatibility -BaselineReport @{config=$baselineConfig} `
                -CurrentReport @{config=$currentConfig}
            'Compatible'
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.Equal(compatible, result.ExitCode == 0);
        Assert.Contains(compatible ? "Compatible" : "Theory discovery settings differ", result.Output);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Capture_PreservesBothByteStreamsAndTheChildExitCode(int exitCode)
    {
        var result = await ReleasePolicyHelperTests.RunHelper($$"""
            $root = Join-Path ([IO.Path]::GetTempPath()) ('etlsql_capture_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $root | Out-Null
            try {
                $script = Join-Path $root 'child with spaces.ps1'
                $child = @'
            $bytes = [Text.Encoding]::UTF8.GetBytes(('line αβ🙂' + "`r`n`r`n") * 100000)
            $stdout = [Console]::OpenStandardOutput()
            $stderr = [Console]::OpenStandardError()
            $stdout.Write($bytes)
            $stderr.Write($bytes)
            exit {{exitCode}}
            '@
                Set-Content -LiteralPath $script -Value $child -Encoding utf8NoBOM
                $stdoutPath = Join-Path $root 'stdout.log'
                $stderrPath = Join-Path $root 'stderr.log'
                $capture = Start-ScaleCapturedProcess -FileName (Get-Process -Id $PID).Path `
                    -Arguments @('-NoProfile', '-NonInteractive', '-File', $script) `
                    -StandardOutputPath $stdoutPath -StandardErrorPath $stderrPath
                $exit = Complete-ScaleCapturedProcess $capture
                $expected = [Text.Encoding]::UTF8.GetBytes(('line αβ🙂' + "`r`n`r`n") * 100000)
                $output = [IO.File]::ReadAllBytes($stdoutPath)
                $errorOutput = [IO.File]::ReadAllBytes($stderrPath)
                $expectedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($expected))
                @{
                    exitCode = $exit
                    expectedBytes = $expected.Length
                    stdoutBytes = $output.Length
                    stderrBytes = $errorOutput.Length
                    stdoutMatches = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($output)) -eq $expectedHash)
                    stderrMatches = ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($errorOutput)) -eq $expectedHash)
                } | ConvertTo-Json -Compress
            } finally { Remove-Item -LiteralPath $root -Recurse -Force }
            """, helperName: "ScaleCertification.Helpers.ps1");

        Assert.True(result.ExitCode == 0, result.Output);
        using var evidence = JsonDocument.Parse(result.Output);
        var actual = evidence.RootElement;
        Assert.Equal(exitCode, actual.GetProperty("exitCode").GetInt32());
        Assert.True(actual.GetProperty("expectedBytes").GetInt32() > 1_000_000);
        Assert.Equal(actual.GetProperty("expectedBytes").GetInt32(), actual.GetProperty("stdoutBytes").GetInt32());
        Assert.Equal(actual.GetProperty("expectedBytes").GetInt32(), actual.GetProperty("stderrBytes").GetInt32());
        Assert.True(actual.GetProperty("stdoutMatches").GetBoolean());
        Assert.True(actual.GetProperty("stderrMatches").GetBoolean());
    }

    [Fact]
    public async Task Capture_ChildInputIsRedirectedAndImmediatelyReachesEndOfFile()
    {
        var result = await ReleasePolicyHelperTests.RunHelper("""
            $root = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('etlsql_capture_' + [guid]::NewGuid().ToString('N'))))
            New-Item -ItemType Directory -Path $root | Out-Null
            try {
                $stdoutPath = Join-Path $root 'stdout.log'
                $stderrPath = Join-Path $root 'stderr.log'
                $child = "@{ redirected = [Console]::IsInputRedirected; input = [Console]::In.ReadToEnd() } | ConvertTo-Json -Compress"
                $capture = Start-ScaleCapturedProcess -FileName (Get-Process -Id $PID).Path `
                    -Arguments @('-NoProfile', '-NonInteractive', '-Command', $child) `
                    -StandardOutputPath $stdoutPath -StandardErrorPath $stderrPath
                if (-not $capture.Process.WaitForExit(10000)) {
                    $capture.Process.Kill($true)
                    $null = Complete-ScaleCapturedProcess $capture
                    throw 'The captured child did not receive end-of-file on stdin.'
                }
                $exitCode = Complete-ScaleCapturedProcess $capture
                if ($exitCode -ne 0) { throw "Child failed: $([IO.File]::ReadAllText($stderrPath))" }
                [IO.File]::ReadAllText($stdoutPath)
            } finally {
                $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
                if (-not $root.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar) -or
                    (Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                    throw 'Capture fixture cleanup escaped its owned temporary directory.'
                }
                Remove-Item -LiteralPath $root -Recurse -Force
            }
            """, helperName: "ScaleCertification.Helpers.ps1");

        Assert.True(result.ExitCode == 0, result.Output);
        using var evidence = JsonDocument.Parse(result.Output);
        Assert.True(evidence.RootElement.GetProperty("redirected").GetBoolean());
        Assert.Equal("", evidence.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task Capture_RejectsOverlappingOutputFiles()
    {
        var result = await ReleasePolicyHelperTests.RunHelper("""
            Start-ScaleCapturedProcess -FileName pwsh -StandardOutputPath same.log -StandardErrorPath same.log
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("paths must differ", result.Output);
    }

    [Fact]
    public async Task Capture_FailedStartReleasesBothLogFiles()
    {
        var result = await ReleasePolicyHelperTests.RunHelper("""
            $root = Join-Path ([IO.Path]::GetTempPath()) ('etlsql_capture_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $root | Out-Null
            try {
                $stdoutPath = Join-Path $root 'stdout.log'
                $stderrPath = Join-Path $root 'stderr.log'
                $failed = $false
                try {
                    Start-ScaleCapturedProcess -FileName (Join-Path $root 'missing-command') `
                        -StandardOutputPath $stdoutPath -StandardErrorPath $stderrPath
                } catch { $failed = $true }
                if (-not $failed) { throw 'Missing command unexpectedly started' }
                foreach ($path in @($stdoutPath, $stderrPath)) {
                    $exclusive = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                    $exclusive.Dispose()
                }
                'Released'
            } finally { Remove-Item -LiteralPath $root -Recurse -Force }
            """, helperName: "ScaleCertification.Helpers.ps1");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("Released", result.Output.Trim());
    }
}
