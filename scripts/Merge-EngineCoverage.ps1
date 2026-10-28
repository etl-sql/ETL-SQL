[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+$')][string]$RunId,
    [ValidateRange(1, 32)][int]$GroupCount = 3,
    [ValidateRange(1, 32)][int]$ShardCount = 12,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestLane.Helpers.ps1')
$inputRoot = [IO.Path]::GetFullPath($InputDirectory)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Merged coverage output must be a new directory.' }
if ($GroupCount -gt $ShardCount) { throw 'Coverage groups cannot exceed the shard count.' }

function Resolve-EvidenceFile([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or
        ($Relative -split '[/\\]') -contains '..') { throw 'Unsafe coverage evidence path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Coverage evidence escapes its artifact.'
    }
    $item = Get-Item -LiteralPath $path
    $parent = $item
    while ($parent.FullName -ne $inputRoot) {
        if (-not $parent -or ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked coverage evidence is not accepted.' }
        $parent = if ($parent -is [IO.FileInfo]) { $parent.Directory } else { $parent.Parent }
    }
    if ($item -isnot [IO.FileInfo]) { throw 'Coverage evidence must be a regular file.' }
    return $path
}

$artifacts = @(Get-ChildItem -LiteralPath $inputRoot -Directory)
$referencePlan = $null
$methodOwners = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$testOwners = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$files = @()
$receipts = @()
$passed = 0
$skipped = 0
$skippedTests = @()
for ($group = 1; $group -le $GroupCount; $group++) {
    # Successful groups from earlier attempts stay usable. Select the latest execution of each
    # group; a broken latest artifact must fail instead of falling back to older evidence.
    $matching = @($artifacts | Where-Object Name -Match "^engine-coverage-group-$group-$RunId-([1-9][0-9]*)$" |
        Sort-Object { [int]($_.Name.Split('-')[-1]) } -Descending)
    if ($matching.Count -eq 0) { throw "Missing coverage group $group." }
    $artifact = $matching[0]
    $manifestPath = Resolve-EvidenceFile $artifact.FullName 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.commit -cne $Commit -or $manifest.runId -cne $RunId -or
        [string]$manifest.runAttempt -cne $artifact.Name.Split('-')[-1] -or
        $manifest.shardGroup -ne $group -or $manifest.shardGroupCount -ne $GroupCount -or $manifest.shardCount -ne $ShardCount -or
        $manifest.configuration -cne $Configuration -or $manifest.project -cne 'tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj') {
        throw "Coverage group $group has mismatched candidate, run, attempt or configuration."
    }
    $plan = @($manifest.shards | ForEach-Object {
            [pscustomobject][ordered]@{ number = $_.number; group = $_.group; expectedTests = $_.expectedTests; classes = $_.classes;
                methods = $_.methods; expectedMethodCounts = $_.expectedMethodCounts; results = $_.results }
        })
    $identity = [ordered]@{ filter = $manifest.filter; discoveredTests = $manifest.discoveredTests; discoveredClasses = $manifest.discoveredClasses; shards = $plan } |
        ConvertTo-Json -Depth 10 -Compress
    if ($null -eq $referencePlan) {
        $referencePlan = $identity
        if ($plan.Count -ne $ShardCount -or $manifest.discoveredTests -le 0) { throw 'Incomplete engine coverage plan.' }
        for ($index = 0; $index -lt $ShardCount; $index++) {
            $shard = $plan[$index]
            if ($shard.number -ne $index + 1 -or $shard.group -ne [int][Math]::Floor($index * $GroupCount / $ShardCount) + 1 -or
                @($shard.methods).Count -eq 0 -or @($shard.expectedMethodCounts).Count -ne @($shard.methods).Count -or
                ($shard.expectedMethodCounts | Measure-Object count -Sum).Sum -ne $shard.expectedTests) { throw 'Invalid engine shard plan.' }
            foreach ($expected in $shard.expectedMethodCounts) {
                if ($expected.count -lt 1 -or $expected.method -cnotin $shard.methods -or -not $methodOwners.Add($expected.method)) {
                    throw 'Missing or overlapping engine method identities.'
                }
            }
        }
        if (($plan | Measure-Object expectedTests -Sum).Sum -ne $manifest.discoveredTests) { throw 'Incomplete engine discovery count.' }
    } elseif ($identity -cne $referencePlan) { throw 'Engine coverage groups disagree on the test inventory.' }

    foreach ($shard in @($manifest.shards | Where-Object group -EQ $group)) {
        $trx = Resolve-EvidenceFile $artifact.FullName $shard.results
        $verification = Read-ShardTestResults $trx $shard.expectedMethodCounts
        if ($verification.missingMethods.Count -gt 0 -or $verification.unexpectedMethods.Count -gt 0 -or
            @($verification.results | Where-Object outcome -NotIn @('Passed', 'NotExecuted')).Count -gt 0) {
            throw "Engine shard $($shard.number) has failed, incomplete or unexpected test results."
        }
        foreach ($testId in @($verification.results | ForEach-Object testId | Sort-Object -Unique)) {
            if (-not $testOwners.Add($testId)) { throw 'Overlapping engine test results.' }
        }
        $passed += @($verification.results | Where-Object outcome -EQ 'Passed').Count
        $skipped += @($verification.results | Where-Object outcome -EQ 'NotExecuted').Count
        $skippedTests += @($verification.results | Where-Object outcome -EQ 'NotExecuted' | ForEach-Object {
                @{ shard = $shard.number; testId = $_.testId; testName = $_.testName }
            })
        if (@($shard.coverageFiles).Count -ne 1) { throw 'Missing or ambiguous engine coverage attachment.' }
        $coverage = $shard.coverageFiles[0]
        $path = Resolve-EvidenceFile $artifact.FullName $coverage.path
        $attachments = @(Get-ShardCoverageFiles -TrxPath $trx)
        if ($coverage.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $attachments.Count -ne 1 -or
            $attachments[0].path -cne $coverage.path -or $attachments[0].sha256 -cne $coverage.sha256) {
            throw 'Engine coverage does not match its TRX attachment.'
        }
        # These files are roughly 80 MB each. A streaming validation avoids retaining twelve
        # large DOMs on the coverage worker before ReportGenerator starts its own merge.
        $reader = [Xml.XmlReader]::Create($path)
        try {
            [void]$reader.MoveToContent()
            $validLines = 0L
            if ($reader.Name -cne 'coverage' -or -not [long]::TryParse($reader.GetAttribute('lines-valid'), [ref]$validLines) -or $validLines -le 0) {
                throw 'Empty engine coverage attachment.'
            }
            while ($reader.Read()) { }
        } finally { $reader.Dispose() }
        $files += @{ source = $path; target = "shard-$($shard.number)/coverage.cobertura.xml" }
    }
    $receipts += @{ group = $group; artifact = $artifact.Name; manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() }
}
if ($files.Count -ne $ShardCount -or $passed -le 0) { throw 'Incomplete merged engine coverage.' }

# Validate every group before creating report input. The downstream canonical coverage gate
# still owns assembly exclusions, report generation and the unchanged 70% requirement.
New-Item -ItemType Directory -Path $outputRoot | Out-Null
foreach ($file in $files) {
    $target = Join-Path $outputRoot $file.target
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.source -Destination $target
}
@{ schemaVersion = 1; commit = $Commit; runId = $RunId; groups = $receipts; shards = $files.Count; passed = $passed; skipped = $skipped; skippedTests = $skippedTests } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot 'merged-inventory.json') -Encoding utf8NoBOM
Write-Host "Verified $ShardCount engine shards across $GroupCount groups: $passed passed, $skipped existing skips."
