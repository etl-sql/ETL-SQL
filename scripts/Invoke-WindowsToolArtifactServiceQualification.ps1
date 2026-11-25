[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $FixtureParent,
    [Parameter(Mandatory)][string] $ServiceRunner,
    [Parameter(Mandatory)][string] $FirstExecutable,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string] $FirstSha256,
    [Parameter(Mandatory)][string] $FirstMarker,
    [Parameter(Mandatory)][string] $SecondExecutable,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string] $SecondSha256,
    [Parameter(Mandatory)][string] $SecondMarker
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The XT08 service qualification requires PowerShell 7 on Windows.' }

$MechanicsOnly = $false
. (Join-Path $PSScriptRoot 'lib/windows-tool-artifact-fixture.ps1')
Assert-FixturePublisher

$parent = Resolve-FixtureItem $FixtureParent $true
Assert-PublisherFixture $parent $parent.FullName
$runner = Resolve-FixtureItem $ServiceRunner $false
if ($runner.Extension -ine '.exe') { throw 'Provide the published single-file Windows service fixture executable.' }
$powerShell = (Get-Process -Id $PID).Path
$serviceName = 'EtlsqlXt08Probe-' + [guid]::NewGuid().ToString('N')
$workerAccount = "NT SERVICE\$serviceName"

$workspace = [IO.Directory]::CreateDirectory((Join-Path $parent.FullName $serviceName))
Protect-FixtureItem $workspace $null
$runnerPath = Join-Path $workspace.FullName 'ProbeService.exe'
[IO.File]::Copy($runner.FullName, $runnerPath)
$settingsPath = Join-Path $workspace.FullName 'probe-settings.json'
$fixtureScript = Join-Path $PSScriptRoot 'Invoke-WindowsToolArtifactQualification.ps1'
$installed = $false
$qualificationFailed = $false
$startedAt = Get-Date

