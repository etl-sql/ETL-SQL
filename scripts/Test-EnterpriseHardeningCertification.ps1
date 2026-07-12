<#
.SYNOPSIS
    Runs the enterprise hardening certification slice and writes retained evidence.

.DESCRIPTION
    This is the short, repeatable evidence lane for Phase 3 enterprise hardening closeout. It
    exercises path/link race guards, DNS rebinding and redirect controls, connector aliases,
    standalone behavior, remote policy/vault recovery, and portal HTTP transport paths. Run it on
    Windows and Linux/WSL before publishing the release evidence.
#>
[CmdletBinding()]
param(
    [string]$RunId = '',
    [string]$Platform = '',
    [string]$OutDir = '',
    [string]$ArtifactsPath = '',
    [switch]$SkipPortalTests,
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = 'enterprise-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
}
if ([string]::IsNullOrWhiteSpace($Platform)) {
    if ($IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        $Platform = 'windows'
    } elseif ($IsLinux -or [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        $Platform = 'linux'
    } elseif ($IsMacOS -or [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        $Platform = 'macos'
    } else {
        $Platform = 'unknown'
    }
}
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot "certification-results/enterprise-hardening/$RunId/$Platform"
}
if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    if ($Platform -in @('linux', 'macos')) {
        $ArtifactsPath = Join-Path ([System.IO.Path]::GetTempPath()) "etl-sql-enterprise-hardening-$RunId"
    } else {
        $ArtifactsPath = Join-Path $OutDir 'dotnet-artifacts'
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null

function Invoke-CertCommand {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [string]$LogName,
        [string]$StepArtifactsPath = ''
    )

    $logPath = Join-Path $OutDir $LogName
    if (-not [string]::IsNullOrWhiteSpace($StepArtifactsPath)) {
        Remove-Item -LiteralPath $StepArtifactsPath -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $StepArtifactsPath | Out-Null
    }
    $started = Get-Date
    $previousRepoRoot = $env:ETLSQL_REPO_ROOT
    $env:ETLSQL_REPO_ROOT = $RepoRoot.Path
    Push-Location $RepoRoot
    try {
        & dotnet @Arguments *>&1 | Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
        if ($null -eq $previousRepoRoot) {
            Remove-Item Env:\ETLSQL_REPO_ROOT -ErrorAction SilentlyContinue
        } else {
            $env:ETLSQL_REPO_ROOT = $previousRepoRoot
        }
    }

    [pscustomobject]@{
        name = $Name
        command = 'dotnet ' + ($Arguments -join ' ')
        logPath = (Resolve-Path -LiteralPath $logPath).Path
        exitCode = $exitCode
        startedAt = $started.ToUniversalTime().ToString('o')
        finishedAt = (Get-Date).ToUniversalTime().ToString('o')
        passed = ($exitCode -eq 0)
    }
}

function Write-Markdown {
    param([object]$Summary, [string]$Path)

    $lines = @(
        '# Enterprise Hardening Certification',
        '',
        ('Run id: `{0}`' -f $Summary.runId),
        ('Platform: `{0}`' -f $Summary.platform),
        ('Commit: `{0}`' -f $Summary.commit),
        ('Status: **{0}**' -f $Summary.status),
        '',
        '| Step | Result | Log |',
        '| :--- | :--- | :--- |'
    )
    foreach ($step in @($Summary.steps)) {
        $result = if ($step.passed) { 'Passed' } else { "Failed ($($step.exitCode))" }
        $lines += '| {0} | {1} | `{2}` |' -f $step.name, $result, $step.logPath
    }
    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$commit = ''
try {
    $commit = (& git -C $RepoRoot rev-parse HEAD 2>$null).Trim()
} catch { }

$engineArtifactsPath = Join-Path $ArtifactsPath 'engine'
$portalArtifactsPath = Join-Path $ArtifactsPath 'portal'

$testArgsCommon = @(
    'test',
    'tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj',
    '--filter',
    'FullyQualifiedName~ConnectorPolicyEnforcementTests|FullyQualifiedName~RestApiTests|FullyQualifiedName~SharePointAndADConnectorTests|FullyQualifiedName~SecretProviderTests|FullyQualifiedName~OrganizationPolicySourceTests|FullyQualifiedName~GovernanceRecoveryTests|FullyQualifiedName~EnterprisePolicyRuntimeTests|FullyQualifiedName~FileSystemPolicyAuthorizerTests|FullyQualifiedName~FileSystemPolicyEnforcementTests|FullyQualifiedName~StmtFileSystemTests|FullyQualifiedName~OpenLineageExportTests|FullyQualifiedName~ReportLauncherTests',
    '--logger',
    'trx;LogFileName=enterprise-hardening.trx',
    '--results-directory',
    $OutDir,
    '--artifacts-path',
    $engineArtifactsPath
)
if ($Platform -eq 'linux') {
    $testArgsCommon += @('--runtime', 'linux-x64')
} elseif ($Platform -eq 'macos') {
    $testArgsCommon += @('--runtime', 'osx-arm64')
}
if ($NoBuild) {
    $testArgsCommon += '--no-build'
} elseif ($NoRestore) {
    $testArgsCommon += '--no-restore'
}

$steps = New-Object System.Collections.Generic.List[object]
$steps.Add((Invoke-CertCommand -Name 'Engine and connector enterprise hardening tests' -Arguments $testArgsCommon -LogName 'enterprise-hardening-tests.log' -StepArtifactsPath $engineArtifactsPath)) | Out-Null
Remove-Item -LiteralPath $engineArtifactsPath -Recurse -Force -ErrorAction SilentlyContinue

if (-not $SkipPortalTests) {
    $portalArgs = @(
        'test',
        'tests/ETL-SQL.ReportPortal.Tests/ETL-SQL.ReportPortal.Tests.csproj',
        '--filter',
        'FullyQualifiedName~AuditOutboxTransportTests|FullyQualifiedName~OidcAuthTests|FullyQualifiedName~PolicyDistributionApiTests|FullyQualifiedName~AdminServicesTests',
        '--logger',
        'trx;LogFileName=enterprise-hardening-portal.trx',
        '--results-directory',
        $OutDir,
        '--artifacts-path',
        $portalArtifactsPath,
        '-p:EnterpriseHardeningCertification=true'
    )
    if ($Platform -eq 'linux') {
        $portalArgs += @('--runtime', 'linux-x64')
    } elseif ($Platform -eq 'macos') {
        $portalArgs += @('--runtime', 'osx-arm64')
    }
    if ($NoBuild) {
        $portalArgs += '--no-build'
    } elseif ($NoRestore) {
        $portalArgs += '--no-restore'
    }
    $steps.Add((Invoke-CertCommand -Name 'Portal enterprise HTTP and policy tests' -Arguments $portalArgs -LogName 'enterprise-hardening-portal-tests.log' -StepArtifactsPath $portalArtifactsPath)) | Out-Null
    Remove-Item -LiteralPath $portalArtifactsPath -Recurse -Force -ErrorAction SilentlyContinue
}

$stepArray = $steps.ToArray()
$status = if (@($stepArray | Where-Object { -not $_.passed }).Count -eq 0) { 'Passed' } else { 'Failed' }
$summary = [pscustomobject]@{
    schemaVersion = 1
    phase = 'v0.15.0 Enterprise Phase 3 hardening closeout'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    runId = $RunId
    platform = $Platform
    commit = $commit
    status = $status
    steps = @($stepArray)
}

$summaryPath = Join-Path $OutDir 'enterprise-hardening-summary.json'
$markdownPath = Join-Path $OutDir 'enterprise-hardening-summary.md'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Markdown -Summary $summary -Path $markdownPath

[pscustomobject]@{
    status = $status
    runId = $RunId
    platform = $Platform
    summaryPath = (Resolve-Path -LiteralPath $summaryPath).Path
    markdownPath = (Resolve-Path -LiteralPath $markdownPath).Path
}

if ($status -ne 'Passed') {
    exit 1
}
