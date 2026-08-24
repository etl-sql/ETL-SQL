[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string[]]$Runtime = @('win-x64', 'linux-x64', 'osx-arm64'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\tenant-validator')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\ETL-SQL.TenantValidator\ETL-SQL.TenantValidator.csproj'
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputRoot)

foreach ($rid in $Runtime) {
    $destination = Join-Path $resolvedOutput $rid
    dotnet publish $project -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:PublishReadyToRun=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -o $destination --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tenant validator publish failed for $rid." }
}
