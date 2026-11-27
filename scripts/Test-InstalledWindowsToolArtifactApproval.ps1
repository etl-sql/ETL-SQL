[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $FixtureParent,
    [Parameter(Mandatory)][string] $FirstExecutable,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string] $FirstSha256,
    [Parameter(Mandatory)][string] $SecondExecutable,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string] $SecondSha256,
    [ValidateRange(1024, 65535)][int] $Port = 15808
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'XT08 installed qualification requires PowerShell 7 on Windows x64.'
}
$MechanicsOnly = $false
. (Join-Path $PSScriptRoot 'lib/windows-tool-artifact-fixture.ps1')
Assert-FixturePublisher
$parent = Resolve-FixtureItem $FixtureParent $true
Assert-PublisherFixture $parent $parent.FullName

$install = Resolve-FixtureItem (Get-ItemPropertyValue -LiteralPath 'HKLM:\Software\ETL-SQL\Install' -Name InstallFolder) $true
$cli = (Resolve-FixtureItem (Join-Path $install.FullName 'ETL-SQL.exe') $false).FullName
$orchestrator = (Resolve-FixtureItem (Join-Path $install.FullName 'ETL-SQL-Service.exe') $false).FullName
$baseUrl = "http://127.0.0.1:$Port"
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
    throw "Port $Port is already in use."
}
$payloads = @(
    @{ Source = (Resolve-FixtureItem $FirstExecutable $false).FullName; Digest = $FirstSha256.ToLowerInvariant() }
    @{ Source = (Resolve-FixtureItem $SecondExecutable $false).FullName; Digest = $SecondSha256.ToLowerInvariant() }
)
foreach ($payload in $payloads) {
    if ((Get-FileHash -LiteralPath $payload.Source).Hash -ine $payload.Digest) {
        throw 'A fixture executable does not match its reviewed SHA-256.'
    }
}
if ($payloads[0].Digest -eq $payloads[1].Digest) { throw 'Supply two distinct reviewed fixtures.' }

$serviceName = 'EtlsqlXt08Installed-' + [guid]::NewGuid().ToString('N')
$workerAccount = "NT SERVICE\$serviceName"
$workspace = [IO.Directory]::CreateDirectory((Join-Path $parent.FullName $serviceName))
Protect-FixtureItem $workspace $null
$settingsPath = Join-Path $install.FullName "appsettings.$serviceName.json"
$machineKeyPath = Join-Path $workspace.FullName 'machine.key'
$apiKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$headers = @{ 'X-Orchestrator-Key' = $apiKey }
$alias = 'xt08fixture'
$digestFailure = 'The approved Windows executable failed SHA-256 verification.'

function Invoke-Approval($Payload, [switch] $ExpectFailure) {
    $start = [Diagnostics.ProcessStartInfo]::new($cli)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($entry in $catalogSettings.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
    foreach ($argument in @('admin', 'machine', 'tool', 'set', '--name', $alias, '--type', 'EXECUTABLE',
        '--option', "COMMAND=$($Payload.Command)", '--option', "ARTIFACT_SHA256=$($Payload.Digest)",
        '--option', 'ARGS=--json')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(45000)) {
            $process.Kill($true)
            throw 'Installed administrator CLI timed out.'
        }
        $diagnostic = "$($stdout.GetAwaiter().GetResult()) $($stderr.GetAwaiter().GetResult())"
        if ($ExpectFailure) {
            if ($process.ExitCode -eq 0 -or -not $diagnostic.Contains($digestFailure)) {
                throw "Expected the installed CLI to refuse reapproval on SHA-256 mismatch. $diagnostic"
            }
        } elseif ($process.ExitCode -ne 0) {
            throw "Installed CLI approval failed. $diagnostic"
        }
    } finally {
        $process.Dispose()
    }
}

