param([Parameter(Mandatory)][string]$RepoRoot, [ValidateSet('NoBuild', 'NoRestore', 'Build', 'FailedBuild', 'ExistingEvidence', 'CleanupEscape', 'LinkedBuild', 'ConcurrentClaim')][string]$Mode)
$ErrorActionPreference = 'Stop'
$cache = Join-Path $RepoRoot 'cache'
$out = Join-Path $RepoRoot 'out'
foreach ($part in @('engine', 'portal')) {
    $null = New-Item -ItemType Directory -Path (Join-Path $cache $part) -Force
    [IO.File]::WriteAllText((Join-Path $cache "$part/keep.txt"), "Original $part artifact")
}
if ($Mode -eq 'ExistingEvidence') {
    $null = New-Item -ItemType Directory -Path $out
    [IO.File]::WriteAllText((Join-Path $out 'enterprise-hardening-tests.log'), 'Original failed-attempt log')
    [IO.File]::WriteAllText((Join-Path $out 'enterprise-hardening-summary.json'), 'Original attempt summary')
}
if ($Mode -eq 'ConcurrentClaim') {
    $nativeNewItem = Get-Command New-Item -CommandType Cmdlet
    function New-Item {
        param([string]$ItemType, [string]$Path, [switch]$Force)
        $result = & $nativeNewItem @PSBoundParameters
        if ([IO.Path]::GetFullPath($Path) -ceq [IO.Path]::GetFullPath($out)) {
            [IO.File]::WriteAllText((Join-Path $out 'enterprise-attempt.json'), 'Competing attempt claim')
        }
        $result
    }
}
function dotnet {
    $arguments = @($args)
    $directory = $arguments[[array]::IndexOf($arguments, '--results-directory') + 1]
    $logger = $arguments[[array]::IndexOf($arguments, '--logger') + 1]
    $name = ($logger -split 'LogFileName=', 2)[1]
    Add-Content -LiteralPath (Join-Path $directory 'mock-invocations.jsonl') -Value (ConvertTo-Json -InputObject $arguments -Compress)
    if ($Mode -eq 'CleanupEscape') {
        Remove-OwnedEnterpriseBuildDirectory -Path $cache
        throw 'Cleanup escape was incorrectly accepted.'
    }
    if ($arguments -contains '--artifacts-path') {
        $build = $arguments[[array]::IndexOf($arguments, '--artifacts-path') + 1]
        $null = New-Item -ItemType Directory -Path $build -Force
        if ($Mode -eq 'LinkedBuild') {
            # This is the empty directory created for this private test attempt.
            if (-not ([IO.Path]::GetFullPath($build)).StartsWith([IO.Path]::GetFullPath($cache) + [IO.Path]::DirectorySeparatorChar)) { throw 'Fixture path escaped.' }
            Remove-Item -LiteralPath $build
            $linkKind = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
            $null = New-Item -ItemType $linkKind -Path $build -Target (Join-Path $cache 'engine')
        } else {
            [IO.File]::WriteAllText((Join-Path $build 'new-output.txt'), 'Owned test build output')
        }
    }
    '<TestRun><ResultSummary><Counters total="1" executed="1" passed="1" /></ResultSummary></TestRun>' |
        Set-Content -LiteralPath (Join-Path $directory $name)
    $global:LASTEXITCODE = if ($Mode -eq 'FailedBuild') { 7 } else { 0 }
    'Passed: 1'
}
$parameters = @{ OutDir = $out; ArtifactsPath = $cache }
if ($Mode -in @('NoBuild', 'ExistingEvidence')) { $parameters.NoBuild = $true }
if ($Mode -eq 'NoRestore') { $parameters.NoRestore = $true }
& (Join-Path $RepoRoot 'scripts/Test-EnterpriseHardeningCertification.ps1') @parameters
exit $LASTEXITCODE
