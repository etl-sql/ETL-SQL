[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Build the XT08 bundle with PowerShell 7 on Windows.' }

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::Exists($outputPath)) { throw 'Choose a new output directory for the qualification bundle.' }
$output = [IO.Directory]::CreateDirectory($outputPath)
$bundle = [IO.Directory]::CreateDirectory((Join-Path $output.FullName 'bundle'))
$bin = [IO.Directory]::CreateDirectory((Join-Path $bundle.FullName 'bin'))
$null = [IO.Directory]::CreateDirectory((Join-Path $bundle.FullName 'lib'))
$runtime = 'win-' + [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()

$sourceFiles = @(
    'Invoke-BundledWindowsToolArtifactQualification.ps1',
    'Invoke-WindowsToolArtifactQualification.ps1',
    'Invoke-WindowsToolArtifactServiceQualification.ps1',
    'Test-InstalledWindowsToolArtifactApproval.ps1',
    'Test-WindowsToolArtifactBoundary.ps1',
    'lib/windows-tool-artifact-fixture.ps1'
)
foreach ($relative in $sourceFiles) {
    [IO.File]::Copy((Join-Path $PSScriptRoot $relative), (Join-Path $bundle.FullName $relative))
}

$service = Join-Path $PSScriptRoot 'fixtures/windows-tool-artifact-service/WindowsToolArtifactServiceFixture.csproj'
$payload = Join-Path $PSScriptRoot 'fixtures/windows-tool-artifact-payload/WindowsToolArtifactPayload.csproj'
$serviceOutput = Join-Path $output.FullName 'service-runner'
$serviceExecutable = 'WindowsToolArtifactServiceFixture.exe'
dotnet publish $service -c Release -r $runtime --no-self-contained -o $serviceOutput | Out-Host
if ($LASTEXITCODE) { throw 'Failed to publish the XT08 service runner.' }
[IO.File]::Copy((Join-Path $serviceOutput $serviceExecutable), (Join-Path $bin.FullName $serviceExecutable))
foreach ($generation in 1, 2) {
    $publishDirectory = Join-Path $output.FullName "payload-$generation"
    $executable = "WindowsToolArtifactPayload-$generation.exe"
    dotnet publish $payload -c Release -r $runtime --no-self-contained -p:FixtureGeneration=$generation -o $publishDirectory | Out-Host
    if ($LASTEXITCODE) { throw "Failed to publish XT08 payload generation $generation." }
    [IO.File]::Copy((Join-Path $publishDirectory $executable), (Join-Path $bin.FullName $executable))
}

$files = [ordered]@{}
foreach ($relative in ($sourceFiles + @(
    'bin/WindowsToolArtifactServiceFixture.exe',
    'bin/WindowsToolArtifactPayload-1.exe',
    'bin/WindowsToolArtifactPayload-2.exe'
))) {
    $files[$relative] = (Get-FileHash -LiteralPath (Join-Path $bundle.FullName $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{
    Contract = 'etlsql-xt08-windows-qualification-bundle-v1'
    Runtime = $runtime
    Files = $files
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $bundle.FullName 'manifest.json') -Encoding utf8NoBOM

$archive = Join-Path $output.FullName "xt08-windows-qualification-$runtime.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($bundle.FullName, $archive)
[pscustomobject]@{
    Archive = $archive
    Sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Runtime = $runtime
}
