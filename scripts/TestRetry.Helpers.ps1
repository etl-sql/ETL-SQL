# Retry-once for test lanes. A test that fails and then passes when rerun alone, in the same job and
# on the same build, is a flake: it is recorded (flaky-tests.json and a job warning) but does not
# fail the lane. A test that fails twice, more than $MaxRetriedMethods failing methods, or a run
# that failed without any failed test result (crash, timeout, discovery) stays a failure.

$script:MaxRetriedMethods = 3

# Failed methods of a TRX as Namespace.Class.Method (theory cases collapse to their method).
function Get-FailedTestMethods {
    param([Parameter(Mandatory)] [string]$TrxPath)
    [xml]$trx = Get-Content -LiteralPath $TrxPath -Raw
    $ns = [Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $definitions = @{}
    foreach ($test in $trx.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns)) {
        $method = $test.SelectSingleNode('t:TestMethod', $ns)
        $definitions[$test.id] = "$($method.className.Split(',')[0]).$($method.name)"
    }
    @($trx.SelectNodes('//t:UnitTestResult', $ns) |
        Where-Object { $_.outcome -ceq 'Failed' } |
        ForEach-Object { $definitions[$_.testId] } |
        Where-Object { $_ } | Sort-Object -Unique)
}

# Outcome of every result per method: @{ method = @('Passed', 'Failed', ...) }.
function Get-TestOutcomesByMethod {
    param([Parameter(Mandatory)] [string]$TrxPath)
    [xml]$trx = Get-Content -LiteralPath $TrxPath -Raw
    $ns = [Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $definitions = @{}
    foreach ($test in $trx.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns)) {
        $method = $test.SelectSingleNode('t:TestMethod', $ns)
        $definitions[$test.id] = "$($method.className.Split(',')[0]).$($method.name)"
    }
    $outcomes = @{}
    foreach ($result in $trx.SelectNodes('//t:UnitTestResult', $ns)) {
        $method = $definitions[$result.testId]
        if (-not $method) { continue }
        if (-not $outcomes.ContainsKey($method)) { $outcomes[$method] = [Collections.Generic.List[string]]::new() }
        $outcomes[$method].Add([string]$result.outcome)
    }
    $outcomes
}

# Reruns the failed methods of $TrxPath once. Returns @{ Cleared = bool; Methods = [...]; RetryTrx = path;
# Reason = text }. Cleared is true only when every failed method passed in every case on the rerun.
function Invoke-FailedTestRetry {
    param(
        [Parameter(Mandatory)] [string]$TrxPath,
        [Parameter(Mandatory)] [string]$Project,
        [Parameter(Mandatory)] [string]$Configuration,
        [Parameter(Mandatory)] [string]$ResultsDirectory,
        [Parameter(Mandatory)] [string]$Label,
        [int]$TimeoutSeconds = 900,
        # Tests substitute a fake dotnet; lanes always use the real one.
        [string]$Runner = 'dotnet',
        [string[]]$RunnerArgs = @()
    )
    $methods = @(Get-FailedTestMethods -TrxPath $TrxPath)
    $outcome = [ordered]@{ Cleared = $false; Methods = $methods; RetryTrx = $null; Reason = '' }
    if ($methods.Count -eq 0) { $outcome.Reason = 'no failed test result to retry'; return [pscustomobject]$outcome }
    if ($methods.Count -gt $script:MaxRetriedMethods) {
        $outcome.Reason = "$($methods.Count) failed methods exceed the retry limit of $script:MaxRetriedMethods"
        return [pscustomobject]$outcome
    }
    $retryName = "retry-$([IO.Path]::GetFileNameWithoutExtension($TrxPath)).trx"
    $filter = ($methods | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
    Write-Host "${Label}: retrying $($methods.Count) failed method(s) once: $($methods -join ', ')"
    # Minimal, like the first run: 'normal' echoed every line a test printed, and the SLT corpus
    # retry printed 428k lines until its step deadline killed it (CI 42705343857).
    $info = [Diagnostics.ProcessStartInfo]::new($Runner)
    foreach ($a in @($RunnerArgs) + @('test', $Project, '--configuration', $Configuration, '--no-build', '--no-restore',
            '--filter', $filter, '--results-directory', $ResultsDirectory,
            '--logger', "trx;LogFileName=$retryName", '--logger', 'console;verbosity=minimal')) { $info.ArgumentList.Add($a) }
    $info.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { }
        $outcome.Reason = "retry timed out after $TimeoutSeconds s"
        return [pscustomobject]$outcome
    }
    $retryTrx = Join-Path $ResultsDirectory $retryName
    $outcome.RetryTrx = $retryTrx
    if (-not (Test-Path -LiteralPath $retryTrx)) { $outcome.Reason = 'retry wrote no TRX'; return [pscustomobject]$outcome }
    $retried = Get-TestOutcomesByMethod -TrxPath $retryTrx
    $stillFailing = @($methods | Where-Object {
            -not $retried.ContainsKey($_) -or @($retried[$_] | Where-Object { $_ -cne 'Passed' }).Count -gt 0
        })
    if ($process.ExitCode -ne 0 -or $stillFailing.Count -gt 0) {
        $outcome.Reason = "failed again on retry: $($stillFailing -join ', ')"
        return [pscustomobject]$outcome
    }
    $outcome.Cleared = $true
    Write-FlakyTestRecord -ResultsDirectory $ResultsDirectory -Label $Label -Methods $methods -TrxPath $TrxPath -RetryTrx $retryTrx
    [pscustomobject]$outcome
}

# Flakes stay visible: one JSON line per flake for collection, and a job warning annotation.
function Write-FlakyTestRecord {
    param([string]$ResultsDirectory, [string]$Label, [string[]]$Methods, [string]$TrxPath, [string]$RetryTrx)
    $record = [ordered]@{
        schemaVersion = 1; label = $Label; methods = $Methods
        sha = $env:GITHUB_SHA; runId = $env:GITHUB_RUN_ID; runAttempt = $env:GITHUB_RUN_ATTEMPT; job = $env:GITHUB_JOB
        firstTrx = [IO.Path]::GetFileName($TrxPath); retryTrx = [IO.Path]::GetFileName($RetryTrx)
        utc = [DateTime]::UtcNow.ToString('o')
    }
    Add-Content -LiteralPath (Join-Path $ResultsDirectory 'flaky-tests.jsonl') -Value ($record | ConvertTo-Json -Compress -Depth 4) -Encoding utf8NoBOM
    foreach ($method in $Methods) {
        Write-Host "::warning title=Flaky test (passed on retry)::$method failed, then passed when rerun alone ($Label)."
    }
}