function Invoke-ArtifactJob([string] $Stage, [int] $Generation, [string] $Digest, [switch] $ExpectFailure) {
    $scriptText = @"
EXECUTE TOOL '$alias' INTO #artifact_result EXPECT SCHEMA (marker STRING);
ASSERT (SELECT COUNT(*) FROM #artifact_result) = 1, 'Expected one executable output row.';
ASSERT (SELECT COUNT(*) FROM #artifact_result WHERE marker = 'XT08 Windows launch fixture generation $Generation') = 1,
  'Unexpected approved executable output.';
"@
    $request = @{
        Method = 'Post'
        Uri = "$baseUrl/jobs"
        TimeoutSec = 5
        Headers = $headers
        ContentType = 'application/json'
        Body = @{ scriptText = $scriptText; label = "XT08 $Stage" } | ConvertTo-Json -Compress
    }
    $job = Invoke-RestMethod @request
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $status = Invoke-RestMethod -Uri "$baseUrl/jobs/$($job.jobId)" -TimeoutSec 5 -Headers $headers
        if ($status.status -notin 0, 1) { break }
        Start-Sleep -Milliseconds 250
    } while ($timer.Elapsed.TotalSeconds -lt 80)

    if ($ExpectFailure) {
        if ($status.status -ne 3 -or -not ([string]$status.errorMessage).Contains($digestFailure)) {
            throw "$Stage expected a SHA-256 refusal. Status=$($status.status). $($status.errorMessage)"
        }
    } elseif ($status.status -ne 2) {
        throw "$Stage expected the reviewed executable output. Status=$($status.status). $($status.errorMessage)"
    }
    $observedGeneration = if (-not $ExpectFailure) { $Generation }
    [pscustomobject]@{
        Stage = $Stage
        JobId = $job.jobId
        ApprovedSha256 = $Digest
        ObservedGeneration = $observedGeneration
        DigestMismatchRefused = [bool]$ExpectFailure
    }
}

$registered = $false
$settingsCreated = $false
$failed = $false
try {
    Invoke-FixtureSc @('create', $serviceName, 'binPath=', ('"' + $orchestrator + '"'), 'type=', 'own',
        'start=', 'demand', 'obj=', $workerAccount)
    $registered = $true
    Invoke-FixtureSc @('privs', $serviceName, 'SeChangeNotifyPrivilege')
    $workerSid = [Security.Principal.NTAccount]::new($workerAccount).Translate([Security.Principal.SecurityIdentifier])
    Protect-FixtureItem $workspace $workerSid

    $state = [IO.Directory]::CreateDirectory((Join-Path $workspace.FullName 'state'))
    Protect-FixtureItem $state $workerSid 'Modify'
    $catalog = [IO.Directory]::CreateDirectory((Join-Path $workspace.FullName 'catalog'))
    Protect-FixtureItem $catalog $workerSid
    $artifacts = [IO.Directory]::CreateDirectory((Join-Path $workspace.FullName 'artifacts'))
    Protect-FixtureItem $artifacts $workerSid
    $aliasRoot = [IO.Directory]::CreateDirectory((Join-Path $artifacts.FullName $alias))
    Protect-FixtureItem $aliasRoot $workerSid
    foreach ($payload in $payloads) {
        $version = [IO.Directory]::CreateDirectory((Join-Path $aliasRoot.FullName $payload.Digest))
        Protect-FixtureItem $version $workerSid
        $payload.Command = Join-Path $version.FullName 'fixture.exe'
        [IO.File]::Copy($payload.Source, $payload.Command)
        Protect-FixtureItem ([IO.FileInfo]::new($payload.Command)) $workerSid
    }

    $catalogSettings = @{
        Governance__Tools__Provider = 'LOCAL'
        Governance__Tools__LocalRoot = $catalog.FullName
        Governance__Tools__OwnershipMode = 'MachineAdministrator'
        Governance__Tools__ServiceIdentity = $workerAccount
        Governance__Tools__ValidateOwnership = 'true'
        Governance__Tools__ArtifactRoot = $artifacts.FullName
        Governance__Tools__RequireArtifactApproval = 'true'
    }
    Invoke-Approval $payloads[0]

    # Restrict read access before writing the test API key. The SCM environment stores only paths and settings.
    New-Item -ItemType File -Path $settingsPath | Out-Null
    $settingsCreated = $true
    Protect-FixtureItem ([IO.FileInfo]::new($settingsPath)) $workerSid
    @{ Orchestrator = @{ ApiKey = $apiKey } } | ConvertTo-Json |
        Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
    New-Item -ItemType File -Path $machineKeyPath | Out-Null
    Protect-FixtureItem ([IO.FileInfo]::new($machineKeyPath)) $workerSid
    [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) |
        Set-Content -LiteralPath $machineKeyPath -Encoding utf8NoBOM

    $appLogDirectory = Join-Path $state.FullName 'app-logs'
    $settings = $catalogSettings + @{
        DOTNET_ENVIRONMENT = $serviceName
        ASPNETCORE_ENVIRONMENT = $serviceName
        ASPNETCORE_URLS = $baseUrl
        Orchestrator__RequireFederatedIdentity = 'false'
        Orchestrator__DatabasePath = (Join-Path $state.FullName 'orchestrator.db')
        Orchestrator__Database__Provider = 'Sqlite'
        Jobs__ExecutablePath = $cli
        Jobs__UseProcessSpawning = 'true'
        Jobs__UseWarmRunner = 'false'
        Jobs__TimeoutSeconds = '60'
        Engine__SpillRoot = (Join-Path $state.FullName 'spill')
        Session__Root = (Join-Path $state.FullName 'sessions')
        Logging__AppLog__Directory = $appLogDirectory
        Logging__ScriptLog__Directory = (Join-Path $state.FullName 'script-logs')
        ETLSQL_MACHINE_KEY_FILE = $machineKeyPath
        TEMP = $state.FullName
        TMP = $state.FullName
    }
    $environment = @($settings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
    $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
    New-ItemProperty -LiteralPath $serviceKey -Name Environment -PropertyType MultiString -Value $environment | Out-Null

    Invoke-FixtureSc @('start', $serviceName) -TimeoutMs 45000
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $ready = $false
    do {
        try {
            $response = Invoke-WebRequest -Uri "$baseUrl/health/ready" -TimeoutSec 3 -SkipHttpErrorCheck
            $ready = $response.StatusCode -eq 200
        } catch [System.Net.Http.HttpRequestException] {
            # Kestrel has not started listening yet.
        }
        if ($ready) { break }
        Start-Sleep -Milliseconds 250
    } while ($timer.Elapsed.TotalSeconds -lt 45)
    if (-not $ready) { throw 'The restricted installed Orchestrator never became ready.' }
    $unauthenticated = Invoke-WebRequest -Uri "$baseUrl/jobs/xt08" -TimeoutSec 5 -SkipHttpErrorCheck
    if ($unauthenticated.StatusCode -ne 401) { throw 'The installed test service did not enforce its API key.' }

    $catalogEntry = Join-Path $catalog.FullName "$alias.json"
    $receipts = @(
        Invoke-ArtifactJob initial 1 $payloads[0].Digest
        Invoke-Approval $payloads[1]
        Invoke-ArtifactJob rotation 2 $payloads[1].Digest

        # A valid but unapproved executable would satisfy the tamper job's assertion if launched.
        [IO.File]::WriteAllBytes($payloads[1].Command, [IO.File]::ReadAllBytes($payloads[0].Source))
        try {
            Invoke-ArtifactJob tamper 1 $payloads[1].Digest -ExpectFailure
            $before = (Get-FileHash -LiteralPath $catalogEntry).Hash
            Invoke-Approval $payloads[1] -ExpectFailure
            if ((Get-FileHash -LiteralPath $catalogEntry).Hash -ne $before) {
                throw 'A rejected reapproval changed the active catalog entry.'
            }
        } finally {
            [IO.File]::WriteAllBytes($payloads[1].Command, [IO.File]::ReadAllBytes($payloads[1].Source))
        }
        Invoke-ArtifactJob afterRejectedApproval 2 $payloads[1].Digest
        Invoke-Approval $payloads[0]
        Invoke-ArtifactJob rollback 1 $payloads[0].Digest
    )
    foreach ($payload in $payloads) {
        if ((Get-FileHash -LiteralPath $payload.Command).Hash -ine $payload.Digest) {
            throw 'An approved version was lost or changed during job cleanup.'
        }
    }
    $pidLedger = Join-Path $appLogDirectory 'child-pids.json'
    $pids = Get-Content -LiteralPath $pidLedger -Raw | ConvertFrom-Json -NoEnumerate
    if ($pids -isnot [array] -or $pids.Length -ne 0) {
        throw 'The restricted service child-process ledger must exist and be empty after the qualification jobs.'
    }
    $proof = [pscustomobject]@{
        ChildProcessLedgerDrained = $true
        ServiceIdentity = $workerAccount
        InstallationPath = $install.FullName
        OrchestratorSha256 = (Get-FileHash -LiteralPath $orchestrator).Hash
        CliSha256 = (Get-FileHash -LiteralPath $cli).Hash
        RejectedApprovalPreservedCatalog = $true
        Receipts = $receipts
    }
    $proof | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $workspace.FullName 'receipts.json') -Encoding utf8NoBOM
    $proof
} catch {
    $failed = $true
    Write-Warning "XT08 qualification diagnostics are in '$($workspace.FullName)'."
    if ($registered) {
        try {
            $service = Get-CimInstance Win32_Service -Filter "Name = '$serviceName'"
            Write-Warning "Service state=$($service.State), exit=$($service.ExitCode), service exit=$($service.ServiceSpecificExitCode)."
        } catch {
            Write-Warning "Could not read XT08 service status. $($_.Exception.Message)"
        }
    }
    throw
} finally {
    try {
        if ($registered) {
            try { Stop-FixtureService } finally { Invoke-FixtureSc @('delete', $serviceName) }
        }
    } catch {
        if (-not $failed) { throw }
        Write-Warning "Could not clean up the XT08 service. $($_.Exception.Message)"
    } finally {
        if ($settingsCreated) { Remove-Item -LiteralPath $settingsPath }
        if ([IO.File]::Exists($machineKeyPath)) { Remove-Item -LiteralPath $machineKeyPath }
    }
}
