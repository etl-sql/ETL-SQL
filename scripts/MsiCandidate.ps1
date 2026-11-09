# Records and verifies the exact installers used by isolated certification jobs: the candidate, and
# the previous release it upgrades from. The first public release has no previous release and is
# certified as a fresh install, so its record holds the candidate alone.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Create', 'Verify', 'VerifyPublished')] [string]$Mode,
    [Parameter(Mandatory)] [string]$Directory,
    [Parameter(Mandatory)] [string]$SourceSha,
    [string]$PublishedMsi,
    [string]$WindowsPayloadHash,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $Directory 'candidate.json'
$allowed = @('previous.msi', 'current.msi')
if ($Mode -eq 'Create') {
    $names = @($allowed | Where-Object { $_ -ceq 'current.msi' -or (Test-Path -LiteralPath (Join-Path $Directory $_) -PathType Leaf) })
    $files = @($names | ForEach-Object {
        @{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $Directory $_) -Algorithm SHA256).Hash }
    })
    $candidate = @{ schemaVersion = 1; sourceSha = $SourceSha; files = $files }
    if ($WindowsPayloadHash -or $Version) {
        if ($WindowsPayloadHash -cnotmatch '^[a-f0-9]{64}$' -or $Version -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Invalid Windows payload identity.' }
        $candidate.windowsPayload = @{ sourceSha = $SourceSha; version = $Version; manifestSha256 = $WindowsPayloadHash }
    }
    $candidate |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
} else {
    if ($Mode -eq 'VerifyPublished' -and [string]::IsNullOrWhiteSpace($PublishedMsi)) {
        throw 'PublishedMsi is required when verifying a release asset.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.sourceSha -cne $SourceSha) {
        throw 'MSI candidate schema or source SHA mismatch.'
    }
    if ($manifest.PSObject.Properties.Name -contains 'windowsPayload') {
        if ($manifest.windowsPayload.sourceSha -cne $SourceSha -or $manifest.windowsPayload.version -cnotmatch '^\d+\.\d+\.\d+$' -or
            $manifest.windowsPayload.manifestSha256 -cnotmatch '^[a-f0-9]{64}$') { throw 'Invalid Windows payload identity.' }
    }
    $names = @($manifest.files | ForEach-Object { [string]$_.name })
    if ($names.Count -notin 1, 2 -or $names -cnotcontains 'current.msi' -or @($names | Where-Object { $_ -cnotin $allowed }).Count) {
        throw 'MSI candidate must record current.msi and, for an upgrade, previous.msi.'
    }
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
