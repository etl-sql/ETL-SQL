using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ETL_SQL.Core.Hosted;

public sealed record SubmitRunResult(
    bool Accepted,
    int StatusCode,
    PipelineRunSubmittedDto? Receipt = null,
    PipelineRunRefusalDto? Refusal = null);

public enum LookupByRequestKind
{
    Found,
    NotFound,
    Unknown,
    Refused
}

public sealed record LookupByRequestResult(
    LookupByRequestKind Kind,
    int StatusCode,
    PipelineRunSubmittedDto? Receipt = null,
    PipelineRunRefusalDto? Refusal = null);

public sealed record GetRunResult(
    bool Ok,
    int StatusCode,
    PipelineRunDto? Run = null,
    PipelineRunRefusalDto? Refusal = null);

public enum CancelRunKind
{
    Accepted,
    AlreadyFinished,
    Denied,
    Refused
}

public sealed record CancelRunResult(
    CancelRunKind Kind,
    int StatusCode,
    PipelineRunCancelRequestedDto? Receipt = null,
    PipelineRunRefusalDto? Refusal = null);

/// <summary>
/// Typed HTTP client for the live ST56 Portal pipeline discovery, submission, recovery,
/// polling, and cancellation endpoints. Shared by ETL-SQL TUI (ST57) and Desktop Studio (ST58).
/// </summary>
public sealed class HostedPipelineClient(HostedClientSessionService sessions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RunnablePipelinePageDto> GetRunnablePipelinesAsync(
        string? query = null,
        int afterId = 0,
        CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
            qs.Add($"query={Uri.EscapeDataString(query.Trim())}");
        if (afterId > 0)
            qs.Add($"afterId={afterId}");
        var path = qs.Count > 0 ? $"/api/studio/pipelines/runnable?{string.Join("&", qs)}" : "/api/studio/pipelines/runnable";

        using var resp = await sessions.AuthorizedSendAsync(
            origin => new HttpRequestMessage(HttpMethod.Get, origin + path), ct);

        if (!resp.IsSuccessStatusCode)
        {
            var refusal = await ReadRefusalAsync(resp, ct);
            throw new InvalidOperationException(refusal.Error);
        }

        return await resp.Content.ReadFromJsonAsync<RunnablePipelinePageDto>(JsonOptions, ct)
            ?? new RunnablePipelinePageDto([], null);
    }

    public async Task<RunnablePipelineDto> GetRunnableRevisionAsync(int pipelineId, CancellationToken ct = default)
    {
        using var resp = await sessions.AuthorizedSendAsync(
            origin => new HttpRequestMessage(HttpMethod.Get, $"{origin}/api/studio/pipelines/{pipelineId}/revision"), ct);

        if (!resp.IsSuccessStatusCode)
        {
            var refusal = await ReadRefusalAsync(resp, ct);
            throw new InvalidOperationException(refusal.Error);
        }

        return await resp.Content.ReadFromJsonAsync<RunnablePipelineDto>(JsonOptions, ct)
            ?? throw new HostedNetworkException($"Portal returned an empty revision for pipeline #{pipelineId}.");
    }

    public async Task<SubmitRunResult> SubmitPipelineRunAsync(
        int pipelineId,
        long version,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        using var resp = await sessions.AuthorizedSendAsync(origin =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{origin}/api/studio/pipelines/{pipelineId}/runs")
            {
                Content = JsonContent.Create(new { version }, options: JsonOptions)
            };
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            return req;
        }, ct);

        if (resp.StatusCode == HttpStatusCode.Accepted)
        {
            var receipt = await ReadValidatedReceiptAsync(resp, pipelineId, version, ct);
            return new SubmitRunResult(true, (int)resp.StatusCode, Receipt: receipt);
        }

        return new SubmitRunResult(false, (int)resp.StatusCode, Refusal: await ReadRefusalAsync(resp, ct));
    }

    public async Task<LookupByRequestResult> GetSubmissionByRequestAsync(
        int pipelineId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        using var resp = await sessions.AuthorizedSendAsync(
            origin => new HttpRequestMessage(
                HttpMethod.Get,
                $"{origin}/api/studio/pipelines/{pipelineId}/runs/by-request/{Uri.EscapeDataString(idempotencyKey)}"),
            ct);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var receipt = await ReadValidatedReceiptAsync(resp, pipelineId, expectedVersion: null, ct);
            return new LookupByRequestResult(LookupByRequestKind.Found, (int)resp.StatusCode, Receipt: receipt);
        }

        var refusal = await ReadRefusalAsync(resp, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound && string.Equals(refusal.Code, "submission_not_found", StringComparison.Ordinal))
            return new LookupByRequestResult(LookupByRequestKind.NotFound, (int)resp.StatusCode, Refusal: refusal);
        if (resp.StatusCode == HttpStatusCode.Conflict && string.Equals(refusal.Code, "submission_outcome_unknown", StringComparison.Ordinal))
            return new LookupByRequestResult(LookupByRequestKind.Unknown, (int)resp.StatusCode, Refusal: refusal);

        return new LookupByRequestResult(LookupByRequestKind.Refused, (int)resp.StatusCode, Refusal: refusal);
    }

    public async Task<GetRunResult> GetPipelineRunAsync(int pipelineId, long runId, CancellationToken ct = default)
    {
        using var resp = await sessions.AuthorizedSendAsync(
            origin => new HttpRequestMessage(HttpMethod.Get, $"{origin}/api/studio/pipelines/{pipelineId}/runs/{runId}"),
            ct);

        if (resp.IsSuccessStatusCode)
        {
            var run = await resp.Content.ReadFromJsonAsync<PipelineRunDto>(JsonOptions, ct)
                ?? throw new HostedNetworkException($"Portal returned an empty status payload for run #{runId}.");
            return new GetRunResult(true, (int)resp.StatusCode, Run: run);
        }

        return new GetRunResult(false, (int)resp.StatusCode, Refusal: await ReadRefusalAsync(resp, ct));
    }

    public async Task<CancelRunResult> CancelPipelineRunAsync(int pipelineId, long runId, CancellationToken ct = default)
    {
        using var resp = await sessions.AuthorizedSendAsync(
            origin => new HttpRequestMessage(HttpMethod.Post, $"{origin}/api/studio/pipelines/{pipelineId}/runs/{runId}/cancel"),
            ct);

        if (resp.StatusCode == HttpStatusCode.Accepted)
        {
            var receipt = await resp.Content.ReadFromJsonAsync<PipelineRunCancelRequestedDto>(JsonOptions, ct)
                ?? new PipelineRunCancelRequestedDto(runId, pipelineId);
            return new CancelRunResult(CancelRunKind.Accepted, (int)resp.StatusCode, Receipt: receipt);
        }

        var refusal = await ReadRefusalAsync(resp, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict && string.Equals(refusal.Code, "run_already_finished", StringComparison.Ordinal))
            return new CancelRunResult(CancelRunKind.AlreadyFinished, (int)resp.StatusCode, Refusal: refusal);
        if (resp.StatusCode == HttpStatusCode.Forbidden && string.Equals(refusal.Code, "cancel_denied", StringComparison.Ordinal))
            return new CancelRunResult(CancelRunKind.Denied, (int)resp.StatusCode, Refusal: refusal);

        return new CancelRunResult(CancelRunKind.Refused, (int)resp.StatusCode, Refusal: refusal);
    }

    private async Task<PipelineRunSubmittedDto> ReadValidatedReceiptAsync(
        HttpResponseMessage resp,
        int expectedPipelineId,
        long? expectedVersion,
        CancellationToken ct)
    {
        PipelineRunSubmittedDto? dto;
        try
        {
            dto = await resp.Content.ReadFromJsonAsync<PipelineRunSubmittedDto>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new HostedNetworkException("Portal returned an unreadable run receipt.", ex);
        }

        if (dto is null
            || dto.RunId <= 0
            || dto.PipelineId != expectedPipelineId
            || dto.Version <= 0
            || (expectedVersion is not null && dto.Version != expectedVersion.Value)
            || !string.Equals(dto.JobName, $"PIPELINE:{expectedPipelineId}", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(dto.ScriptHash))
        {
            throw new HostedNetworkException("Portal returned an invalid run receipt.");
        }

        return dto;
    }

    private async Task<PipelineRunRefusalDto> ReadRefusalAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var secrets = sessions.GetActiveSecrets();
        try
        {
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = doc.RootElement;
                    var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                        ? HostedSecretRedactor.Redact(c.GetString(), secrets)
                        : root.TryGetProperty("error", out var ce) && ce.ValueKind == JsonValueKind.String
                            ? HostedSecretRedactor.Redact(ce.GetString(), secrets)
                            : $"http_{(int)resp.StatusCode}";
                    var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                        ? HostedSecretRedactor.Redact(e.GetString(), secrets)
                        : root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String
                            ? HostedSecretRedactor.Redact(d.GetString(), secrets)
                            : resp.ReasonPhrase ?? $"Request failed ({(int)resp.StatusCode})";
                    long? currentVersion = root.TryGetProperty("currentVersion", out var cv)
                        && cv.ValueKind == JsonValueKind.Number
                        && cv.TryGetInt64(out var ver)
                        ? ver
                        : null;
                    return new PipelineRunRefusalDto(code, error, currentVersion);
                }
            }
        }
        catch { }

        return new PipelineRunRefusalDto(
            $"http_{(int)resp.StatusCode}",
            HostedSecretRedactor.Redact(resp.ReasonPhrase ?? $"Request failed ({(int)resp.StatusCode})", secrets));
    }
}
