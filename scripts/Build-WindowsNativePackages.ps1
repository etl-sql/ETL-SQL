$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props')
$version = [string]$props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
$sha = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -cne $env:CANDIDATE_SHA) { throw 'Windows consumer checkout does not match its candidate.' }
$outDir = Join-Path $repo 'artifacts/native-packages/win-x64'
. (Join-Path $PSScriptRoot 'ReleaseOutput.Helpers.ps1')
$scratch = Join-Path $repo 'artifacts/windows-payload-consumers'
Assert-ReleaseOutputPath -RepoRoot $repo -Path $scratch
$null = New-Item -ItemType Directory -Force -Path $scratch
$payloadRoot = Join-Path $scratch ([guid]::NewGuid().ToString('N'))
try {
    & node (Join-Path $PSScriptRoot 'collect-windows-payload.mjs') $sha $version $payloadRoot
    if ($LASTEXITCODE -ne 0) { throw 'Shared Windows payload selection failed; no rebuild fallback is permitted.' }
    $selection = Get-Content -LiteralPath (Join-Path $payloadRoot 'selection.json') -Raw | ConvertFrom-Json
    & (Join-Path $PSScriptRoot 'Invoke-CertificationProcess.ps1') -ScriptPath (Join-Path $PSScriptRoot 'publish-release.ps1') `
        -ScriptArguments @('-Platforms', 'win-x64', '-WindowsPayloadDirectory', $selection.payloadDirectory, '-WindowsPayloadHash', $selection.manifestSha256) `
        -EvidenceDirectory (Join-Path $outDir 'package-process') -TimeoutSeconds 780
    if ($LASTEXITCODE -ne 0) { throw 'Windows ZIP/VSIX packaging failed.' }
    $summaryPath = Join-Path $outDir 'certification.json'
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    # Keep only the immutable origin, not a runner-local extraction path, in certification metadata.
    $selection.PSObject.Properties.Remove('payloadDirectory')
    $summary | Add-Member -NotePropertyName windowsPayload -NotePropertyValue $selection -Force
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath
    $selection | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outDir 'windows-payload-origin.json')
} finally {
    # This script owns only this GUID directory. Original producer artifacts remain retained.
    Assert-ReleaseOutputPath -RepoRoot $repo -Path $payloadRoot
    if ([IO.Path]::GetDirectoryName($payloadRoot) -cne $scratch -or [IO.Path]::GetFileName($payloadRoot) -cnotmatch '^[a-f0-9]{32}$') { throw 'Unsafe Windows payload scratch cleanup.' }
    if (Test-Path -LiteralPath $payloadRoot) { Remove-Item -LiteralPath $payloadRoot -Recurse -Force }
}
