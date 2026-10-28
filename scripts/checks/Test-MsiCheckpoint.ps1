$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '../Test-MsiPortalUpgrade.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$null)
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-ResourceSnapshot' }, $true)
if ($null -eq $function) { throw 'Missing production checkpoint function.' }
Invoke-Expression $function.Extent.Text
function Get-CimInstance { param($ClassName) [pscustomobject]@{ FreePhysicalMemory = 2048; TotalVisibleMemorySize = 4096 } }
function Get-Process { [pscustomobject]@{ ProcessName = 'fixture'; CPU = 1; WorkingSet64 = 1048576 } }
function Get-Service { param($Name, $ErrorAction) $null }
$summary = [IO.Path]::GetTempFileName()
$previous = $env:GITHUB_ACTIONS
try {
    $env:GITHUB_ACTIONS = 'true'
    $stage = "fresh%0A::error::not-a-command`nnext"
    $lines = @(Write-ResourceSnapshot $stage 6>&1 | ForEach-Object { $_.ToString() })
    if ($lines.Count -ne 1 -or -not $lines[0].StartsWith('::notice title=MSI checkpoint::')) { throw 'Expected one checkpoint notice.' }
    if ($lines[0].Contains("`n") -or $lines[0].Contains("`r") -or -not $lines[0].Contains('%250A')) { throw 'Unsafe workflow-command data.' }
    $payload = $lines[0].Substring('::notice title=MSI checkpoint::'.Length).Replace('%0D', "`r").Replace('%0A', "`n").Replace('%25', '%') | ConvertFrom-Json
    if ($payload.stage -cne $stage -or $payload.freeMemoryMiB -ne 2 -or $payload.totalMemoryMiB -ne 4) { throw 'Checkpoint fields changed.' }
    $env:GITHUB_ACTIONS = 'false'
    if (@(Write-ResourceSnapshot 'local check' 6>&1).Count -ne 0) { throw 'A local run emitted a GitHub command.' }
    if (-not (Get-Content -LiteralPath $summary -Raw).Contains('free memory')) { throw 'Local resource diagnostics were lost.' }
    Write-Host 'MSI checkpoint: production function emits bounded escaped hosted diagnostics and keeps local runs independent.'
} finally {
    $env:GITHUB_ACTIONS = $previous
    Remove-Item -LiteralPath $summary -Force
}
