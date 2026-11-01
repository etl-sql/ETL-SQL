[CmdletBinding()]
param([Parameter(Mandatory)] [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ReleaseOutput.Helpers.ps1')
$output = [IO.Path]::GetFullPath($OutputDirectory)
Assert-ReleaseOutputPath -RepoRoot $repo -Path $output
if (Test-Path -LiteralPath $output) { throw 'Windows payload publication requires a new output directory.' }
$sha = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sha -cnotmatch '^[a-f0-9]{40}$') { throw 'Cannot identify Windows payload source.' }
$dirty = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Windows payload publication requires clean source.' }
[xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props')
$version = [string]$props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect .NET SDK.' }
& node (Join-Path $PSScriptRoot 'sync-assets.js')
if ($LASTEXITCODE -ne 0) { throw 'Windows payload asset preparation failed.' }
$projects = & node (Join-Path $PSScriptRoot 'windows-payload.mjs') projects
if ($LASTEXITCODE -ne 0) { throw 'Cannot read Windows host inventory.' }
$bin = Join-Path $output 'bin'
$null = New-Item -ItemType Directory -Path $bin
foreach ($hostProject in ($projects | ConvertFrom-Json)) {
    Write-Host "Publishing shared Windows host $($hostProject.executable)..."
    & dotnet publish (Join-Path $repo $hostProject.project) -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o $bin --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows host publish failed; no payload is accepted.' }
}
Copy-Item -LiteralPath (Join-Path $repo 'src/appsettings.json') -Destination (Join-Path $bin 'appsettings.json')
$after = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $after -cne $sha) { throw 'Windows payload source commit changed.' }
$dirty = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Windows payload source changed during publication.' }
$hash = & node (Join-Path $PSScriptRoot 'windows-payload.mjs') create $output $sha $version $sdk
if ($LASTEXITCODE -ne 0 -or $hash -cnotmatch '^[a-f0-9]{64}$') { throw 'Windows payload verification failed.' }
if ($env:GITHUB_OUTPUT) { "manifest_sha256=$hash" | Add-Content -LiteralPath $env:GITHUB_OUTPUT }
Write-Host "Shared Windows payload ready: $hash"