try {
    $imagePath = '"' + $runnerPath + '" ' + $serviceName
    Invoke-FixtureSc @('create', $serviceName, 'binPath=', $imagePath, 'type=', 'own', 'start=', 'demand', 'obj=', $workerAccount)
    $installed = $true
    Invoke-FixtureSc @('privs', $serviceName, 'SeChangeNotifyPrivilege')

    $reader = [Security.Principal.NTAccount]::new($workerAccount).Translate([Security.Principal.SecurityIdentifier])
    Protect-FixtureItem $workspace $reader
    Protect-FixtureItem ([IO.FileInfo]::new($runnerPath)) $reader

    $probeDir = [IO.Directory]::CreateDirectory((Join-Path $workspace.FullName 'probe'))
    $probeLib = [IO.Directory]::CreateDirectory((Join-Path $probeDir.FullName 'lib'))
    $probeScript = Join-Path $probeDir.FullName 'Invoke-WindowsToolArtifactQualification.ps1'
    [IO.File]::Copy($fixtureScript, $probeScript)
    [IO.File]::Copy((Join-Path $PSScriptRoot 'Test-WindowsToolArtifactBoundary.ps1'),
        (Join-Path $probeDir.FullName 'Test-WindowsToolArtifactBoundary.ps1'))
    [IO.File]::Copy((Join-Path $PSScriptRoot 'lib/windows-tool-artifact-fixture.ps1'),
        (Join-Path $probeLib.FullName 'windows-tool-artifact-fixture.ps1'))

    $prepare = @{
        FixtureParent = $parent.FullName
        WorkerAccount = $workerAccount
        FirstExecutable = $FirstExecutable
        FirstSha256 = $FirstSha256
        FirstMarker = $FirstMarker
        SecondExecutable = $SecondExecutable
        SecondSha256 = $SecondSha256
        SecondMarker = $SecondMarker
    }
    $fixture = & $fixtureScript @prepare
    $fixtureRoot = $fixture.FixtureRoot
    $stages = @(
        @{ Name = 'initial'; Generation = 1; Sha256 = $FirstSha256 },
        @{ Name = 'rotation'; Generation = 2; Sha256 = $SecondSha256 },
        @{ Name = 'rollback'; Generation = 1; Sha256 = $FirstSha256 }
    )
    $receipts = foreach ($stage in $stages) {
        if ($stage.Name -ne 'initial') {
            $null = & $fixtureScript -FixtureRoot $fixtureRoot -Generation $stage.Generation
        }

        $work = [IO.Directory]::CreateDirectory((Join-Path $workspace.FullName $stage.Name))
        Protect-FixtureItem $work $reader 'Modify'
        $receiptPath = Join-Path $work.FullName 'receipt.json'
        @{
            PowerShell = $powerShell
            VerificationScript = $probeScript
            FixtureRoot = $fixtureRoot
            WorkDirectory = $work.FullName
            ReceiptPath = $receiptPath
        } | ConvertTo-Json -Compress | Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM

        Invoke-FixtureSc @('start', $serviceName) -TimeoutMs 45000
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $service = Get-Service -Name $serviceName
        try {
            while (-not [IO.File]::Exists($receiptPath) -and $timer.Elapsed.TotalSeconds -lt 55) {
                $service.Refresh()
                if ($service.Status -eq 'Stopped') { break }
                Start-Sleep -Milliseconds 200
            }
        } finally {
            $service.Dispose()
        }
        Stop-FixtureService
        if (-not [IO.File]::Exists($receiptPath)) {
            throw "The worker did not write a receipt for '$($stage.Name)' in '$($work.FullName)'."
        }

        $receipt = Resolve-FixtureItem $receiptPath $false
        if ($receipt.Length -gt 64KB) { throw "The receipt for '$($stage.Name)' exceeded 64 KiB." }
        $result = Get-Content -LiteralPath $receipt.FullName -Raw | ConvertFrom-Json
        if ($result.ExitCode -ne 0) {
            throw "Service probe failed during '$($stage.Name)'. $($result.Stderr)"
        }
        $proof = $result.Stdout | ConvertFrom-Json
        if ($proof.ServiceBoundaryVerified -isnot [bool] -or -not $proof.ServiceBoundaryVerified -or
            $proof.Sha256Verified -isnot [bool] -or -not $proof.Sha256Verified -or
            $proof.LaunchedWithReadHandle -isnot [bool] -or -not $proof.LaunchedWithReadHandle -or $proof.ActiveGeneration -ne $stage.Generation -or
            $proof.FixtureSha256 -ine $stage.Sha256) {
            throw "The receipt for '$($stage.Name)' did not prove the expected generation and service boundary."
        }
        $proof | Add-Member -NotePropertyName Stage -NotePropertyValue $stage.Name -PassThru
    }
    [pscustomobject]@{
        FixtureRoot = $fixtureRoot
        EvidenceDirectory = $workspace.FullName
        WorkerProfile = 'Windows virtual service account'
        Receipts = $receipts
    }
} catch {
    $qualificationFailed = $true
    if ($installed) {
        try {
            $diagnostic = Get-CimInstance Win32_Service -Filter "Name = '$serviceName'"
            if ($diagnostic) {
                Write-Warning "Service state=$($diagnostic.State), exit=$($diagnostic.ExitCode), service exit=$($diagnostic.ServiceSpecificExitCode)."
            }
            $events = Get-WinEvent -FilterHashtable @{
                LogName = 'System'
                ProviderName = 'Service Control Manager'
                StartTime = $startedAt
            } -ErrorAction SilentlyContinue | Where-Object {
                $_.Message -like "*$serviceName*"
            }
            foreach ($event in $events) {
                Write-Warning "SCM event $($event.Id): $($event.Message)"
            }
        } catch {
            Write-Warning "Could not read service diagnostics: $($_.Exception.Message)"
        }
    }
    throw
} finally {
    if ($installed) {
        try {
            try {
                Stop-FixtureService
            } finally {
                Invoke-FixtureSc @('delete', $serviceName)
            }
        } catch {
            if (-not $qualificationFailed) { throw }
            Write-Warning "Could not clean up the test service: $($_.Exception.Message)"
        }
    }
}
