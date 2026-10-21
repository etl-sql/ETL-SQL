# Only these documentation surfaces are known not to change the installed payload.
# Runtime help under docs/reference and other unlisted paths require certification.
function Get-MsiUpgradeImpact {
    param([AllowEmptyCollection()][string[]]$ChangedPaths = @())

    $documentationOnly = @(
        '^(TODO|ROADMAP|CHANGELOG|AGENTS|CLAUDE|GEMINI)\.md$',
        '^docs/(releases|architecture)/.+\.md$'
    )
    $relevant = @($ChangedPaths | Where-Object {
        $candidatePath = $_
        -not ($documentationOnly | Where-Object { $candidatePath -cmatch $_ })
    })
    [pscustomobject]@{
        ShouldRun = $relevant.Count -gt 0
        RelevantPaths = $relevant
    }
}
