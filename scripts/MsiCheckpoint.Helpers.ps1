# Send diagnostic progress directly to the job's check run; queued runner notices can be lost.
$script:MsiDurableCheckpointTarget = $null
function Send-MsiCheckpointRequest {
    param(
        [ValidateSet('GET', 'PATCH')][string]$Method,
        [ValidatePattern('^repos/[\w.-]+/[\w.-]+/(actions/runs/[1-9]\d*/jobs\?filter=latest&per_page=100|check-runs/[1-9]\d*)$')][string]$Route,
        [object]$Body,
        [Net.Http.HttpMessageHandler]$Handler
    )
    $handler = $Handler
    if ($null -eq $handler) {
        $handler = [Net.Http.HttpClientHandler]::new()
        $handler.AllowAutoRedirect = $false
    }
    $client = [Net.Http.HttpClient]::new($handler)
    $cancel = [Threading.CancellationTokenSource]::new([timespan]::FromSeconds(3))
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), "https://api.github.com/$Route")
    $response = $null; $stream = $null; $memory = [IO.MemoryStream]::new()
    try {
        $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $env:GH_TOKEN)
        $request.Headers.UserAgent.ParseAdd('ETL-SQL-release-checkpoint')
        $request.Headers.Accept.ParseAdd('application/vnd.github+json')
        $request.Headers.Add('X-GitHub-Api-Version', '2026-03-10')
        if ($null -ne $Body) {
            $request.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 8 -Compress), [Text.Encoding]::UTF8, 'application/json')
        }
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cancel.Token).WaitAsync($cancel.Token).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw 'Checkpoint request did not succeed.' }
        $stream = $response.Content.ReadAsStreamAsync($cancel.Token).WaitAsync($cancel.Token).GetAwaiter().GetResult()
        $buffer = [byte[]]::new(65536)
        while (($read = $stream.ReadAsync($buffer, 0, $buffer.Length, $cancel.Token).WaitAsync($cancel.Token).GetAwaiter().GetResult()) -gt 0) {
            if ($memory.Length + $read -gt 2MB) { throw 'Checkpoint response exceeds its bound.' }
            $memory.Write($buffer, 0, $read)
        }
        [Text.Encoding]::UTF8.GetString($memory.ToArray()) | ConvertFrom-Json -ErrorAction Stop
    } finally {
        if ($stream) { $stream.Dispose() }
        if ($response) { $response.Dispose() }
        $request.Dispose(); $memory.Dispose(); $cancel.Dispose(); $client.Dispose()
    }
}

function Write-MsiDurableCheckpoint {
    param(
        [string]$Stage,
        [Nullable[long]]$FreeMemoryMiB,
        [Nullable[long]]$TotalMemoryMiB,
        [scriptblock]$Request = { param($Method, $Route, $Body) Send-MsiCheckpointRequest -Method $Method -Route $Route -Body $Body }
    )
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:ETLSQL_MSI_DURABLE_CHECKPOINTS -cne '1' -or
        [string]::IsNullOrWhiteSpace($env:GH_TOKEN) -or $env:PROFILE -cnotin @('portal', 'workstation') -or
        $env:GITHUB_REPOSITORY -cnotmatch '^[\w.-]+/[\w.-]+$' -or
        @($env:GITHUB_REPOSITORY -split '/' | Where-Object { $_ -in @('.', '..') }).Count -gt 0 -or
        $env:GITHUB_RUN_ID -cnotmatch '^[1-9]\d*$' -or $env:GITHUB_RUN_ATTEMPT -cnotmatch '^[1-9]\d*$' -or
        $env:CERTIFICATION_SHA -cnotmatch '^[a-f0-9]{40}$' -or $env:ETLSQL_MSI_CHECKPOINT_RUN_SHA -cnotmatch '^[a-f0-9]{40}$' -or
        $null -eq $Stage -or $Stage.Length -gt 128 -or
        $Stage -cnotmatch '^(?:supervisor starting|fresh backup (?:start|complete)|== (?:fresh install of|install previous|upgrade to|roll back to) (?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))$') {
        return $false
    }
    if ($null -ne $FreeMemoryMiB -or $null -ne $TotalMemoryMiB) {
        if ($null -eq $FreeMemoryMiB -or $null -eq $TotalMemoryMiB -or $FreeMemoryMiB -lt 0 -or
            $TotalMemoryMiB -lt 1 -or $FreeMemoryMiB -gt $TotalMemoryMiB) { return $false }
    }
    try {
        $key = "$env:GITHUB_REPOSITORY/$env:GITHUB_RUN_ID/$env:GITHUB_RUN_ATTEMPT/$env:PROFILE/$env:CERTIFICATION_SHA/$env:ETLSQL_MSI_CHECKPOINT_RUN_SHA"
        if (-not $script:MsiDurableCheckpointTarget -or $script:MsiDurableCheckpointTarget.key -cne $key) {
            $listing = & $Request 'GET' "repos/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID/jobs?filter=latest&per_page=100" $null
            if ($listing.jobs -isnot [array] -or $listing.total_count -ne $listing.jobs.Count) { return $false }
            $name = "MSI certification ($env:PROFILE)"
            $matches = @($listing.jobs | Where-Object {
                    ($_.name -ceq $name -or $_.name.EndsWith(" / $name", [StringComparison]::Ordinal)) -and
                    $_.status -ceq 'in_progress' -and [string]$_.run_id -ceq $env:GITHUB_RUN_ID -and
                    [string]$_.run_attempt -ceq $env:GITHUB_RUN_ATTEMPT -and $_.head_sha -ceq $env:ETLSQL_MSI_CHECKPOINT_RUN_SHA
                })
            if ($matches.Count -ne 1) { return $false }
            $prefix = "https://api.github.com/repos/$env:GITHUB_REPOSITORY/check-runs/"
            $url = [string]$matches[0].check_run_url
            if (-not $url.StartsWith($prefix, [StringComparison]::Ordinal) -or $url.Substring($prefix.Length) -cnotmatch '^[1-9]\d*$') { return $false }
            $script:MsiDurableCheckpointTarget = @{ key = $key; route = "repos/$env:GITHUB_REPOSITORY/check-runs/$($url.Substring($prefix.Length))" }
        }
        $record = [ordered]@{
            schemaVersion = 1; scope = 'diagnostic-only'; stage = $Stage; utc = [datetimeoffset]::UtcNow.ToString('o')
            candidate = $env:CERTIFICATION_SHA; runHead = $env:ETLSQL_MSI_CHECKPOINT_RUN_SHA
            run = $env:GITHUB_RUN_ID; attempt = $env:GITHUB_RUN_ATTEMPT; profile = $env:PROFILE
        }
        if ($null -ne $FreeMemoryMiB) {
            $record.freeMemoryMiB = $FreeMemoryMiB
            $record.totalMemoryMiB = $TotalMemoryMiB
        }
        $message = $record | ConvertTo-Json -Compress
        # Output-only updates append annotations and never change the check's status or conclusion.
        $body = @{ output = @{
                title = 'MSI diagnostic checkpoint'; summary = 'Diagnostic progress only; certification results are separate.'
                annotations = @(@{ path = '.github/workflows/msi-upgrade.yml'; start_line = 1; end_line = 1
                        annotation_level = 'notice'; title = 'MSI checkpoint'; message = $message })
            } }
        $null = & $Request 'PATCH' $script:MsiDurableCheckpointTarget.route $body
        return $true
    } catch {
        # Diagnostic transport cannot change the installer result or expose an HTTP response/token.
        return $false
    }
}
