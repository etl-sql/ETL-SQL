$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '../Test-MsiPortalUpgrade.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$null)
$function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-ResourceSnapshot' }, $true)
if ($null -eq $function) { throw 'Missing production checkpoint function.' }
Invoke-Expression $function.Extent.Text
function Get-CimInstance { param($ClassName) [pscustomobject]@{ FreePhysicalMemory = 2048; TotalVisibleMemorySize = 4096 } }
function Get-Process { [pscustomobject]@{ ProcessName = 'fixture'; CPU = 1; WorkingSet64 = 1048576 } }
function Get-Service { param($Name, $ErrorAction) $null }
function Write-MsiDurableCheckpoint { param($Stage, $FreeMemoryMiB, $TotalMemoryMiB) $false }
$summary = [IO.Path]::GetTempFileName()
$previous = $env:GITHUB_ACTIONS
try {
    $env:GITHUB_ACTIONS = 'true'
    $stage = "fresh%0A::error::not-a-command`nnext"
    $lines = @(Write-ResourceSnapshot $stage 6>&1 | ForEach-Object { $_.ToString() })
    if ($lines.Count -ne 1 -or -not $lines[0].StartsWith('::notice title=MSI checkpoint::')) { throw 'Expected one checkpoint notice.' }
    if ($lines[0].Contains("`n") -or $lines[0].Contains("`r") -or -not $lines[0].Contains('%250A')) { throw 'Unsafe workflow-command data.' }
    $payload = $lines[0].Substring('::notice title=MSI checkpoint::'.Length).Replace('%0D', "`r").Replace('%0A', "`n").Replace('%25', '%') | ConvertFrom-Json
    if ($payload.stage -cne $stage -or $payload.freeMemoryMiB -ne 2 -or $payload.totalMemoryMiB -ne 4) { throw 'Checkpoint fields changed.' }
    $env:GITHUB_ACTIONS = 'false'
    if (@(Write-ResourceSnapshot 'local check' 6>&1).Count -ne 0) { throw 'A local run emitted a GitHub command.' }
    if (-not (Get-Content -LiteralPath $summary -Raw).Contains('free memory')) { throw 'Local resource diagnostics were lost.' }
    Write-Host 'MSI checkpoint: production function emits bounded escaped hosted diagnostics and keeps local runs independent.'
} finally {
    $env:GITHUB_ACTIONS = $previous
    Remove-Item -LiteralPath $summary -Force
}

