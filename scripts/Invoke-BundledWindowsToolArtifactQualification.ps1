[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $FixtureParent,
    [switch] $MechanicsOnly
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run the XT08 bundle with PowerShell 7 on Windows.' }

$contract = 'etlsql-xt08-windows-qualification-bundle-v1'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
$expectedFiles = @(
    'Invoke-BundledWindowsToolArtifactQualification.ps1',
    'Invoke-WindowsToolArtifactQualification.ps1',
    'Invoke-WindowsToolArtifactServiceQualification.ps1',
    'Test-WindowsToolArtifactBoundary.ps1',
    'lib/windows-tool-artifact-fixture.ps1',
    'bin/WindowsToolArtifactServiceFixture.exe',
    'bin/WindowsToolArtifactPayload-1.exe',
    'bin/WindowsToolArtifactPayload-2.exe'
)
$runtime = 'win-' + [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if ($manifest.Contract -cne $contract -or $manifest.Runtime -cne $runtime -or $manifest.Files.Count -ne $expectedFiles.Count) {
    throw 'The XT08 manifest does not match this bundle or Windows architecture.'
}
foreach ($relative in $expectedFiles) {
    $path = Join-Path $PSScriptRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $manifest.Files[$relative]) {
        throw "Bundle verification failed for '$relative'."
    }
}

$prepare = @{
    FixtureParent = $FixtureParent
    FirstExecutable = Join-Path $PSScriptRoot 'bin/WindowsToolArtifactPayload-1.exe'
    FirstSha256 = $manifest.Files['bin/WindowsToolArtifactPayload-1.exe']
    FirstMarker = 'XT08 Windows launch fixture generation 1'
    SecondExecutable = Join-Path $PSScriptRoot 'bin/WindowsToolArtifactPayload-2.exe'
    SecondSha256 = $manifest.Files['bin/WindowsToolArtifactPayload-2.exe']
    SecondMarker = 'XT08 Windows launch fixture generation 2'
}
if (-not $MechanicsOnly) {
    $serviceRunner = Join-Path $PSScriptRoot 'bin/WindowsToolArtifactServiceFixture.exe'
    & (Join-Path $PSScriptRoot 'Invoke-WindowsToolArtifactServiceQualification.ps1') @prepare -ServiceRunner $serviceRunner
    return
}

$probe = Join-Path $PSScriptRoot 'Invoke-WindowsToolArtifactQualification.ps1'
$fixture = & $probe @prepare -MechanicsOnly
$receipts = foreach ($generation in 1, 2, 1) {
    if ($fixture.ActiveGeneration -ne $generation) {
        $fixture = & $probe -FixtureRoot $fixture.FixtureRoot -Generation $generation -MechanicsOnly
    }

    $receipt = & $probe -FixtureRoot $fixture.FixtureRoot -MechanicsOnly
    $expectedSha = if ($generation -eq 1) { $prepare.FirstSha256 } else { $prepare.SecondSha256 }
    if ($receipt.ServiceBoundaryVerified -or -not $receipt.Sha256Verified -or -not $receipt.LaunchedWithReadHandle -or
        $receipt.ActiveGeneration -ne $generation -or $receipt.FixtureSha256 -ine $expectedSha) {
        throw "Unexpected mechanics receipt for generation $generation."
    }
    $receipt
}
[pscustomobject]@{
    FixtureRoot = $fixture.FixtureRoot
    MechanicsOnly = $true
    Receipts = $receipts
}
