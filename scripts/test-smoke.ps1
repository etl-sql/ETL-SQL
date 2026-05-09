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
                Project = "tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj"
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
        Label = "Report Portal publish, execute, and snapshot basics"
        Tests = @(
            @{
                Project = "tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj"
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
