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
                $phase = @{status='{{status}}'; command='same command'; log=$log; completedAt=[datetimeoffset]::UtcNow.ToString('o'); note='Skipped because prerequisite phase failed'}
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
    [InlineData(-1, true)]
    [InlineData(-8, false)]
    [InlineData(1, false)]
    public async Task Resume_RequiresRecentOriginalCompletion(int ageDays, bool expected)
    {
        var result = await RunHelper($$"""
            $log = [IO.Path]::GetTempFileName()
            try {
                Set-Content -LiteralPath $log -Value 'Executed phase output'
                $phase = @{status='Passed'; command='same'; log=$log; completedAt=[datetimeoffset]::UtcNow.AddDays({{ageDays}}).ToString('o')}
                Test-PreReleasePhaseReusable -PreviousPhase $phase -Command 'same'
            } finally { Remove-Item -LiteralPath $log -Force }
            """);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(expected.ToString(), result.Output.Trim());
    }

    private const string PhaseRunnerFixture = """
        $source = Join-Path (Split-Path -Parent (Get-Command Test-PreReleasePhaseReusable).ScriptBlock.File) 'Test-PreRelease.ps1'
        $tokens = $null; $parseErrors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count) { throw ($parseErrors -join '; ') }
        foreach ($name in @('Get-PreReleasePhaseDependencies', 'Get-FailedPreReleasePrerequisites', 'Invoke-LoggedPhase')) {
            $definition = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
            if ($definition.Count -ne 1) { throw "Missing phase function: $name" }
            . ([scriptblock]::Create($definition[0].Extent.Text))
        }
        function Save-State { }
        function Start-PhaseWatchdog { return $null }
        function Stop-PhaseWatchdog { return $null }
        $temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
        $RunDir = [IO.Path]::GetFullPath((Join-Path $temporaryBase ('etlsql-release-phase-' + [guid]::NewGuid().ToString('N'))))
        function Remove-PhaseFixture {
            if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($RunDir)) -ne $temporaryBase) { throw 'Fixture cleanup escaped the temporary directory' }
            Remove-Item -LiteralPath $RunDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $RunDir | Out-Null
        $RepoRoot = $RunDir
        $Resume = $true
        $fingerprint = 'fixture-source'
        $results = [Collections.Generic.List[object]]::new()
        $previousLog = Join-Path $RunDir 'previous.log'
        Set-Content -LiteralPath $previousLog -Value 'Earlier successful execution'
        $marker = Join-Path $RunDir 'executed.txt'
        """;

    [Theory]
    [InlineData("Asset preparation")]
    [InlineData("Asset drift check")]
    [InlineData("Asset ownership check")]
    public async Task Resume_ExecutesAssetPhasesDespitePreviousSuccess(string phase)
    {
        var result = await RunHelper(PhaseRunnerFixture + $$"""

            try {
                function node { Set-Content -LiteralPath $marker -Value 'Executed'; $global:LASTEXITCODE = 0 }
                $command = $ast.FindAll({ param($node)
                    $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-LoggedPhase' -and
                    $node.CommandElements[1].Value -eq '{{phase}}'
                }, $true)
                if ($command.Count -ne 1) { throw 'Missing asset phase invocation' }
                $previousPhaseMap = @{ '{{phase}}' = @{status='Passed'; command=$command[0].CommandElements[2].Value; log=$previousLog; artifacts=@()} }
                . ([scriptblock]::Create($command[0].Extent.Text))
                'RESULT|' + (Test-Path -LiteralPath $marker) + '|' + $results[0].status
            } finally { Remove-PhaseFixture }
            """);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RESULT|True|Passed", result.Output);
    }

    [Theory]
    [InlineData("Dotnet build", "Asset preparation")]
    [InlineData("Format verify", "Asset drift check")]
    [InlineData("Asset ownership check", "Asset drift check")]
    [InlineData("VS Code compile", "Asset drift check")]
    [InlineData("VS Code VSIX package", "VS Code UI build")]
    public async Task Resume_FailedPrerequisiteBlocksPreviouslyPassedConsumer(string phase, string prerequisite)
    {
        var result = await RunHelper(PhaseRunnerFixture + $$"""

            try {
                $results.Add(@{name='{{prerequisite}}'; status='Failed'; note='Fixture failure'})
                $previousPhaseMap = @{ '{{phase}}' = @{status='Passed'; command='consumer'; log=$previousLog; artifacts=@()} }
                Invoke-LoggedPhase -Name '{{phase}}' -Command 'consumer' -Action { Set-Content -LiteralPath $marker -Value 'Unexpected' } `
                    -PreviousPhaseMap $previousPhaseMap -Fingerprint $fingerprint -Results $results
                'RESULT|' + (Test-Path -LiteralPath $marker) + '|' + $results[1].status + '|' + $results[1].note
            } finally { Remove-PhaseFixture }
            """);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("RESULT|False|Skipped|Skipped because prerequisite", result.Output);
        Assert.Contains(prerequisite, result.Output);
    }

    [Fact]
    public async Task PrePush_PreparesBeforeInvokingDotnetFormat()
    {
        var result = await RunHelper("""
            function node { Write-Output 'TRACE|prepare'; $global:LASTEXITCODE = 0 }
            function Get-Content { '{"lanes":["source-preflight","build-and-test"]}' }
            function dotnet { Write-Output 'TRACE|format'; throw 'Stop at the formatting boundary' }
            $script = Join-Path (Split-Path -Parent (Get-Command Test-PreReleasePhaseReusable).ScriptBlock.File) 'Test-PrePush.ps1'
            & $script
            """);
        Assert.NotEqual(0, result.ExitCode);
        var prepare = result.Output.IndexOf("TRACE|prepare", StringComparison.Ordinal);
        var format = result.Output.IndexOf("TRACE|format", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && format > prepare, result.Output);
    }

    [Fact]
    public async Task PrePush_StopsBeforeFormatWhenPreparationFails()
    {
        var result = await RunHelper("""
            function node {
                Write-Output 'TRACE|prepare'
                $global:LASTEXITCODE = if ($args[0].EndsWith('sync-assets.js')) { 7 } else { 0 }
            }
            function Get-Content { '{"lanes":["source-preflight","build-and-test"]}' }
            function dotnet { Write-Output 'TRACE|unexpected-format' }
            $script = Join-Path (Split-Path -Parent (Get-Command Test-PreReleasePhaseReusable).ScriptBlock.File) 'Test-PrePush.ps1'
            & $script
            """);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Browser asset preparation failed", result.Output);
        Assert.DoesNotContain("TRACE|unexpected-format", result.Output);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fingerprint_ChangesWhenAlreadyDirtyFilesChangeAgain(bool bashFingerprint)
    {
        var repository = Path.Combine(Path.GetTempPath(), $"etlsql_fingerprint_{Guid.NewGuid():N}");
        Directory.CreateDirectory(repository);
        try
        {
            var fingerprint = bashFingerprint
                ? "& node (Join-Path (Split-Path -Parent (Get-Command Test-PreReleasePhaseReusable).ScriptBlock.File) 'source-fingerprint.mjs') $PWD.Path"
                : "Get-ReleaseSourceFingerprint -RepoRoot $PWD.Path";
            var result = await RunHelper($$"""
                git init --quiet
                Set-Content -LiteralPath tracked.txt -Value original
                git add tracked.txt
                git -c user.name=ReleasePolicyTests -c user.email=release-policy@example.invalid commit --quiet -m foundation
                if ($LASTEXITCODE -ne 0) { throw 'fixture commit failed' }
                Set-Content -LiteralPath tracked.txt -Value first
                $first = {{fingerprint}}
                Set-Content -LiteralPath tracked.txt -Value second
                $second = {{fingerprint}}
                if ($first -eq $second) { throw 'Tracked edits did not change the fingerprint' }
                Set-Content -LiteralPath 'untracked espace-é-$value.txt' -Value first
                $third = {{fingerprint}}
                Set-Content -LiteralPath 'untracked espace-é-$value.txt' -Value second
                $fourth = {{fingerprint}}
                if ($third -eq $fourth) { throw 'Untracked edits did not change the fingerprint' }
                Write-Output verified
                """, repository);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Equal("verified", result.Output.Trim());
        }
        finally
        {
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(repository)));
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
