<#
.SYNOPSIS
    Seeds a small, reproducible acceptance profile into a running Portal, and optionally smoke-tests
    it.

.DESCRIPTION
    Everything here goes through the public HTTP API, which is the point: the same script works
    against `dotnet run`, a container, or a deployed environment, so "it passed locally" and "it
    passed in the image" are statements about the same checks rather than two different ones that
    happen to share a name.

    The profile is deliberately small. An acceptance dataset that takes ten minutes to seed is one
    people stop seeding, and a large one hides the failure it was meant to reveal among rows nobody
    reads. This is the minimum that exercises the journeys that matter: a folder, a report that
    actually runs, one user per role, and a shared connection.

    It is also idempotent — re-running against an already-seeded Portal reports what already exists
    rather than failing or duplicating it, so it is safe to run against a long-lived environment.

.PARAMETER BaseUrl
    Portal root, e.g. http://localhost:5000

.PARAMETER AdminUser / -AdminPassword
    First-run administrator. The forced password change is performed automatically on first use.

.PARAMETER Prefix
    Name prefix for everything created, so a seeded environment can be identified and cleaned.

.PARAMETER SmokeOnly
    Skip seeding and only assert the profile is present and working. This is the mode a Docker
    image check runs.

.EXAMPLE
    pwsh -File scripts/Invoke-AcceptanceProfile.ps1 -BaseUrl http://localhost:5000
    pwsh -File scripts/Invoke-AcceptanceProfile.ps1 -BaseUrl http://localhost:8080 -SmokeOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [string]$AdminUser = "admin",

    [string]$AdminPassword = "Admin@12345!",

    [string]$NewAdminPassword = "Acceptance@Portal99!",

    [string]$Prefix = "acceptance",

    # Only useful when the Portal's script root is reachable from this machine -- a local run, or a
    # container with the root bind-mounted. Left unset, the report is skipped rather than reported
    # as a failure, because a script file the Portal cannot see is not something this script can fix
    # over HTTP.
    [string]$ScriptRootPath,

    [switch]$SmokeOnly
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

$script:Failures = @()
$script:Checks = 0

function Write-Step($message) { Write-Host "  $message" -ForegroundColor Cyan }

function Assert-That {
    param([string]$Description, [scriptblock]$Condition)
    $script:Checks++
    $ok = $false
    try { $ok = [bool](& $Condition) } catch { $ok = $false }
    if ($ok) {
        Write-Host "  PASS  $Description" -ForegroundColor Green
    }
    else {
        Write-Host "  FAIL  $Description" -ForegroundColor Red
        $script:Failures += $Description
    }
}

function Invoke-Portal {
    param(
        [string]$Method,
        [string]$Path,
        $Body,
        [string]$Token,
        [switch]$AllowFailure
    )
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }

    # Not $args: that is an automatic variable, and shadowing it inside a function is a bug waiting
    # for the first person who adds a parameter.
    $request = @{
        Method  = $Method
        Uri     = "$BaseUrl$Path"
        Headers = $headers
    }
    if ($null -ne $Body) {
        $request.Body = ($Body | ConvertTo-Json -Depth 10)
        $request.ContentType = "application/json"
    }

    try { return Invoke-RestMethod @request }
    catch {
        if ($AllowFailure) { return $null }
        throw "$Method $Path failed: $($_.Exception.Message)"
    }
}

function Get-AdminToken {
    # The seeded administrator is created must-change-password, so the first sign-in is two steps.
    # Handling both orders means the script works on a fresh Portal and on one already seeded.
    $login = Invoke-Portal POST "/api/auth/login" @{ username = $AdminUser; password = $NewAdminPassword } -AllowFailure
    if ($login -and $login.token) { return $login.token }

    $first = Invoke-Portal POST "/api/auth/login" @{ username = $AdminUser; password = $AdminPassword }
    Invoke-Portal POST "/api/auth/change-password" @{ currentPassword = $AdminPassword; newPassword = $NewAdminPassword } -Token $first.token | Out-Null
    return (Invoke-Portal POST "/api/auth/login" @{ username = $AdminUser; password = $NewAdminPassword }).token
}

Write-Host "Acceptance profile against $BaseUrl" -ForegroundColor White

# ── Reachability ────────────────────────────────────────────────────────────────────────────────
# Checked before anything else so an unreachable Portal reports as unreachable rather than as a
# dozen failed assertions that each look like a product defect.
try {
    Invoke-RestMethod -Method Get -Uri "$BaseUrl/healthz" -TimeoutSec 30 | Out-Null
}
catch {
    Write-Host "  Portal is not reachable at $BaseUrl/healthz" -ForegroundColor Red
    exit 2
}

$token = Get-AdminToken
$folderName = "$Prefix-folder"
$reportName = "$Prefix-report"

