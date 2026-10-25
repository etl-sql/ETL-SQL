<# Validate authored installer inputs without publishing applications or installing WiX. #>
[CmdletBinding()]
param([string]$Root = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
$findings = [System.Collections.Generic.List[string]]::new()
# COMPAT_BREAK: 0.22 — setup uses maintained packages; retired source helpers must stay removed.
foreach ($relative in @('scripts/install.ps1', 'scripts/install.sh',
    'src/ETL-SQL.Orchestrator.Service/install-service-windows.ps1',
    'src/ETL-SQL.Orchestrator.Service/install-service-linux.sh')) {
    if (Test-Path -LiteralPath (Join-Path $Root $relative)) {
        $findings.Add("Retired installer helper: $relative. Use the maintained MSI or Linux package installer.")
    }
}
$installer = Join-Path $Root 'src/ETL-SQL.Installer'
try {
    [xml]$manifest = Get-Content (Join-Path $installer 'Installer.wxs') -Raw
    if ($manifest.DocumentElement.LocalName -cne 'Wix' -or
        $manifest.DocumentElement.NamespaceURI -cne 'http://schemas.microsoft.com/wix/2006/wi') {
        throw 'Expected a WiX 3 manifest.'
    }
    foreach ($attribute in $manifest.SelectNodes('//@Source | //@SourceFile')) {
        $source = $attribute.Value
        # Published binaries are checked after publish; heat generates wwwroot.wxs then too.
        if ($source.StartsWith('$(var.PublishDir)\', [StringComparison]::Ordinal)) { continue }
        if ($source.Contains('$(')) {
            $findings.Add("Unresolved source variable: $source. Add an explicit validation rule for this input.")
            continue
        }
        $sourcePath = Join-Path $installer ($source.Replace('\', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            $findings.Add("Missing installer source: $source")
        }
    }
} catch { $findings.Add("Installer.wxs: $($_.Exception.Message)") }

foreach ($relative in @('scripts/build-msi.ps1', 'src/ETL-SQL.Installer/configure-portal-jwt.ps1')) {
    $file = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        $findings.Add("Missing PowerShell source: $relative")
        continue
    }
    $tokens = $null
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$parseErrors)
    foreach ($issue in $parseErrors) {
        $findings.Add("${relative}:$($issue.Extent.StartLineNumber): $($issue.Message)")
    }
}

try {
    # Parse only. The installer JScript must never execute on the preflight host.
    $output = & node --check (Join-Path $installer 'installer-actions.js') 2>&1
    if ($LASTEXITCODE -ne 0) { $findings.Add("installer-actions.js syntax: $($output -join [Environment]::NewLine)") }
} catch { $findings.Add("installer-actions.js: $($_.Exception.Message)") }

foreach ($finding in $findings) { Write-Output "FAIL $finding" }
if ($findings.Count -gt 0) { exit 1 }
Write-Output 'Installer source checks passed: XML, static source files, retired-helper policy and PowerShell/JavaScript syntax. WiX compilation and installation remain required.'
