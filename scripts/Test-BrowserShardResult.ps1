[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ManifestPath,
    [Parameter(Mandatory)] [int]$Shard,
    [Parameter(Mandatory)] [string]$TrxPath,
    [Parameter(Mandatory)] [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$selected = @($manifest.shards | Where-Object index -EQ $Shard)
if ($manifest.schemaVersion -ne 1 -or $selected.Count -ne 1 -or $selected[0].methods.Count -eq 0) {
    throw 'Invalid shard manifest.'
}
[xml]$trx = Get-Content -LiteralPath $TrxPath -Raw
$ns = [Xml.XmlNamespaceManager]::new($trx.NameTable)
$ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$definitions = @{}
foreach ($test in $trx.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns)) {
    if ($definitions.ContainsKey($test.id)) { throw 'Duplicate test definition.' }
    $method = $test.SelectSingleNode('t:TestMethod', $ns)
    $definitions[$test.id] = "$($method.className.Split(',')[0]).$($method.name)"
}
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$results = @($trx.SelectNodes('//t:UnitTestResult', $ns))
if ($results.Count -eq 0) { throw 'No browser results.' }
foreach ($result in $results) {
    $method = $definitions[$result.testId]
    if ($result.outcome -cne 'Passed' -or $method -cnotin $selected[0].methods) {
        throw "Failed, skipped or unexpected browser result: $($result.testName)"
    }
    [void]$seen.Add($method)
}
foreach ($method in $selected[0].methods) {
    if (-not $seen.Contains($method)) { throw "Browser shard omitted method: $method" }
}
if ($results.Count -lt $selected[0].discoveredCases) { throw 'Browser shard returned fewer cases than discovery.' }
@{
    schemaVersion = 1
    inventoryHash = $manifest.inventoryHash
    shard = $Shard
    shardCount = $manifest.shardCount
    methods = @($seen | Sort-Object)
    actualCases = $results.Count
    status = 'Passed'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Browser shard $Shard result coverage verified: $($results.Count) cases."
