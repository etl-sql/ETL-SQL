[CmdletBinding()]
param(
    [ValidateSet("all", "core", "security", "reporting", "portal")]
    [string]$Lane = "all",

    [string]$Configuration = "Debug",

    [switch]$NoRestore,

    [switch]$NoBuild,

    [ValidateSet('all', 'engine', 'portal')]
    [string]$ProjectGroup = 'all'
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ($NoBuild) {
    & node (Join-Path $PSScriptRoot 'sync-assets.js') -Check
} else {
    & node (Join-Path $PSScriptRoot 'sync-assets.js')
}
if ($LASTEXITCODE -ne 0) { throw "Browser asset preparation failed. Run npm ci --prefix scripts/typecheck, then node scripts/sync-assets.js." }

$lanes = [ordered]@{
    core = @{
        Label = "Core language behavior"
        Tests = @(
            @{
                Project = "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj"
                Filter = "Category=Smoke.Core"
            }
        )
    }
    security = @{
        Label = "Security and path guardrails"
        Tests = @(
            @{
                Project = "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj"
                Filter = "Category=Smoke.Security"
            },
            @{
                Project = "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj"
                Filter = "Category=Smoke.Security"
            }
        )
    }
    reporting = @{
        Label = "Reporting manifest and runtime behavior"
        Tests = @(
            @{
                Project = "tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj"
                Filter = "Category=Smoke.Reporting"
            }
        )
    }
    portal = @{
        Label = "Portal publish, execute, and snapshot basics"
        Tests = @(
            @{
                Project = "tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj"
                Filter = "Category=Smoke.Portal"
            }
        )
    }
}

$selected = if ($Lane -eq "all") { @("core", "security", "reporting", "portal") } else { @($Lane) }
$executedSuites = 0

foreach ($name in $selected) {
    $laneInfo = $lanes[$name]
    Write-Host ""
    Write-Host "==> Smoke lane: $($laneInfo.Label) [$name]"

    foreach ($test in $laneInfo.Tests) {
        $isPortal = $test.Project -like '*ETL-SQL.Portal.Tests*'
        if (($ProjectGroup -eq 'engine' -and $isPortal) -or ($ProjectGroup -eq 'portal' -and -not $isPortal)) { continue }
        $executedSuites++
        $projectPath = Join-Path $repoRoot $test.Project
        if ($NoBuild) {
            & node (Join-Path $PSScriptRoot 'check-built-browser-assets.mjs') --configuration $Configuration --project $projectPath
            if ($LASTEXITCODE -ne 0) { throw 'Selected project has missing, stale or unverifiable browser assets. Rebuild without -NoBuild.' }
        }
        $args = @(
            "test",
            $projectPath,
            "--configuration", $Configuration,
            "--filter", $test.Filter,
            "--logger", "console;verbosity=minimal"
        )

        if ($NoRestore) { $args += "--no-restore" }
        if ($NoBuild) { $args += "--no-build" }

        & dotnet @args
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}
if ($executedSuites -eq 0) { throw 'The selected smoke lane and project group contain no test suites.' }
