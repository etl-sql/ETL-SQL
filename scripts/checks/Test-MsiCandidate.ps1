# No installation or elevation: exercise the artifact boundary with disposable fixture bytes.
$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot '../MsiCandidate.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('msi-candidate-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$current = Join-Path $fixture 'current.msi'
$previous = Join-Path $fixture 'previous.msi'
$manifestPath = Join-Path $fixture 'candidate.json'
$published = Join-Path $fixture 'ETL-SQL-v1.2.3-x64-Setup.msi'
$sourceSha = 'a' * 40

function Assert-Rejected([scriptblock]$Action, [string]$Message) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        return
    }
    throw "Expected rejection: $Message"
}

try {
    Set-Content -LiteralPath $current -Value 'candidate bytes'
    Set-Content -LiteralPath $previous -Value 'previous release bytes'
    & $helper -Mode Create -Directory $fixture -SourceSha $sourceSha
    & $helper -Mode Verify -Directory $fixture -SourceSha $sourceSha
    Copy-Item -LiteralPath $current -Destination $published
    & $helper -Mode VerifyPublished -Directory $fixture -SourceSha $sourceSha -PublishedMsi $published
    Add-Content -LiteralPath $published -Value 'different published build'
    Assert-Rejected { & $helper -Mode VerifyPublished -Directory $fixture -SourceSha $sourceSha -PublishedMsi $published } 'hash mismatch'
    Copy-Item -LiteralPath $current -Destination $published -Force
    # A retry must use the same persisted manifest and bytes.
    & $helper -Mode Verify -Directory $fixture -SourceSha $sourceSha
    Assert-Rejected { & $helper -Mode Verify -Directory $fixture -SourceSha ('b' * 40) } 'source SHA mismatch'
    Add-Content -LiteralPath $current -Value 'tampering'
    Assert-Rejected { & $helper -Mode Verify -Directory $fixture -SourceSha $sourceSha } 'hash mismatch'
    Set-Content -LiteralPath $current -Value 'candidate bytes'
    $original = Get-Content -LiteralPath $manifestPath -Raw
    $manifest = $original | ConvertFrom-Json
    $manifest.files[1].name = $manifest.files[0].name
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath
    Assert-Rejected { & $helper -Mode Verify -Directory $fixture -SourceSha $sourceSha } 'MSI record'
    Set-Content -LiteralPath $manifestPath -Value $original
    Remove-Item -LiteralPath $previous
    Assert-Rejected { & $helper -Mode Verify -Directory $fixture -SourceSha $sourceSha } 'does not exist'
    & $helper -Mode VerifyPublished -Directory $fixture -SourceSha $sourceSha -PublishedMsi $published
    Assert-Rejected { & $helper -Mode VerifyPublished -Directory $fixture -SourceSha ('b' * 40) -PublishedMsi $published } 'source SHA mismatch'
    Assert-Rejected { & $helper -Mode VerifyPublished -Directory $fixture -SourceSha $sourceSha } 'PublishedMsi is required'
    Write-Host 'MSI candidate checks passed: retry, SHA, tampering, duplicate record, missing file, published bytes.'
} finally {
    foreach ($file in @($current, $previous, $manifestPath, $published)) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    Remove-Item -LiteralPath $fixture
}
