function ConvertTo-DeploymentEvidenceTime {
    param([object]$Value)
    if ($Value -is [DateTimeOffset]) { return $Value }
    if ($Value -is [DateTime]) { return [DateTimeOffset]$Value }
    $parsed = [DateTimeOffset]::MinValue
    if ($Value -isnot [string] -or -not [DateTimeOffset]::TryParse($Value, [ref]$parsed)) { throw 'Invalid evidence timestamp.' }
    return $parsed
}

function Get-DeploymentTestEvidence {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Filter, [object]$StartedUtc, [object]$CompletedUtc)

    try {
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 16777216
        $reader = [Xml.XmlReader]::Create($Path, $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
        } finally { $reader.Dispose() }
        if ($document.DocumentElement.LocalName -cne 'TestRun' -or
            $document.DocumentElement.NamespaceURI -cne 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010') { throw 'Invalid TRX root.' }
        if ($StartedUtc -or $CompletedUtc) {
            $times = $document.SelectSingleNode('/*[local-name()="TestRun"]/*[local-name()="Times"]')
            $start = [DateTimeOffset]::MinValue; $end = $start; $phaseStart = $start; $phaseEnd = $start
            $phaseStart = ConvertTo-DeploymentEvidenceTime $StartedUtc
            $phaseEnd = ConvertTo-DeploymentEvidenceTime $CompletedUtc
            if (-not [DateTimeOffset]::TryParse($times.GetAttribute('start'), [ref]$start) -or
                -not [DateTimeOffset]::TryParse($times.GetAttribute('finish'), [ref]$end) -or
                $end -lt $start -or $start -lt $phaseStart -or $end -gt $phaseEnd) { throw 'TRX is outside its recorded phase execution.' }
        }
        $counters = $document.SelectSingleNode('/*[local-name()="TestRun"]/*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
        $total = 0L; $executed = 0L; $passed = 0L
        if (-not [long]::TryParse($counters.GetAttribute('total'), [ref]$total) -or
            -not [long]::TryParse($counters.GetAttribute('executed'), [ref]$executed) -or
            -not [long]::TryParse($counters.GetAttribute('passed'), [ref]$passed) -or
            $total -le 0 -or $executed -ne $total -or $passed -ne $total) { throw 'Incomplete test counters.' }
        foreach ($name in @('failed', 'error', 'timeout', 'aborted', 'inconclusive', 'notRunnable', 'notExecuted', 'disconnected')) {
            if ($counters.HasAttribute($name) -and $counters.GetAttribute($name) -cne '0') { throw 'Unsuccessful test counters.' }
        }
        $definitions = @($document.SelectNodes('/*[local-name()="TestRun"]/*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]'))
        $results = @($document.SelectNodes('/*[local-name()="TestRun"]/*[local-name()="Results"]/*[local-name()="UnitTestResult"]'))
        if ($definitions.Count -ne $total -or $results.Count -ne $total) { throw 'Incomplete test records.' }
        $methods = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        foreach ($definition in $definitions) {
            $id = $definition.GetAttribute('id')
            $method = $definition.SelectSingleNode('*[local-name()="TestMethod"]')
            $className = $method.GetAttribute('className'); $name = $method.GetAttribute('name')
            if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($className) -or
                [string]::IsNullOrWhiteSpace($name) -or -not $methods.TryAdd($id, "$className.$name")) { throw 'Invalid test definitions.' }
        }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($result in $results) {
            $id = $result.GetAttribute('testId')
            if ($result.GetAttribute('outcome') -cne 'Passed' -or -not $methods.ContainsKey($id) -or -not $seen.Add($id)) { throw 'Invalid test outcomes.' }
        }
        $matched = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($clause in ($Filter -split '\|')) {
            if ($clause -cnotmatch '^FullyQualifiedName([~=])(.+)$') { throw 'Unsupported test filter.' }
            $operation = $Matches[1]; $selector = $Matches[2]
            $hits = @($methods.Values | Where-Object {
                if ($operation -eq '=') { $_.Equals($selector, [StringComparison]::OrdinalIgnoreCase) }
                else { $_.Contains($selector, [StringComparison]::OrdinalIgnoreCase) }
            })
            if ($hits.Count -eq 0) { throw 'A planned selector did not execute.' }
            foreach ($hit in $hits) { $null = $matched.Add($hit) }
        }
        if (@($methods.Values | Where-Object { -not $matched.Contains($_) }).Count -gt 0) { throw 'Unexpected tests executed.' }
        return [ordered]@{ path = $null; sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant(); total = $total; passed = $passed }
    } catch { throw 'TRX must contain only passed, uniquely identified tests covering every planned selector.' }
}
