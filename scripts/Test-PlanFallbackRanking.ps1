<#
.SYNOPSIS
    Self-test for Summarize-PlanFallbacks.ps1.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptRoot '..')

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("etl-sql-planfallback-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $inputPath = Join-Path $tempRoot 'input.json'
    $jsonPath = Join-Path $tempRoot 'ranking.json'
    $markdownPath = Join-Path $tempRoot 'ranking.md'

    @'
{
  "workloads": [
    {
      "planFallbacks": [
        {
          "candidatePath": "ColumnarProjection",
          "reasonCode": "UnsupportedExpression",
          "outcome": "Fallback",
          "count": 2,
          "elapsedMs": 10.5,
          "rowCount": 1000,
          "spillBytes": 0,
          "peakWorkingSetMB": 64
        },
        {
          "candidatePath": "ColumnarProjection",
          "reasonCode": "UnsupportedExpression",
          "outcome": "Accepted",
          "count": 99,
          "elapsedMs": 999
        }
      ]
    },
    {
      "planFallbackSummary": "SqlPushdown:ConnectorCapabilityMissing=1; RowPipeline:SemanticGuard=1",
      "elapsedMs": 7,
      "rowCount": 250,
      "spillBytes": 4096,
      "peakWorkingSetMB": 72
    }
  ]
}
'@ | Set-Content -LiteralPath $inputPath -Encoding UTF8

    & (Join-Path $ScriptRoot 'Summarize-PlanFallbacks.ps1') `
        -Path $inputPath `
        -JsonOutput $jsonPath `
        -MarkdownReport $markdownPath | Out-Null

    $rows = @(Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json)
    Assert-True ($rows.Count -eq 3) "Expected three ranked fallback rows."

    $projection = @($rows | Where-Object { $_.CandidatePath -eq 'ColumnarProjection' -and $_.ReasonCode -eq 'UnsupportedExpression' })
    Assert-True ($projection.Count -eq 1) "Expected one ColumnarProjection fallback row."
    Assert-True ([int64]$projection[0].Count -eq 2) "Expected structured fallback count to be preserved."
    Assert-True ([decimal]$projection[0].ObservedElapsedMs -eq 10.5) "Expected structured elapsed cost to be preserved."
    Assert-True ([decimal]$projection[0].ObservedRowsAffected -eq 1000) "Expected structured row count to be preserved."

    $pushdown = @($rows | Where-Object { $_.CandidatePath -eq 'SqlPushdown' -and $_.ReasonCode -eq 'ConnectorCapabilityMissing' })
    Assert-True ($pushdown.Count -eq 1) "Expected one SqlPushdown fallback row."
    Assert-True ([decimal]$pushdown[0].ObservedSpillBytes -eq 4096) "Expected legacy-summary cost context to be preserved."

    $markdown = Get-Content -LiteralPath $markdownPath -Raw
    Assert-True ($markdown.Contains('| ColumnarProjection | UnsupportedExpression | 2 |')) "Expected Markdown report to include structured fallback ranking."

    Write-Host 'Plan fallback ranking self-test passed.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
