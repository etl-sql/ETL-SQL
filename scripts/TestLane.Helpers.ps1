function Read-ShardTestResults {
    param([string]$TrxPath, [object[]]$ExpectedMethodCounts)
    [xml]$trx = Get-Content -LiteralPath $TrxPath -Raw
    $namespace = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $namespace.AddNamespace('trx', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $results = @($trx.SelectNodes('//trx:UnitTestResult', $namespace))
    $identities = @{}
    foreach ($definition in $trx.SelectNodes('//trx:TestDefinitions/trx:UnitTest', $namespace)) {
        $identities[$definition.id] = "$($definition.TestMethod.className).$($definition.TestMethod.name)"
    }
    $actualCounts = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    $unexpectedMethods = [System.Collections.Generic.List[string]]::new()
    foreach ($result in $results) {
        $method = $identities[$result.testId]
        if (-not $method -or $method -cnotin @($ExpectedMethodCounts.method)) {
            $unexpectedMethods.Add("$($result.testId):$method")
            continue
        }
        if (-not $actualCounts.ContainsKey($method)) { $actualCounts[$method] = 0 }
        $actualCounts[$method]++
    }
    $missingMethods = @($ExpectedMethodCounts | Where-Object {
            -not $actualCounts.ContainsKey($_.method) -or $actualCounts[$_.method] -lt $_.count
        })
    return [pscustomobject]@{ results = $results; missingMethods = $missingMethods; unexpectedMethods = @($unexpectedMethods) }
}

function Get-ShardCoverageFiles {
    param([string]$TrxPath)
    [xml]$run = Get-Content -LiteralPath $TrxPath -Raw
    $root = [IO.Path]::GetFullPath((Split-Path -Parent $TrxPath))
    $deployment = [string]$run.TestRun.TestSettings.Deployment.runDeploymentRoot
    foreach ($attachment in $run.TestRun.ResultSummary.CollectorDataEntries.Collector.UriAttachments.UriAttachment) {
        $uri = [string]$attachment.A.href
        if (-not $uri.EndsWith('coverage.cobertura.xml', [StringComparison]::Ordinal)) { continue }
        # The TRX logger copies attachments into <deployment>/In/<agent>/, independently of
        # Coverlet's original GUID directory. Resolve the recorded attachment, not a directory scan.
        foreach ($part in @($deployment, $uri)) {
            if ([string]::IsNullOrWhiteSpace($part) -or [IO.Path]::IsPathRooted($part) -or $part.Contains(':') -or
                ($part -split '[/\\]') -contains '..') { throw 'Unsafe coverage attachment path.' }
        }
        $relative = ($deployment + '/In/' + $uri).Replace('\', '/')
        $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Coverage attachment escapes its evidence directory.'
        }
        [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
