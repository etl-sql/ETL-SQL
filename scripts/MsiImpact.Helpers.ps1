# MSI runs for shared package inputs and explicitly selected installer certification scripts.
function Get-MsiUpgradeImpact {
    param(
        [AllowEmptyCollection()][string[]]$ChangedPaths = @(),
        [string]$ManifestPath = (Join-Path $PSScriptRoot 'ci-impact.json')
    )

    $policy = Get-Content -LiteralPath $ManifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ($policy.schemaVersion -ne 1 -or $policy.rules -isnot [array] -or $policy.rules.Count -eq 0 -or
        $policy.lanes -isnot [array] -or $policy.lanes.Count -eq 0 -or $policy.always -isnot [array] -or
        @($policy.lanes | Select-Object -Unique).Count -ne $policy.lanes.Count -or
        @($policy.lanes | Where-Object { $_ -isnot [string] -or $_ -cnotmatch '^[a-z][a-z0-9-]*$' }).Count -gt 0 -or
        @($policy.always | Where-Object { $_ -cnotin $policy.lanes }).Count -gt 0) {
        throw 'Invalid package-impact manifest.'
    }
    foreach ($rule in $policy.rules) {
        $allLanes = $rule.lanes -is [string] -and $rule.lanes -ceq '*'
        if (-not $rule.name -or $rule.pattern -isnot [string] -or -not $rule.pattern.StartsWith('^') -or
            $rule.nativePackages -isnot [bool] -or ($allLanes -and -not $rule.nativePackages) -or
            (-not $allLanes -and ($rule.lanes -isnot [array] -or @($rule.lanes | Where-Object { $_ -cnotin $policy.lanes }).Count -gt 0))) {
            throw 'Invalid package-impact rule.'
        }
        $null = [regex]::new($rule.pattern)
    }
    $relevant = @($ChangedPaths | Where-Object {
        $candidatePath = $_
        $rule = $policy.rules | Where-Object { $candidatePath -cmatch $_.pattern } | Select-Object -First 1
        $null -eq $rule -or $rule.nativePackages -or $rule.lanes -ccontains 'msi-upgrade'
    })
    [pscustomobject]@{
        ShouldRun = $relevant.Count -gt 0
        RelevantPaths = $relevant
    }
}
