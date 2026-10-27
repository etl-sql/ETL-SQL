<# Source-only version consistency; no restore or build required. #>
[CmdletBinding()]
param([string]$Root = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
$findings = [System.Collections.Generic.List[string]]::new()
try {
    [xml]$props = Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
    $versions = @($props.SelectNodes('/Project/PropertyGroup/VersionPrefix'))
    if ($versions.Count -ne 1 -or $versions[0].InnerText -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'Expected exactly one literal Major.Minor.Patch VersionPrefix.'
    }
    $expected = $versions[0].InnerText
} catch {
    Write-Output "FAIL Directory.Build.props: $($_.Exception.Message)"
    exit 1
}

function Test-VersionValue([string]$Location, $Value) {
    if ($Value -cne $expected) {
        $findings.Add("${Location}: expected $expected; found '$Value'. Run scripts/Set-Version.ps1 for a complete version bump.")
    }
}

foreach ($relative in @('src/etl-sql-vscode/package.json', 'src/etl-sql-vscode/package-lock.json')) {
    try {
        $json = Get-Content -LiteralPath (Join-Path $Root $relative) -Raw | ConvertFrom-Json -AsHashtable
        Test-VersionValue "$relative version" $json.version
        if ($relative.EndsWith('package-lock.json')) {
            Test-VersionValue "$relative packages[''].version" $json.packages[''].version
        }
    } catch { $findings.Add("${relative}: $($_.Exception.Message)") }
}

try {
    $pages = @(Get-ChildItem -LiteralPath (Join-Path $Root 'src/ETL-SQL.Portal/wwwroot') -Filter '*.html' -File)
    if ($pages.Count -eq 0) { throw 'No Portal HTML pages found.' }
    foreach ($page in $pages) {
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $page.FullName -Raw), '\?v=([^\s"''<>]+)')) {
            Test-VersionValue "Portal/$($page.Name) cache version" $match.Groups[1].Value
        }
    }
} catch { $findings.Add("Portal pages: $($_.Exception.Message)") }

foreach ($finding in $findings) { Write-Output "FAIL $finding" }
if ($findings.Count -gt 0) { exit 1 }
Write-Output "Release source versions agree: $expected (.NET, VS Code manifest/lock and Portal cache URLs)."
