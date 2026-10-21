# Records and verifies the exact installer pair used by isolated certification jobs.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Create', 'Verify', 'VerifyPublished')] [string]$Mode,
    [Parameter(Mandatory)] [string]$Directory,
    [Parameter(Mandatory)] [string]$SourceSha,
    [string]$PublishedMsi
)

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $Directory 'candidate.json'
$names = @('previous.msi', 'current.msi')
if ($Mode -eq 'Create') {
    $files = @($names | ForEach-Object {
        @{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $Directory $_) -Algorithm SHA256).Hash }
    })
    @{ schemaVersion = 1; sourceSha = $SourceSha; files = $files } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
} else {
    if ($Mode -eq 'VerifyPublished' -and [string]::IsNullOrWhiteSpace($PublishedMsi)) {
        throw 'PublishedMsi is required when verifying a release asset.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.sourceSha -cne $SourceSha) {
        throw 'MSI candidate schema or source SHA mismatch.'
    }
    if (@($manifest.files).Count -ne 2) { throw 'MSI candidate must contain exactly two installer records.' }
    foreach ($name in $names) {
        $entries = @($manifest.files | Where-Object { $_.name -ceq $name })
        if ($entries.Count -ne 1) { throw "Missing or duplicate MSI record: $name" }
        if ($entries[0].sha256 -cnotmatch '^[A-F0-9]{64}$') { throw "Invalid MSI hash: $name" }
        if ($Mode -eq 'VerifyPublished' -and $name -eq 'previous.msi') { continue }
        $filePath = if ($Mode -eq 'VerifyPublished') { $PublishedMsi } else { Join-Path $Directory $name }
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "MSI candidate file does not exist: $name"
        }
        $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
        if ($hash -cne $entries[0].sha256) { throw "MSI candidate hash mismatch: $name" }
    }
}
