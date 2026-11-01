# Resolve each host's publish dependencies before compiling any host for this RID.
function Restore-ReleasePublishInputs {
    param(
        [Parameter(Mandatory)] [string[]]$Projects,
        [Parameter(Mandatory)] [ValidateSet('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')] [string]$RuntimeIdentifier
    )
    foreach ($project in $Projects) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        Write-Host "Restoring publish inputs for $name ($RuntimeIdentifier)..."
        & dotnet restore $project -r $RuntimeIdentifier -p:Configuration=Release -p:SelfContained=true `
            -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Publish input restore failed for $name ($RuntimeIdentifier), exit $LASTEXITCODE. No host publishing started for this platform." }
    }
}
