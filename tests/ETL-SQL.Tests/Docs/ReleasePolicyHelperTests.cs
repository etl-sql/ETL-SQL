using System.Diagnostics;

namespace ETL_SQL.Tests.Docs;

public sealed class ReleasePolicyHelperTests
{
    private const string Releases = """
        $releases = @(
            @{tag_name='v2.0.0'; draft=$false; prerelease=$false},
            @{tag_name='v1.0.1'; draft=$false; prerelease=$false},
            @{tag_name='v1.0.0'; draft=$false; prerelease=$false},
            @{tag_name='v0.20.0'; draft=$false; prerelease=$false},
            @{tag_name='v0.19.0'; draft=$false; prerelease=$false},
            @{tag_name='v1.0.2'; draft=$true; prerelease=$false},
            @{tag_name='v1.0.3'; draft=$false; prerelease=$true},
            @{tag_name='v3.0.0-rc.1'; draft=$false; prerelease=$true},
            @{tag_name='unrelated-tag'; draft=$false; prerelease=$false}
        )
        """;

    [Theory]
    [InlineData("0.19.1", "v0.19.0")]
    [InlineData("1.0.2", "v1.0.1")]
    [InlineData("2.0.0", "v1.0.1")]
    public async Task PreviousRelease_IsTheHighestPublishedStableVersionBelowTheCandidate(string candidate, string expected)
    {
        var result = await RunHelper(Releases + $"\nSelect-PreviousStableRelease -CurrentVersion '{candidate}' -Releases $releases");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(expected, result.Output.Trim());
    }

    [Fact]
    public async Task PreviousRelease_FailsWhenThereIsNoOlderStableVersion()
    {
        var result = await RunHelper(Releases + "\nSelect-PreviousStableRelease -CurrentVersion '0.1.0' -Releases $releases");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No prior stable", result.Output);
    }

    [Theory]
    [InlineData("v0.19.1", "false")]
    [InlineData("v1.0.2", "false")]
    [InlineData("v2.0.0", "true")]
    [InlineData("v2.0.1", "true")]
    [InlineData("v3.0.0-rc.1", "false")]
    public async Task LatestBadge_RespectsStableVersionOrder(string candidate, string expected)
    {
        var result = await RunHelper(Releases + $"\nGet-ReleaseLatestValue -CandidateTag '{candidate}' -Releases $releases");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(expected, result.Output.Trim());
    }

    [Fact]
    public async Task PreviousRelease_SeesAnOlderReleaseBeyondTheFirstApiPage()
    {
        var result = await RunHelper("""
            $pages = '[ [{"tag_name":"v2.0.0","draft":false,"prerelease":false}], [{"tag_name":"v1.0.0","draft":false,"prerelease":false}] ]'
            $releases = @($pages | ConvertFrom-Json | ForEach-Object { $_ })
            Select-PreviousStableRelease -CurrentVersion '1.0.1' -Releases $releases
            """);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("v1.0.0", result.Output.Trim());
    }

    [Fact]
    public async Task MalformedReleaseRecords_CannotChangeLatest()
    {
        var result = await RunHelper("Get-ReleaseLatestValue -CandidateTag 'v1.0.0' -Releases @(@{tag_name='v2.0.0'})");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("missing boolean", result.Output);
    }

    [Theory]
    [InlineData("Passed", "same command", true)]
    [InlineData("Skipped", "same command", false)]
    [InlineData("Failed", "same command", false)]
    [InlineData("Passed", "changed command", false)]
    public async Task Resume_RequiresAnExecutedMatchingPhase(string status, string command, bool expected)
    {
        var result = await RunHelper($$"""
            $log = [IO.Path]::GetTempFileName()
            try {
                Set-Content -LiteralPath $log -Value 'Executed phase output'
                $phase = @{status='{{status}}'; command='same command'; log=$log; note='Skipped because prerequisite phase failed'}
                Test-PreReleasePhaseReusable -PreviousPhase $phase -Command '{{command}}'
            } finally { Remove-Item -LiteralPath $log -Force }
            """);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(expected.ToString(), result.Output.Trim());
    }

    [Fact]
    public async Task Resume_RefusesMissingEvidence()
    {
        var result = await RunHelper("Test-PreReleasePhaseReusable -PreviousPhase @{status='Passed'; command='same command'; log='missing.log'} -Command 'same command'");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("False", result.Output.Trim());
    }

    [Theory]
    [InlineData("Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12", true)]
    [InlineData("Total tests: 12\n     Passed: 12", true)]
    [InlineData("No test matches the given testcase filter", false)]
    [InlineData("Passed! - Failed: 0, Passed: 0, Skipped: 12, Total: 12", false)]
    public async Task TestOutput_RequiresExecutedTests(string output, bool expected)
    {
        var literal = output.Replace("'", "''");
        var result = await RunHelper($"Test-ReleaseTestOutputExecuted -Output '{literal}'");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(expected.ToString(), result.Output.Trim());
    }

