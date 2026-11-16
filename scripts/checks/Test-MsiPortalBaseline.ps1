$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../Test-MsiPortalUpgrade.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$null, [ref]$null)
$guard = $ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.IfStatementAst] -and
    $_.Extent.Text.Contains('Partial Portal scenarios are diagnostic-only') }
if (@($guard).Count -ne 1) { throw 'Missing partial-scenario certification guard.' }
$priorDiagnostic = $env:ETLSQL_MSI_DIAGNOSTIC_ONLY
try {
    $DiagnosticScenario = 'UpgradeRollback'
    $env:ETLSQL_MSI_DIAGNOSTIC_ONLY = ''
    $rejected = $false
    try { Invoke-Expression $guard.Extent.Text } catch { $rejected = $true }
    if (-not $rejected) { throw 'Partial certification accepted outside diagnostic mode.' }
    $env:ETLSQL_MSI_DIAGNOSTIC_ONLY = '1'
    Invoke-Expression $guard.Extent.Text
    $fresh = $ast.Find({ param($node) $node -is [Management.Automation.Language.IfStatementAst] -and
        $node.Clauses[0].Item1.Extent.Text -eq "$" + "DiagnosticScenario -ceq 'All'" }, $true)
    if ($null -eq $fresh) { throw 'Fresh lifecycle is not explicitly selected.' }
    if (Invoke-Expression $fresh.Clauses[0].Item1.Extent.Text) { throw 'Upgrade-only diagnostic selected fresh install.' }
    $DiagnosticScenario = 'All'
    if (-not (Invoke-Expression $fresh.Clauses[0].Item1.Extent.Text)) { throw 'Full certification omitted fresh install.' }
} finally { $env:ETLSQL_MSI_DIAGNOSTIC_ONLY = $priorDiagnostic }
foreach ($name in @('Get-Setting', 'Initialize-PreviousPortalUpgradeBaseline')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if ($null -eq $function) { throw "Missing baseline function $name" }
    Invoke-Expression $function.Extent.Text
}
$start = $ast.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left.Extent.Text -eq '$baseline' -and $node.Right.Extent.Text -eq 'Initialize-PreviousPortalUpgradeBaseline' }, $true)
$end = $ast.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$cachePrevious' }, $true)
if ($null -eq $start -or $null -eq $end) { throw 'Public upgrade sequence does not capture its previous baseline.' }
$capture = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and
    $node.Extent.StartOffset -ge $start.Extent.StartOffset -and $node.Extent.StartOffset -lt $end.Extent.StartOffset }, $true))
if ($capture.Count -ne 5) { throw 'Unexpected public baseline assignments.' }
$captureCode = ($capture | ForEach-Object { $_.Extent.Text }) -join "`n"
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$installDir = Join-Path $tempRoot ('portal-baseline-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $installDir | Out-Null
try {
    $shared = '{"Portal":{"Jwt":{"Secret":"shared-jwt"},"Dataset":{"AtRestKey":"shared-data"},"Orchestrator":{"ApiKey":"shared-api","IdentitySigningSecret":"shared-signing"}},"Orchestrator":{"ApiKey":"shared-api","IdentitySigningSecret":"shared-signing"}}'
    $protected = $shared.Replace('shared-', 'protected-')
    foreach ($modern in @($false, $true)) {
        Set-Content -LiteralPath (Join-Path $installDir 'appsettings.json') -Value $shared
        if ($modern) { Set-Content -LiteralPath (Join-Path $installDir 'appsettings.secrets.json') -Value $protected }
        $signIn = Join-Path $installDir 'first-run-password.txt'
        Set-Content -LiteralPath $signIn -Value 'synthetic bootstrap'
        $before = Get-FileHash -LiteralPath (Join-Path $installDir 'appsettings.json')
        Invoke-Expression $captureCode
        $prefix = if ($modern) { 'protected-' } else { 'shared-' }
        if ($baseline['Orchestrator:IdentitySigningSecret'] -cne ($prefix + 'signing') -or
            $baseline['Portal:Jwt:Secret'] -cne ($prefix + 'jwt') -or
            $baseline['Orchestrator:ApiKey'] -cne ($prefix + 'api') -or
            $baseline['Portal:Dataset:AtRestKey'] -cne ($prefix + 'data') -or
            $signing -cne ($prefix + 'signing') -or $secret -cne ($prefix + 'jwt') -or
            $apiKey -cne ($prefix + 'api') -or $atRest -cne ($prefix + 'data')) { throw 'Public upgrade sequence did not capture effective settings.' }
        if ((Test-Path -LiteralPath $signIn) -or $before.Hash -cne (Get-FileHash -LiteralPath $before.Path).Hash) { throw 'Bootstrap consumption or shared-file preservation failed.' }
        $again = Initialize-PreviousPortalUpgradeBaseline
        if ($again['Orchestrator:IdentitySigningSecret'] -cne $baseline['Orchestrator:IdentitySigningSecret']) { throw 'Absent bootstrap file changed the baseline.' }
    }
    $invalid = $protected | ConvertFrom-Json
    $invalid.Portal.Orchestrator.IdentitySigningSecret = 'different-synthetic-signing'
    Set-Content -LiteralPath (Join-Path $installDir 'appsettings.secrets.json') -Value ($invalid | ConvertTo-Json -Depth 16)
    Set-Content -LiteralPath $signIn -Value 'synthetic bootstrap'
    $rejected = $false
    try { $null = Initialize-PreviousPortalUpgradeBaseline } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath $signIn)) { throw 'Mismatched mirrors were accepted or consumed bootstrap.' }
    foreach ($empty in @('', $null)) {
        $invalid = $protected | ConvertFrom-Json
        $invalid.Orchestrator.IdentitySigningSecret = $empty
        Set-Content -LiteralPath (Join-Path $installDir 'appsettings.secrets.json') -Value ($invalid | ConvertTo-Json -Depth 16)
        $rejected = $false
        try { $null = Initialize-PreviousPortalUpgradeBaseline } catch { $rejected = $true }
        if (-not $rejected -or -not (Test-Path -LiteralPath $signIn)) { throw 'Empty protected override fell back to shared settings.' }
    }
    foreach ($file in @('appsettings.json', 'appsettings.secrets.json')) {
        Set-Content -LiteralPath (Join-Path $installDir $file) -Value '{}'
    }
    $rejected = $false
    try { $null = Initialize-PreviousPortalUpgradeBaseline } catch { $rejected = $true }
    if (-not $rejected -or -not (Test-Path -LiteralPath $signIn)) { throw 'Empty baseline was accepted or consumed bootstrap.' }
    Write-Host 'Portal upgrade baseline: legacy/shared and protected precedence, consumed bootstrap, idempotency and invalid mirrors passed. No installer executed.'
} finally {
    $resolved = [IO.Path]::GetFullPath($installDir)
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^portal-baseline-[a-f0-9]{32}$') { throw 'Refusing cleanup outside fixture.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
