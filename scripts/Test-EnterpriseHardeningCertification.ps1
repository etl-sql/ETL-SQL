<#
.SYNOPSIS
    Runs the enterprise hardening certification slice and writes retained evidence.

.DESCRIPTION
    This is the short, repeatable evidence lane for enterprise policy and monitoring closeout. It
    exercises enrollment, signed retrieval and cache recovery, executable host bootstrap, scheduled
    and spawned runners, parallel policy propagation, operation boundaries, security-event delivery,
    path/link race guards, DNS rebinding and redirect controls, connector aliases, standalone
    behavior, remote policy/vault recovery, and Portal HTTP transport paths. Run it on Windows and
    Linux before publishing the release evidence.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RunId = '',
    [string]$Platform = '',
    [string]$OutDir = '',
    [string]$ArtifactsPath = '',
    [switch]$SkipPortalTests,
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$Configuration = if ($Configuration -eq 'Release') { 'Release' } else { 'Debug' }
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')
. (Join-Path $ScriptRoot 'Release.Helpers.ps1')

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = 'enterprise-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
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

if (Test-Path -LiteralPath $OutDir) {
    $existing = Get-Item -LiteralPath $OutDir -Force
    if (-not $existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        @(Get-ChildItem -LiteralPath $OutDir -Force).Count -gt 0) {
        throw 'Enterprise evidence directory must be new or empty. Select a new OutDir/RunId to preserve earlier attempts.'
    }
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
# Reserve empty directories exclusively so concurrent invocations cannot overwrite one attempt.
try {
    $claim = [IO.File]::Open((Join-Path $OutDir 'enterprise-attempt.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
} catch { throw 'Cannot exclusively claim enterprise evidence directory. Select a new writable OutDir/RunId.' }
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes(([ordered]@{
        schemaVersion = 1; runId = $RunId; platform = $Platform; configuration = $Configuration
        startedAt = [DateTimeOffset]::UtcNow.ToString('o')
    } | ConvertTo-Json))
    $claim.Write($bytes, 0, $bytes.Length)
} finally { $claim.Dispose() }
$ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
$privateBuildRoot = ''
if (-not $NoBuild -and -not $NoRestore) {
    New-Item -ItemType Directory -Force -Path $ArtifactsPath | Out-Null
    $privateBuildRoot = Join-Path $ArtifactsPath ('enterprise-build-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $privateBuildRoot | Out-Null
}

function Remove-OwnedEnterpriseBuildDirectory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if ([string]::IsNullOrWhiteSpace($privateBuildRoot)) { throw 'No private enterprise build directory is owned by this attempt.' }
    $target = [IO.Path]::GetFullPath($Path)
    $owned = [IO.Path]::GetFullPath($privateBuildRoot)
    $base = $ArtifactsPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $owned.StartsWith($base, $comparison) -or
        ($target -cne $owned -and $target -cne (Join-Path $owned 'engine') -and $target -cne (Join-Path $owned 'portal'))) {
        throw 'Refusing cleanup outside this attempt''s private enterprise build directory.'
    }
    foreach ($directory in @($owned, $target)) {
        if ((Test-Path -LiteralPath $directory) -and
            ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing enterprise build cleanup through a link.'
        }
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}

function Invoke-CertCommand {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [string]$LogName,
        [string]$ExpectedResultPath,
        [string]$StepArtifactsPath = ''
    )

    $logPath = Join-Path $OutDir $LogName
    if (-not [string]::IsNullOrWhiteSpace($StepArtifactsPath)) {
        New-Item -ItemType Directory -Path $StepArtifactsPath | Out-Null
    }
    $started = Get-Date
    Push-Location $RepoRoot
    try {
        & dotnet @Arguments *>&1 | Tee-Object -FilePath $logPath | Out-Host
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if (-not (Test-ReleaseTrxComplete -Path $ExpectedResultPath)) {
        $message = "Certification step '$Name' produced missing, empty, skipped or unsuccessful TRX evidence at '$ExpectedResultPath'."
        Add-Content -LiteralPath $logPath -Value $message -Encoding UTF8
        if ($exitCode -eq 0) { $exitCode = 1 }
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
        ('Configuration: `{0}`' -f $Summary.configuration),
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

$source = Get-ReleaseCertificationSource -RepoRoot $RepoRoot
$commit = $source.commit

$engineArtifactsPath = if ($privateBuildRoot) { Join-Path $privateBuildRoot 'engine' } else { '' }
$portalArtifactsPath = if ($privateBuildRoot) { Join-Path $privateBuildRoot 'portal' } else { '' }

$testArgsCommon = @(
    'test',
    'tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj',
    '--configuration', $Configuration,
    '--filter',
    'FullyQualifiedName~ConnectorPolicyEnforcementTests|FullyQualifiedName~RestApiTests|FullyQualifiedName~SharePointAndADConnectorTests|FullyQualifiedName~SecretProviderTests|FullyQualifiedName~OrganizationPolicySourceTests|FullyQualifiedName~OrganizationPolicyCacheTests|FullyQualifiedName~OrganizationPolicySchemaTests|FullyQualifiedName~PolicyAuthorityServiceTests|FullyQualifiedName~GovernanceRecoveryTests|FullyQualifiedName~GovernanceEnforcementAuditTests|FullyQualifiedName~EnterpriseEnrollmentTests|FullyQualifiedName~EnterprisePolicyRuntimeTests|FullyQualifiedName~EnterpriseHostCertificationTests|FullyQualifiedName~ExecutionSnapshotPropagationTests|FullyQualifiedName~ProcessJobExecutorChaosTests|FullyQualifiedName~PolicyRefreshBypassTests|FullyQualifiedName~FileSystemPolicyAuthorizerTests|FullyQualifiedName~FileSystemPolicyEnforcementTests|FullyQualifiedName~FileConnectorWriteBoundaryTests|FullyQualifiedName~MutationGuardrailEnforcementTests|FullyQualifiedName~ResourceCeilingEnforcementTests|FullyQualifiedName~ProcessPolicyRulesTests|FullyQualifiedName~StandaloneRegressionTests|FullyQualifiedName~SecurityEventContractTests|FullyQualifiedName~SecurityEventRuntimeTests|FullyQualifiedName~SecurityEventOutboxTests|FullyQualifiedName~SecurityEventTransportTests|FullyQualifiedName~SecurityEventHealthGateTests|FullyQualifiedName~BootstrapSecurityEventSinkTests|FullyQualifiedName~StmtFileSystemTests',
    '--logger',
    'trx;LogFileName=enterprise-hardening.trx',
    '--results-directory',
    $OutDir
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
} else {
    $testArgsCommon += @('--artifacts-path', $engineArtifactsPath)
}

$steps = New-Object System.Collections.Generic.List[object]
$engineResultPath = Join-Path $OutDir 'enterprise-hardening.trx'
$steps.Add((Invoke-CertCommand -Name 'Engine and connector enterprise hardening tests' -Arguments $testArgsCommon -LogName 'enterprise-hardening-tests.log' -ExpectedResultPath $engineResultPath -StepArtifactsPath $(if ($NoBuild -or $NoRestore) { '' } else { $engineArtifactsPath }))) | Out-Null
Remove-OwnedEnterpriseBuildDirectory $engineArtifactsPath

if (-not $SkipPortalTests) {
    $portalArgs = @(
        'test',
        'tests/ETL-SQL.Portal.Tests/ETL-SQL.Portal.Tests.csproj',
        '--configuration', $Configuration,
        '--filter',
        'FullyQualifiedName~AuditOutboxTransportTests|FullyQualifiedName~OidcAuthTests|FullyQualifiedName~PolicyDistributionApiTests|FullyQualifiedName~PolicyAuthorityApiTests|FullyQualifiedName~GovernanceRecoveryCertificationTests|FullyQualifiedName~FleetContainmentTests|FullyQualifiedName~FleetHealthAggregatorTests|FullyQualifiedName~AdminServicesTests',
        '--logger',
        'trx;LogFileName=enterprise-hardening-portal.trx',
        '--results-directory',
        $OutDir
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
    } else {
        $portalArgs += @('--artifacts-path', $portalArtifactsPath)
    }
    $portalResultPath = Join-Path $OutDir 'enterprise-hardening-portal.trx'
    $outboxVariable = 'ETLSQL_SECURITY_EVENT_OUTBOX_PATH'
    $previousOutboxPath = [Environment]::GetEnvironmentVariable($outboxVariable)
    [Environment]::SetEnvironmentVariable($outboxVariable,
        (Join-Path $OutDir "portal-standalone-security-events-$Platform.db"))
    try {
        $steps.Add((Invoke-CertCommand -Name 'Portal enterprise HTTP and policy tests' -Arguments $portalArgs -LogName 'enterprise-hardening-portal-tests.log' -ExpectedResultPath $portalResultPath -StepArtifactsPath $(if ($NoBuild -or $NoRestore) { '' } else { $portalArtifactsPath }))) | Out-Null
    } finally {
        [Environment]::SetEnvironmentVariable($outboxVariable, $previousOutboxPath)
    }
    Remove-OwnedEnterpriseBuildDirectory $portalArtifactsPath
}
Remove-OwnedEnterpriseBuildDirectory $privateBuildRoot

$stepArray = $steps.ToArray()
$status = if (@($stepArray | Where-Object { -not $_.passed }).Count -gt 0) { 'Failed' }
    elseif ($SkipPortalTests) { 'Incomplete' } else { 'Passed' }
Assert-ReleaseCertificationSourceUnchanged -RepoRoot $RepoRoot -Initial $source
$dirtyPaths = $source.dirtyPaths
$summary = [pscustomobject]@{
    schemaVersion = 1
    phase = 'Enterprise policy and monitoring certification'
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    runId = $RunId
    platform = $Platform
    configuration = $Configuration
    commit = $commit
    sourceFingerprint = $source.fingerprint
    dirty = $dirtyPaths.Count -gt 0
    releaseEligible = $status -eq 'Passed' -and $dirtyPaths.Count -eq 0 -and $Configuration -eq 'Release'
    uncovered = @(
        if ($SkipPortalTests) { 'Portal enterprise HTTP and policy tests were not run.' }
        if ($Configuration -ne 'Release') { 'Debug configuration is development evidence and cannot support a release claim.' }
    )
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
