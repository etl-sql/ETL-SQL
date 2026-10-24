param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference = 'Stop'
$scripts = (Get-Item (Join-Path $PSScriptRoot '../..')).FullName
. (Join-Path $scripts 'ReleaseEvidence.Helpers.ps1')
$candidate = 'a' * 40
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($candidate))).ToLowerInvariant()
if (((Get-DeploymentCertificationLanes Profile) -join ',') -cne 'Solo,Team,Enterprise,SaaS,SharedSaaS' -or
    ((Get-DeploymentCertificationLanes Transition) -join ',') -cne 'SoloToTeam,TeamToEnterprise,EnterpriseToSaaS,SoloToSaaS,SaaSToEnterpriseExit,Upgrade') {
    throw 'Canonical inventory differs from the required release profiles/transitions.'
}
$shared = Get-TopologyClaim SharedSaaS
$dedicated = Get-TopologyClaim SaaS
if ($shared.claimScope -cne 'ApplicationAndPolicyContracts' -or $shared.sharedSaaS -cne 'NotCertified' -or
    @($shared.uncovered).Count -ne 2 -or $dedicated.claim -cne 'Managed Dedicated' -or $dedicated.sharedSaaS -cne 'NotCertified') {
    throw 'Shared application contracts and Managed Dedicated must not certify Shared SaaS runtime containment.'
}
$script:next = 0
function New-Evidence {
    $path = Join-Path $Root "case-$script:next"
    $script:next++
    $null = New-Item -ItemType Directory -Path $path
    $claims = [Collections.Generic.List[object]]::new()
    $reports = @{}
    foreach ($kind in @('Profile', 'Transition')) {
        foreach ($lane in (Get-DeploymentCertificationLanes $kind)) {
            $topology = Get-TopologyClaim $lane
            $relative = "$kind-$lane/certification.json"
            $directory = Join-Path $path "$kind-$lane"
            $null = New-Item -ItemType Directory -Path $directory
            'Executed fixture phase' | Set-Content -LiteralPath (Join-Path $directory 'proof.log')
            $now = [DateTimeOffset]::UtcNow.ToString('O')
            $reports[$lane] = [ordered]@{
                schemaVersion = 'etl-sql.deployment-profile-certification/v1'
                commit = $candidate; sourceFingerprint = $fingerprint; dirty = $false; releaseEligible = $true
                configuration = 'Release'; result = 'Passed'; kind = $kind; lanes = @($lane); topologyClaims = @($topology)
                phases = @(@{ phase = 'fixture'; status = 'Passed'; exitCode = 0; command = 'fixture execution'; startedUtc = $now; completedUtc = $now; log = 'proof.log' })
            }
            $claims.Add([ordered]@{
                lane = $lane; kind = $kind; topology = $topology.topology; claim = $topology.claim
                claimScope = if ($null -eq $topology.claimScope) { 'ProfileAndTransitionContracts' } else { $topology.claimScope }
                sharedSaaS = if ($null -eq $topology.sharedSaaS) { 'N/A' } else { $topology.sharedSaaS }
                uncovered = if ($null -eq $topology.uncovered) { @() } else { @($topology.uncovered) }
                commit = $candidate; result = 'Passed'; releaseEligible = $true; evidence = $relative
            })
        }
    }
    return @{ root = $path; reports = $reports; index = @{ schemaVersion = 'etl-sql.deployment-profile-release-claims/v1'; releaseVersion = '0.22.0'; generatedUtc = [DateTimeOffset]::UtcNow.ToString('O'); claims = $claims.ToArray() } }
}
function Save-Evidence {
    param([object]$Evidence)
    foreach ($report in $Evidence.reports.Values) {
        $relative = "$($report.kind)-$($report.lanes[0])/certification.json"
        $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Evidence.root $relative)
    }
    $Evidence.index | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Evidence.root 'claims-index.json')
}
function Archive {
    param([object]$Evidence, [bool]$Complete = $true)
    New-ReleaseCertificationArchive -EvidenceRoot $Evidence.root -ReleaseVersion '0.22.0' -CandidateCommit $candidate `
        -DestinationPath (Join-Path $Evidence.root 'output.zip') -RequireCompleteClaimIndex:$Complete
}
function Read-ArchiveScope {
    param([string]$Path)
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $reader = [IO.StreamReader]::new($zip.GetEntry('archive-manifest.json').Open())
        try { return ($reader.ReadToEnd() | ConvertFrom-Json).claimIndexScope } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
function Assert-Rejected {
    param([string]$Name, [scriptblock]$Mutation)
    $evidence = New-Evidence
    & $Mutation $evidence
    Save-Evidence $evidence
    $destination = Join-Path $evidence.root 'output.zip'
    'previous output' | Set-Content -LiteralPath $destination -NoNewline
    $rejected = $false
    try { Archive $evidence } catch { $rejected = $true }
    if (-not $rejected -or [IO.File]::ReadAllText($destination) -cne 'previous output') { throw "Accepted incomplete/invalid claim index or replaced output: $Name" }
    Write-Host "Rejected $Name"
}
$complete = New-Evidence
Save-Evidence $complete
Archive $complete
if ((Read-ArchiveScope (Join-Path $complete.root 'output.zip')) -cne 'AllProfilesAndTransitions') { throw 'Archive lacks complete-index scope.' }
foreach ($kind in @('Profile', 'Transition')) {
    foreach ($lane in (Get-DeploymentCertificationLanes $kind)) {
        $missing = "$kind/$lane"
        Assert-Rejected "missing $missing" { param($e) $e.index.claims = @($e.index.claims | Where-Object { "$($_.kind)/$($_.lane)" -cne $missing }) }
    }
}
Assert-Rejected 'duplicate claim' { param($e) $e.index.claims += $e.index.claims[0] }
Assert-Rejected 'unknown claim' { param($e) $e.index.claims[0].lane = 'Unknown' }
Assert-Rejected 'wrong claim kind' { param($e) $e.index.claims[0].kind = 'Transition' }
Assert-Rejected 'required claim marked NotCertified' { param($e) $e.index.claims[0].result = 'NotCertified'; $e.index.claims[0].releaseEligible = $false }
Assert-Rejected 'string eligibility' { param($e) $e.index.claims[0].releaseEligible = 'true' }
Assert-Rejected 'wrong lane casing' { param($e) $e.index.claims[0].lane = 'solo' }
Assert-Rejected 'Debug report' { param($e) $e.reports.Solo.configuration = 'Debug' }
Assert-Rejected 'duplicate report lane' { param($e) $e.reports.Solo.lanes += 'Solo' }
Assert-Rejected 'unknown report lane' { param($e) $e.reports.Solo.lanes += 'Unknown' }
Assert-Rejected 'missing report configuration' { param($e) $e.reports.Solo.Remove('configuration') }
Assert-Rejected 'string report eligibility' { param($e) $e.reports.Solo.releaseEligible = 'true' }
Assert-Rejected 'string dirty flag' { param($e) $e.reports.Solo.dirty = 'false' }
Assert-Rejected 'self-consistent inflated Shared SaaS scope' { param($e)
    ($e.index.claims | Where-Object lane -eq 'SharedSaaS').claimScope = 'LiveHardenedRuntime'
    $e.reports.SharedSaaS.topologyClaims[0].claimScope = 'LiveHardenedRuntime'
}
Assert-Rejected 'self-consistent omitted Shared SaaS gaps' { param($e)
    ($e.index.claims | Where-Object lane -eq 'SharedSaaS').uncovered = @()
    $e.reports.SharedSaaS.topologyClaims[0].uncovered = @()
}
$partial = New-Evidence
$partial.index.claims = @($partial.index.claims | Where-Object lane -eq 'Solo')
Save-Evidence $partial
Archive $partial $false
if ((Read-ArchiveScope (Join-Path $partial.root 'output.zip')) -cne 'SelectedClaims') { throw 'Partial archive must retain selected-claim scope.' }

# Execute the real publisher only in a private fixture; trip before any output replacement/build.
$publisher = Join-Path $Root 'publisher'
$null = New-Item -ItemType Directory -Path (Join-Path $publisher 'scripts')
$null = New-Item -ItemType Directory -Path (Join-Path $publisher 'release')
$sentinel = Join-Path $publisher 'release/keep.txt'
'keep previous bytes' | Set-Content -LiteralPath $sentinel -NoNewline
foreach ($name in @('publish-release.ps1', 'ReleaseEvidence.Helpers.ps1', 'DeploymentCertification.Contracts.ps1')) {
    Copy-Item -LiteralPath (Join-Path $scripts $name) -Destination (Join-Path $publisher 'scripts')
}
foreach ($probe in @(@{ evidence = $partial; expected = 'Required deployment claim is missing' }, @{ evidence = $complete; expected = 'PUBLISHER_REACHED_OUTPUT_REPLACEMENT' })) {
    $output = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run-publisher-claim-index-probe.ps1') -PublisherRoot $publisher -EvidenceRoot $probe.evidence.root 2>&1
    if ($LASTEXITCODE -eq 0 -or ($output -join "`n") -notmatch [regex]::Escape($probe.expected) -or
        [IO.File]::ReadAllText($sentinel) -cne 'keep previous bytes') { throw "Publisher evidence preflight probe failed: $($probe.expected)" }
}
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $scripts 'publish-release.ps1'), [ref]$tokens, [ref]$errors)
$calls = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and
    $node.GetCommandName() -in @('Get-ReleaseCertificationInputs', 'New-ReleaseCertificationArchive') }, $true))
if ($calls.Count -ne 2) { throw 'Expected both publisher evidence checks.' }
foreach ($call in $calls) {
    if (@($call.CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -ceq 'RequireCompleteClaimIndex' }).Count -ne 1) {
        throw 'Publisher must require complete claims before publishing and while archiving.'
    }
}
Write-Host 'Complete claim indexes: canonical scopes, all eleven required rows, invalid metadata, partial archive compatibility and actual publisher preflight verified.'
