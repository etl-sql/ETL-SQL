using System.Text.Json;

namespace ETL_SQL.Tests.Docs;

public sealed class ScaleCertificationProcessTests
{
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
