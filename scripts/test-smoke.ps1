[CmdletBinding()]
param(
    [ValidateSet("all", "core", "security", "reporting", "portal")]
    [string]$Lane = "all",

    [string]$Configuration = "Debug",

    [switch]$NoRestore,

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ($NoBuild) {
    & node (Join-Path $PSScriptRoot 'sync-assets.js') -Check
    if ($LASTEXITCODE -eq 0) {
        # Current preparation does not make existing binaries current; they keep the assets they were built with.
        & node (Join-Path $PSScriptRoot 'check-built-browser-assets.mjs') --configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Compiled output was built from an older browser asset preparation. Rebuild without -NoBuild." }
    }
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

foreach ($name in $selected) {
    $laneInfo = $lanes[$name]
    Write-Host ""
    Write-Host "==> Smoke lane: $($laneInfo.Label) [$name]"

    foreach ($test in $laneInfo.Tests) {
        $args = @(
            "test",
            (Join-Path $repoRoot $test.Project),
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