. (Join-Path $PSScriptRoot '../MsiCheckpoint.Helpers.ps1')
$checkpointEnvironment = @{
    GITHUB_ACTIONS = 'true'; ETLSQL_MSI_DURABLE_CHECKPOINTS = '1'; GH_TOKEN = 'fixture-only-not-a-token'
    PROFILE = 'portal'; GITHUB_REPOSITORY = 'owner/repo'; GITHUB_RUN_ID = '123'; GITHUB_RUN_ATTEMPT = '2'
    CERTIFICATION_SHA = ('a' * 40); ETLSQL_MSI_CHECKPOINT_RUN_SHA = ('b' * 40)
}
$priorCheckpointEnvironment = @{}
foreach ($name in $checkpointEnvironment.Keys) {
    $priorCheckpointEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $checkpointEnvironment[$name])
}
$script:checkpointRequests = [Collections.Generic.List[object]]::new()
$script:checkpointMode = 'normal'
$request = {
    param($Method, $Route, $Body)
    $script:checkpointRequests.Add(@{ method = $Method; route = $Route; body = $Body })
    if ($script:checkpointMode -eq 'transport-failure') { throw 'Fixture response must never appear in diagnostics.' }
    if ($Method -eq 'PATCH') {
        if ($script:checkpointMode -eq 'patch-failure') { throw 'Fixture patch rejected.' }
        return @{}
    }
    $row = [pscustomobject]@{
        name = 'MSI certification (portal)'; status = 'in_progress'; run_id = 123; run_attempt = 2
        head_sha = ('b' * 40); check_run_url = 'https://api.github.com/repos/owner/repo/check-runs/456'
    }
    switch ($script:checkpointMode) {
        'wrong-head' { $row.head_sha = 'c' * 40 }
        'wrong-run' { $row.run_id = 124 }
        'wrong-attempt' { $row.run_attempt = 1 }
        'wrong-profile' { $row.name = 'MSI certification (workstation)' }
        'completed' { $row.status = 'completed' }
        'foreign-check' { $row.check_run_url = 'https://api.github.com/repos/other/repo/check-runs/456' }
        'foreign-host' { $row.check_run_url = 'https://example.invalid/repos/owner/repo/check-runs/456' }
        'reusable-name' { $row.name = 'certify-msi / MSI certification (portal)' }
    }
    $rows = @(if ($script:checkpointMode -eq 'duplicate') { $row; $row } else { $row })
    return @{ total_count = $(if ($script:checkpointMode -eq 'truncated') { 2 } else { $rows.Count }); jobs = $rows }
}
try {
    if (-not (Write-MsiDurableCheckpoint -Stage 'fresh backup start' -Request $request)) { throw 'Valid checkpoint was not delivered.' }
    if (-not (Write-MsiDurableCheckpoint -Stage 'fresh backup complete' -Request $request)) { throw 'Cached checkpoint was not delivered.' }
    if ($script:checkpointRequests.Count -ne 3) { throw 'Current job lookup was not cached.' }
    foreach ($entry in @($script:checkpointRequests | Where-Object method -eq 'PATCH')) {
        if ($entry.route -cne 'repos/owner/repo/check-runs/456' -or $entry.body.Keys.Count -ne 1 -or -not $entry.body.ContainsKey('output')) { throw 'Checkpoint could modify a check result.' }
        $annotation = $entry.body.output.annotations[0]
        $payload = $annotation.message | ConvertFrom-Json
        if ($annotation.annotation_level -cne 'notice' -or $payload.scope -cne 'diagnostic-only' -or
            $payload.candidate -cne ('a' * 40) -or $payload.runHead -cne ('b' * 40)) { throw 'Checkpoint lost candidate/run scope.' }
    }
    if (($script:checkpointRequests | ConvertTo-Json -Depth 10).Contains($env:GH_TOKEN)) { throw 'Checkpoint exposed authentication.' }
    if (-not (Write-MsiDurableCheckpoint -Stage 'fresh backup start' -FreeMemoryMiB 1024 -TotalMemoryMiB 8192 -Request $request)) { throw 'Resource checkpoint was not delivered.' }
    $resource = $script:checkpointRequests[$script:checkpointRequests.Count - 1].body.output.annotations[0].message | ConvertFrom-Json
    if ($resource.freeMemoryMiB -ne 1024 -or $resource.totalMemoryMiB -ne 8192) { throw 'Resource checkpoint changed.' }
    foreach ($resources in @(@{ FreeMemoryMiB = 1 }, @{ FreeMemoryMiB = -1; TotalMemoryMiB = 10 }, @{ FreeMemoryMiB = 11; TotalMemoryMiB = 10 })) {
        $before = $script:checkpointRequests.Count
        if ((Write-MsiDurableCheckpoint -Stage 'fresh backup start' @resources -Request $request) -or $script:checkpointRequests.Count -ne $before) { throw 'Invalid resource fields reached transport.' }
    }
    foreach ($mode in @('wrong-head', 'wrong-run', 'wrong-attempt', 'wrong-profile', 'completed', 'foreign-check', 'foreign-host', 'duplicate', 'truncated', 'transport-failure', 'patch-failure')) {
        $script:checkpointMode = $mode; $script:MsiDurableCheckpointTarget = $null
        if (Write-MsiDurableCheckpoint -Stage 'supervisor starting' -Request $request) { throw "Invalid checkpoint accepted: $mode" }
    }
    $script:checkpointMode = 'reusable-name'; $script:MsiDurableCheckpointTarget = $null
    if (-not (Write-MsiDurableCheckpoint -Stage '== upgrade to 1.2.3' -Request $request)) { throw 'Reusable job identity was rejected.' }
    foreach ($stage in @('unknown phase', "fresh backup start`n::error::injected", 'fresh backup start SECRET:fixture')) {
        $before = $script:checkpointRequests.Count
        if ((Write-MsiDurableCheckpoint -Stage $stage -Request $request) -or $script:checkpointRequests.Count -ne $before) { throw 'Unsafe stage reached transport.' }
    }
    foreach ($name in @('GITHUB_ACTIONS', 'ETLSQL_MSI_DURABLE_CHECKPOINTS', 'GH_TOKEN', 'PROFILE', 'GITHUB_REPOSITORY', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'CERTIFICATION_SHA', 'ETLSQL_MSI_CHECKPOINT_RUN_SHA')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, '')
        $before = $script:checkpointRequests.Count
        if ((Write-MsiDurableCheckpoint -Stage 'supervisor starting' -Request $request) -or $script:checkpointRequests.Count -ne $before) { throw "Missing context reached transport: $name" }
        [Environment]::SetEnvironmentVariable($name, $value)
    }
    Add-Type -TypeDefinition @'
#nullable enable
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace ETL_SQL.CheckpointFixtures;
public sealed class HttpHandler(string mode) : HttpMessageHandler
{
    public int Requests { get; private set; }
    public string? Url { get; private set; }
    public bool HasBearer { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        Url = request.RequestUri?.AbsoluteUri;
        HasBearer = request.Headers.Authorization?.Scheme == "Bearer";
        if (mode == "timeout") await Task.Delay(Timeout.Infinite, cancellationToken);
        if (mode == "unresponsive") await Task.Delay(20000);
        return new HttpResponseMessage(mode == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK)
        {
            Content = new StringContent(mode == "oversized" ? new string('x', 2 * 1024 * 1024 + 1) : "{\"ok\":true}")
        };
    }
}
'@
    foreach ($mode in @('success', 'redirect', 'oversized', 'timeout', 'unresponsive')) {
        $handler = [ETL_SQL.CheckpointFixtures.HttpHandler]::new($mode)
        $failed = $false; $clock = [Diagnostics.Stopwatch]::StartNew()
        try { $response = Send-MsiCheckpointRequest -Method GET -Route 'repos/owner/repo/check-runs/456' -Handler $handler } catch { $failed = $true }
        if ($failed -ne ($mode -ne 'success')) { throw "HTTP checkpoint outcome changed: $mode" }
        if ($mode -eq 'success' -and -not $response.ok) { throw 'Valid HTTP checkpoint response was lost.' }
        if ($handler.Requests -ne 1 -or -not $handler.HasBearer -or $handler.Url -cne 'https://api.github.com/repos/owner/repo/check-runs/456') { throw 'HTTP checkpoint origin/authentication changed.' }
        if ($mode -in @('timeout', 'unresponsive') -and $clock.Elapsed.TotalSeconds -gt 10) { throw 'Checkpoint transport ignored cancellation.' }
    }
    Write-Host 'MSI durable checkpoints: append-only output, cached current-job identity, PR merge/run scope, safe stages and bounded-transport failures verified without GitHub writes.'
} finally {
    foreach ($name in $priorCheckpointEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $priorCheckpointEnvironment[$name]) }
    $script:MsiDurableCheckpointTarget = $null
}