    [Theory]
    [InlineData(12, 12, 12, true)]
    [InlineData(0, 0, 0, false)]
    [InlineData(12, 0, 0, false)]
    [InlineData(12, 10, 10, false)]
    [InlineData(12, 12, 10, false)]
    public async Task TrxEvidence_RequiresACompletePassedRun(int total, int executed, int passed, bool expected)
    {
        var result = await RunHelper($$"""
            $path = [IO.Path]::GetTempFileName()
            try {
                '<TestRun><ResultSummary><Counters total="{{total}}" executed="{{executed}}" passed="{{passed}}" /></ResultSummary></TestRun>' | Set-Content -LiteralPath $path
                Test-ReleaseTrxComplete -Path $path
            } finally { Remove-Item -LiteralPath $path -Force }
            """);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(expected.ToString(), result.Output.Trim());
    }

    [Fact]
    public async Task Fingerprint_ChangesWhenAlreadyDirtyFilesChangeAgain()
    {
        var repository = Path.Combine(Path.GetTempPath(), $"etlsql_fingerprint_{Guid.NewGuid():N}");
        Directory.CreateDirectory(repository);
        try
        {
            var result = await RunHelper("""
                git init --quiet
                Set-Content -LiteralPath tracked.txt -Value original
                git add tracked.txt
                git -c user.name=ReleasePolicyTests -c user.email=release-policy@example.invalid commit --quiet -m foundation
                if ($LASTEXITCODE -ne 0) { throw 'fixture commit failed' }
                Set-Content -LiteralPath tracked.txt -Value first
                $first = Get-ReleaseSourceFingerprint -RepoRoot $PWD.Path
                Set-Content -LiteralPath tracked.txt -Value second
                $second = Get-ReleaseSourceFingerprint -RepoRoot $PWD.Path
                if ($first -eq $second) { throw 'Tracked edits did not change the fingerprint' }
                Set-Content -LiteralPath untracked.txt -Value first
                $third = Get-ReleaseSourceFingerprint -RepoRoot $PWD.Path
                Set-Content -LiteralPath untracked.txt -Value second
                $fourth = Get-ReleaseSourceFingerprint -RepoRoot $PWD.Path
                if ($third -eq $fourth) { throw 'Untracked edits did not change the fingerprint' }
                Write-Output verified
                """, repository);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Equal("verified", result.Output.Trim());
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(repository, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, false)]
    public async Task HaGate_PropagatesChildCommandFailures(int childExitCode, bool expected)
    {
        var result = await RunHelper($$"""
            $tokens = $null
            $errors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PWD 'scripts/Test-HaSoakContracts.ps1'), [ref]$tokens, [ref]$errors)
            $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Step' }, $true)
            if ($null -eq $function) { throw 'HA gate step function was not found' }
            . ([scriptblock]::Create($function.Extent.Text))
            Invoke-Step -Name fixture -Action { & pwsh -NoProfile -NonInteractive -Command 'exit {{childExitCode}}' }
            """);
        Assert.Equal(expected, result.ExitCode == 0);
        if (!expected)
            Assert.Contains("failed with exit code 7", result.Output);
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("release/v0.19.0", true)]
    [InlineData("feature/maintenance", false)]
    public async Task ReleaseAncestry_AcceptsOnlySupportedRemoteBranches(string branch, bool accepted)
    {
        var repository = Path.Combine(Path.GetTempPath(), $"etlsql_release_policy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(repository);
        try
        {
            var result = await RunHelper($$"""
                git init --quiet
                if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
                git -c user.name=ReleasePolicyTests -c user.email=release-policy@example.invalid commit --quiet --allow-empty -m foundation
                if ($LASTEXITCODE -ne 0) { throw 'foundation commit failed' }
                git update-ref refs/remotes/origin/main HEAD
                git -c user.name=ReleasePolicyTests -c user.email=release-policy@example.invalid commit --quiet --allow-empty -m maintenance
                if ($LASTEXITCODE -ne 0) { throw 'maintenance commit failed' }
                git tag v0.19.1
                git update-ref refs/remotes/origin/{{branch}} HEAD
                Assert-ReleaseTagReachable -Tag v0.19.1
                """, repository);

            Assert.Equal(accepted, result.ExitCode == 0);
            Assert.Contains(accepted ? $"refs/remotes/origin/{branch}" : "must be reachable", result.Output);
        }
        finally
        {
            // Git object files are read-only on Windows.
            foreach (var file in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(repository, recursive: true);
        }
    }

    internal static async Task<(int ExitCode, string Output)> RunHelper(string expression, string? workingDirectory = null, string helperName = "Release.Helpers.ps1")
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ETL-SQL.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var helper = Path.Combine(directory!.FullName, "scripts", helperName).Replace("'", "''");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = workingDirectory ?? directory.FullName,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", $"$ErrorActionPreference = 'Stop'; . '{helper}'; {expression}" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (workingDirectory is not null)
        {
            // Temporary fixture repositories must not inherit the developer's signing keys or
            // global ignore paths. Production commits still use the repository's normal policy.
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(workingDirectory, ".git-fixture-global");
            startInfo.Environment["GIT_CONFIG_COUNT"] = "1";
            startInfo.Environment["GIT_CONFIG_KEY_0"] = "core.excludesFile";
            startInfo.Environment["GIT_CONFIG_VALUE_0"] = Path.Combine(workingDirectory, ".git", "info", "exclude");
        }
        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output + await error);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
    }
}