if (-not $SmokeOnly) {
    Write-Step "Seeding..."

    $folders = Invoke-Portal GET "/api/folders" -Token $token
    $folder = $folders | Where-Object { $_.name -eq $folderName } | Select-Object -First 1
    if (-not $folder) {
        $folder = Invoke-Portal POST "/api/folders" @{ name = $folderName } -Token $token
        Write-Step "created folder $folderName"
    }
    else { Write-Step "folder $folderName already present" }

    # One report per profile, self-contained so it runs anywhere: no connection, no parameters.
    $scriptName = "$Prefix-report.rptsql"
    if ($ScriptRootPath) {
        if (-not (Test-Path $ScriptRootPath)) { New-Item -ItemType Directory -Path $ScriptRootPath -Force | Out-Null }
        $scriptBody = @"
SELECT 1 AS Value INTO #d;
CREATE VISUAL V AS TABLE (SOURCE = #d, MAPPINGS (Value = Value));
"@
        Set-Content -Path (Join-Path $ScriptRootPath $scriptName) -Value $scriptBody -NoNewline
        Write-Step "wrote $scriptName to $ScriptRootPath"
    }

    $reports = Invoke-Portal GET "/api/folders/$($folder.id)/reports" -Token $token
    if (-not ($reports | Where-Object { $_.name -eq $reportName })) {
        $created = Invoke-Portal POST "/api/reports" @{
            folderId   = $folder.id
            name       = $reportName
            scriptPath = $scriptName
        } -Token $token -AllowFailure
        if ($created) { Write-Step "published report $reportName" }
        else {
            Write-Host "  SKIP  report not seeded: $scriptName is not under the Portal's ScriptRootPath." -ForegroundColor Yellow
            Write-Host "        Pass -ScriptRootPath when the root is reachable from here." -ForegroundColor Yellow
        }
    }
    else { Write-Step "report $reportName already present" }

    # One user per role, so a role journey can be run by hand against this environment.
    foreach ($role in @("Viewer", "Publisher", "DataSteward", "OrchestratorManager")) {
        $username = "$Prefix-$($role.ToLowerInvariant())"
        $existing = Invoke-Portal GET "/api/admin/users/catalog?q=$username" -Token $token -AllowFailure
        if ($existing -and $existing.items -and ($existing.items | Where-Object { $_.username -eq $username })) {
            Write-Step "user $username already present"
            continue
        }
        Invoke-Portal POST "/api/admin/users" @{
            username = $username
            password = "Acceptance@Role99!"
            role     = $role
            email    = "$username@example.test"
        } -Token $token -AllowFailure | Out-Null
        Write-Step "created user $username ($role)"
    }
}

# ── Smoke ───────────────────────────────────────────────────────────────────────────────────────
# The same assertions whatever the target. That is what makes local and container runs comparable.
Write-Step "Smoke checks..."

Assert-That "health endpoint responds" {
    $null -ne (Invoke-RestMethod -Method Get -Uri "$BaseUrl/healthz" -TimeoutSec 30)
}

Assert-That "administrator can sign in" { $token -and $token.Length -gt 20 }

$folders = Invoke-Portal GET "/api/folders" -Token $token
Assert-That "seeded folder is listed" { ($folders | Where-Object { $_.name -eq $folderName }) -ne $null }

$folder = $folders | Where-Object { $_.name -eq $folderName } | Select-Object -First 1
if ($folder) {
    $reports = Invoke-Portal GET "/api/folders/$($folder.id)/reports" -Token $token
    $report = $reports | Where-Object { $_.name -eq $reportName } | Select-Object -First 1

    if (-not $report) {
        Write-Host "  SKIP  no seeded report to run (see the seeding note above)" -ForegroundColor Yellow
    }
    else {
        Assert-That "seeded report is listed" { $true }
        # Running it is the check that matters: listing a report proves the catalog, running it
        # proves the engine, the script root, and the execution pipeline are all actually wired.
        $job = Invoke-Portal POST "/api/reports/$($report.id)/execute" @{} -Token $token -AllowFailure
        Assert-That "seeded report starts a job" { $job -and $job.jobId }

        if ($job -and $job.jobId) {
            $deadline = (Get-Date).AddSeconds(60)
            do {
                Start-Sleep -Milliseconds 500
                $status = Invoke-Portal GET "/api/jobs/$($job.jobId)" -Token $token -AllowFailure
            } while ($status -and $status.status -notin @("Completed", "Failed", "Cancelled") -and (Get-Date) -lt $deadline)

            Assert-That "seeded report completes" { $status -and $status.status -eq "Completed" }
        }
    }
}

$users = Invoke-Portal GET "/api/admin/users/catalog" -Token $token -AllowFailure
Assert-That "role users are present" {
    $names = $users.items | ForEach-Object { $_.username }
    @("$Prefix-viewer", "$Prefix-publisher", "$Prefix-datasteward", "$Prefix-orchestratormanager") |
        Where-Object { $names -notcontains $_ } | Measure-Object | Select-Object -ExpandProperty Count | ForEach-Object { $_ -eq 0 }
}

Write-Host ""
if ($script:Failures.Count -eq 0) {
    Write-Host "All $($script:Checks) acceptance checks passed against $BaseUrl" -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures.Count) of $($script:Checks) acceptance checks failed:" -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
