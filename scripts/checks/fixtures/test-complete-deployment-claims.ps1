param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference = 'Stop'
$scripts = (Get-Item (Join-Path $PSScriptRoot '../..')).FullName
. (Join-Path $scripts 'ReleaseEvidence.Helpers.ps1')
. (Join-Path $PSScriptRoot 'DeploymentEvidence.Fixture.ps1')
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
$script:template = $null
function New-Evidence {
    $path = Join-Path $Root "case-$script:next"
    $script:next++
    $null = New-Item -ItemType Directory -Path $path
    if ($null -ne $script:template) {
        $evidence = $script:template | ConvertFrom-Json -AsHashtable
        foreach ($directory in (Get-ChildItem -LiteralPath $evidence.root -Directory)) { Copy-Item -LiteralPath $directory.FullName -Destination $path -Recurse }
        $evidence.root = $path
        return $evidence
    }
    $claims = [Collections.Generic.List[object]]::new()
    $reports = @{}
    $paths = @{}
    foreach ($kind in @('Profile', 'Transition')) {
        foreach ($lane in (Get-DeploymentCertificationLanes $kind)) {
            $topology = Get-TopologyClaim $lane
            $relative = "$kind-$lane/certification.json"
            $paths[$lane] = $relative
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
    $evidence = @{ root = $path; reports = $reports; paths = $paths; index = @{ schemaVersion = 'etl-sql.deployment-profile-release-claims/v1'; releaseVersion = '0.22.0'; generatedUtc = [DateTimeOffset]::UtcNow.ToString('O'); claims = $claims.ToArray() } }
    Add-FixtureExecutionEvidence $evidence
    $evidence.index.generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $script:template = $evidence | ConvertTo-Json -Depth 32
    return $evidence
}
function Save-Evidence {
    param([object]$Evidence)
    foreach ($key in $Evidence.reports.Keys) {
        $report = $Evidence.reports[$key]
        $relative = $Evidence.paths[$key]
        $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Evidence.root $relative)
    }
    $Evidence.index | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Evidence.root 'claims-index.json')
}
function Archive {
    param([object]$Evidence, [bool]$Complete = $true)
    New-ReleaseCertificationArchive -EvidenceRoot $Evidence.root -ReleaseVersion '0.22.0' -CandidateCommit $candidate `
        -DestinationPath (Join-Path $Evidence.root 'output.zip') -RequireCompleteClaimIndex:$Complete -RequireValidatedExecutionPlan:$Complete
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
function Edit-FixtureTrx {
    param([object]$Evidence, [scriptblock]$Edit)
    $phase = $Evidence.reports.Solo.phases[1]
    $path = Join-Path $Evidence.root "Profile-Solo/$($phase.testEvidence.path)"
    [xml]$document = Get-Content -LiteralPath $path -Raw
    & $Edit $document
    $document.Save($path)
    $phase.testEvidence.sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $phase.testEvidence.total = [long]$document.TestRun.ResultSummary.Counters.total
    $phase.testEvidence.passed = [long]$document.TestRun.ResultSummary.Counters.passed
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
Assert-Rejected 'missing required phase' { param($e) $e.reports.Solo.phases = @($e.reports.Solo.phases | Select-Object -Skip 1) }
Assert-Rejected 'duplicate required phase' { param($e) $e.reports.Solo.phases[1] = $e.reports.Solo.phases[0] }
Assert-Rejected 'changed phase order' { param($e) $swap = $e.reports.Solo.phases[0]; $e.reports.Solo.phases[0] = $e.reports.Solo.phases[1]; $e.reports.Solo.phases[1] = $swap }
Assert-Rejected 'wrong prerequisite tag' { param($e) $e.reports.Enterprise.phases[1].prerequisite = 'other' }
Assert-Rejected 'wrong proof contract' { param($e) $e.reports.Solo.phases[1].proof = 'other' }
Assert-Rejected 'changed invocation with matching display command' { param($e) $p = $e.reports.Solo.phases[1]; $p.invocation.arguments[5] = 'FullyQualifiedName~Wrong'; $p.command = 'dotnet ' + ($p.invocation.arguments -join ' ') }
Assert-Rejected 'NoBuild evidence without build provenance' { param($e) $e.reports.Solo.noBuild = $true }
Assert-Rejected 'noncanonical report kind' { param($e) $e.reports.Enterprise.kind = 'profile' }
Assert-Rejected 'aliased phase log' { param($e) $e.reports.Solo.phases[1].log = $e.reports.Solo.phases[0].log }
Assert-Rejected 'missing TRX file' { param($e) $p = $e.reports.Solo.phases[1]; [IO.File]::Delete((Join-Path $e.root "Profile-Solo/$($p.testEvidence.path)")) }
Assert-Rejected 'changed TRX bytes' { param($e) $p = $e.reports.Solo.phases[1]; [IO.File]::AppendAllText((Join-Path $e.root "Profile-Solo/$($p.testEvidence.path)"), ' ') }
Assert-Rejected 'aliased phase TRX' { param($e) $e.reports.Solo.phases[1].testEvidence = $e.reports.Solo.phases[0].testEvidence }
Assert-Rejected 'TRX outside phase window' { param($e) $e.reports.Solo.phases[1].startedUtc = [DateTimeOffset]::UtcNow.ToString('O') }
Assert-Rejected 'unproven Enterprise prerequisite' { param($e) $e.reports.Enterprise.hostedPrerequisites[0].proven = $false }
Assert-Rejected 'missing Enterprise prerequisite' { param($e) $e.reports.Enterprise.hostedPrerequisites = @($e.reports.Enterprise.hostedPrerequisites | Select-Object -Skip 1) }
Assert-Rejected 'missing concrete scenario reference' { param($e) $e.reports.Upgrade.scenarioEvidenceFiles = @() }
Assert-Rejected 'missing concrete scenario file' { param($e) [IO.File]::Delete((Join-Path $e.root 'Transition-Upgrade/scenario-evidence/SoloUpgrade.json')) }
Assert-Rejected 'changed concrete scenario file' { param($e) [IO.File]::AppendAllText((Join-Path $e.root 'Transition-Upgrade/scenario-evidence/SoloUpgrade.json'), ' ') }
Assert-Rejected 'missing nested fault bundle' { param($e) [IO.File]::Delete((Join-Path $e.root 'Profile-Solo/provider-neutral-faults/Solo/certification.json')) }
$nestedPath = 'Profile-Solo/provider-neutral-faults/Solo/certification.json'
Assert-Rejected 'nested fault report missing receipt' { param($e) $path = Join-Path $e.root $nestedPath; $r = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable; $r.runs[0].testEvidence = $null; $r | ConvertTo-Json -Depth 32 | Set-Content $path }
Assert-Rejected 'nested fault run outside parent phase' { param($e) $path = Join-Path $e.root $nestedPath; $r = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable; $r.runs[0].startedUtc = [DateTimeOffset]::UtcNow.AddDays(-1).ToString('O'); $r | ConvertTo-Json -Depth 32 | Set-Content $path }
Assert-Rejected 'self-consistent repeated fault scenario' { param($e)
    $path = Join-Path $e.root $nestedPath; $r = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable
    $details = Join-Path (Split-Path -Parent $path) $r.reportFiles[0].file
    $d = Get-Content $details -Raw | ConvertFrom-Json -AsHashtable; $d.runs[1] = $d.runs[0]; $d | ConvertTo-Json -Depth 32 | Set-Content $details
    $r.reportFiles[0].sha256 = (Get-FileHash $details -Algorithm SHA256).Hash.ToLowerInvariant(); $r | ConvertTo-Json -Depth 32 | Set-Content $path
}
Assert-Rejected 'failed TRX outcome with passing counters' { param($e) Edit-FixtureTrx $e { param($d) $d.TestRun.Results.UnitTestResult[0].SetAttribute('outcome', 'Failed') } }
Assert-Rejected 'skipped TRX outcome' { param($e) Edit-FixtureTrx $e { param($d) $d.TestRun.Results.UnitTestResult[0].SetAttribute('outcome', 'NotExecuted'); $d.TestRun.ResultSummary.Counters.SetAttribute('notExecuted', '1') } }
Assert-Rejected 'duplicate TRX identity' { param($e) Edit-FixtureTrx $e { param($d) $d.TestRun.TestDefinitions.UnitTest[1].SetAttribute('id', 'test-1') } }
Assert-Rejected 'unexecuted planned test selector with consistent counters' { param($e) Edit-FixtureTrx $e { param($d)
    $null = $d.TestRun.TestDefinitions.RemoveChild($d.TestRun.TestDefinitions.UnitTest[1]); $null = $d.TestRun.Results.RemoveChild($d.TestRun.Results.UnitTestResult[1])
    foreach ($name in @('total', 'executed', 'passed')) { $d.TestRun.ResultSummary.Counters.SetAttribute($name, '1') }
} }
Assert-Rejected 'unexpected tests with passing counters' { param($e) Edit-FixtureTrx $e { param($d)
    $d.TestRun.TestDefinitions.UnitTest[0].TestMethod.SetAttribute('className', 'NotInThePlannedFilter')
} }
Assert-Rejected 'DTD in TRX' { param($e)
    $p = $e.reports.Solo.phases[1]; $path = Join-Path $e.root "Profile-Solo/$($p.testEvidence.path)"
    $raw = [IO.File]::ReadAllText($path).Replace('<TestRun', '<!DOCTYPE TestRun [<!ENTITY ignored "unused">]><TestRun')
    [IO.File]::WriteAllText($path, $raw); $p.testEvidence.sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
Assert-Rejected 'missing scenario rollback with consistent file hash' { param($e)
    $reference = $e.reports.Upgrade.scenarioEvidenceFiles[0]
    $path = Join-Path $e.root "Transition-Upgrade/$($reference.path)"; $r = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable
    $r.Remove('rollback'); $r | ConvertTo-Json -Depth 16 | Set-Content $path; $reference.sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
Assert-Rejected 'incorrect fault invariant despite summary Passed' { param($e)
    $path = Join-Path $e.root $nestedPath; $r = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable
    $details = Join-Path (Split-Path -Parent $path) $r.reportFiles[0].file
    $d = Get-Content $details -Raw | ConvertFrom-Json -AsHashtable; $d.runs[0].invariants.noSplitBrainMutation = $false; $d | ConvertTo-Json -Depth 32 | Set-Content $details
    $r.reportFiles[0].sha256 = (Get-FileHash $details -Algorithm SHA256).Hash.ToLowerInvariant(); $r | ConvertTo-Json -Depth 32 | Set-Content $path
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
foreach ($name in @('publish-release.ps1', 'ReleaseEvidence.Helpers.ps1', 'DeploymentCertification.Contracts.ps1', 'DeploymentCertification.Plan.ps1', 'DeploymentCertification.Evidence.ps1', 'DeploymentTestEvidence.Helpers.ps1')) {
    Copy-Item -LiteralPath (Join-Path $scripts $name) -Destination (Join-Path $publisher 'scripts')
}
$null = New-Item -ItemType Directory -Path (Join-Path $publisher 'scripts/lib')
Copy-Item -LiteralPath (Join-Path $scripts 'lib/package-runtime-state.patterns') -Destination (Join-Path $publisher 'scripts/lib')
$null = New-Item -ItemType Directory -Path (Join-Path $publisher 'tests/fixtures') -Force
Copy-Item -LiteralPath (Join-Path $scripts '../tests/fixtures/provider-neutral-fault-matrix.json') -Destination (Join-Path $publisher 'tests/fixtures')
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
    if (@($call.CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -ceq 'RequireValidatedExecutionPlan' }).Count -ne 1) { throw 'Publisher must validate execution receipts at both evidence checks.' }
}
Write-Host 'Complete claim indexes: canonical scopes, all eleven required rows, invalid metadata, partial archive compatibility and actual publisher preflight verified.'
