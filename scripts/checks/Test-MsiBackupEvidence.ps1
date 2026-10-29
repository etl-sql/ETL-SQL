$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../Test-MsiPortalUpgrade.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$null, [ref]$null)
$statements = @($ast.EndBlock.Statements)
$index = 0
while ($index -lt $statements.Count -and -not $statements[$index].Extent.Text.StartsWith('$privateBackupRoot =')) { $index++ }
if ($index + 2 -ge $statements.Count) { throw 'Missing backup scratch initialization.' }
$initialize = ($statements[$index..($index + 2)] | ForEach-Object { $_.Extent.Text }) -join "`n"
$destinations = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.AssignmentStatementAst] -and
    $node.Right.Extent.Text -in @("Join-Path `$privateBackupRoot 'fresh-backup'", "Join-Path `$privateBackupRoot 'pre-upgrade-backup'")
}, $true))
if ($destinations.Count -ne 2) { throw 'Both backup producers must use private scratch.' }
$cleanup = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.CommandAst] -and
    $node.Extent.Text -eq 'Remove-Item -LiteralPath $privateBackupRoot -Recurse -Force -ErrorAction SilentlyContinue'
}, $true)
if ($null -eq $cleanup) { throw 'Missing private backup cleanup.' }
$out = Join-Path ([IO.Path]::GetTempPath()) ('msi-evidence-fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$fixtureRoot = $out
try {
    foreach ($fail in @($false, $true)) {
        Invoke-Expression $initialize
        try {
            foreach ($destination in $destinations) { Invoke-Expression $destination.Extent.Text }
            foreach ($directory in @($backupDir, $backup)) {
                New-Item -ItemType Directory -Path $directory | Out-Null
                Set-Content -LiteralPath (Join-Path $directory 'fixture-keys.zip') -Value 'synthetic secret'
            }
            if (@(Get-ChildItem -LiteralPath $out -Recurse -File).Count) { throw 'Backup payload entered evidence.' }
            if ($fail) { throw 'Injected certification failure' }
        } catch {
            if ($_.Exception.Message -ne 'Injected certification failure') { throw }
        } finally { Invoke-Expression $cleanup.Extent.Text }
        if (Test-Path -LiteralPath $privateBackupRoot) { throw 'Private backup cleanup failed.' }
    }
    $out = [IO.Path]::GetTempPath()
    $rejected = $false
    try { Invoke-Expression $initialize } catch {
        if ($_.Exception.Message -ne 'Backup scratch must be outside uploaded evidence.') { throw }
        $rejected = $true
    }
    if (-not $rejected -or (Test-Path -LiteralPath $privateBackupRoot)) { throw 'Overlapping evidence root was accepted.' }
    Write-Host 'MSI backup evidence: both producers stay outside evidence; cleanup passes after success/failure; overlapping roots reject.'
} finally {
    # Only the unique fixture directory is eligible for recursive cleanup.
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
