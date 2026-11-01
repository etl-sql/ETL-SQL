# Select retained installer inputs for diagnosis only. This never emits certification evidence.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[1-9][0-9]{0,19}$')][string]$ArtifactId,
    [string]$OutputDirectory = 'release-validation/msi-portal-diagnostic'
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or $env:GITHUB_REF -cnotmatch '^refs/heads/.+' -or
    $env:GITHUB_REF -cmatch '^refs/heads/(?:main|dev|release(?:/.*)?)$') {
    throw 'MSI diagnosis must use a manually dispatched development branch.'
}
if ($env:GITHUB_REPOSITORY -cnotmatch '^[\w.-]+/[\w.-]+$' -or
    @($env:GITHUB_REPOSITORY -split '/' | Where-Object { $_ -in @('.', '..') }).Count -gt 0 -or
    $env:GITHUB_SHA -cnotmatch '^[a-f0-9]{40}$' -or [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    throw 'Missing diagnostic repository, script identity or output channel.'
}
function Read-DiagnosticApi([string]$Route) {
    $response = & gh api "repos/$env:GITHUB_REPOSITORY/$Route"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read diagnostic input provenance.' }
    return ($response -join "`n" | ConvertFrom-Json)
}
$artifact = Read-DiagnosticApi "actions/artifacts/$ArtifactId"
$artifactName = [regex]::Match([string]$artifact.name, '^msi-inputs-([1-9][0-9]*)-([1-9][0-9]*)$')
if ([string]$artifact.id -cne $ArtifactId -or $artifact.expired -or
    -not $artifactName.Success -or
    $artifact.digest -cnotmatch '^sha256:[a-f0-9]{64}$') { throw 'Not a retained MSI input artifact.' }
$originRun = $artifactName.Groups[1].Value; $originAttempt = [int]$artifactName.Groups[2].Value
if ([string]$artifact.workflow_run.id -cne $originRun) { throw 'MSI artifact run mismatch.' }
$run = Read-DiagnosticApi "actions/runs/$originRun"
if ($run.repository.full_name -cne $env:GITHUB_REPOSITORY -or
    $run.head_repository.full_name -cne $env:GITHUB_REPOSITORY -or
    $run.status -cne 'completed' -or $run.path -cne '.github/workflows/msi-upgrade.yml' -or
    $run.run_attempt -lt $originAttempt -or $run.head_sha -cnotmatch '^[a-f0-9]{40}$') {
    throw 'MSI input origin is not a completed repository MSI run.'
}
$jobs = Read-DiagnosticApi "actions/runs/$originRun/attempts/$originAttempt/jobs?per_page=100"
if ($jobs.total_count -ne @($jobs.jobs).Count -or
    @($jobs.jobs | Where-Object { $_.name -ceq 'Build MSI certification inputs' -and $_.conclusion -ceq 'success' }).Count -ne 1) {
    throw 'MSI input artifact has no original successful build.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new diagnostic evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
@{
    schemaVersion = 1; scope = 'DiagnosticOnly'; scriptSha = $env:GITHUB_SHA
    repository = $env:GITHUB_REPOSITORY; inputArtifactId = $ArtifactId
    inputArtifactDigest = $artifact.digest; inputRunId = $originRun
    inputAttempt = $originAttempt; inputRunHead = $run.head_sha
    uncovered = 'No release certification: diagnostic scripts and retained installer sources may differ.'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'origin.json')
"artifact_id=$ArtifactId" >> $env:GITHUB_OUTPUT
"origin_head=$($run.head_sha)" >> $env:GITHUB_OUTPUT
Write-Host 'Retained MSI inputs selected for diagnosis only; no build requested.'
