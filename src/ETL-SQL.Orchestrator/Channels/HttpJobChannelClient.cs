using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Channels
{
    /// <summary>
    /// HTTP client implementation of <see cref="IJobChannel"/>. Connects to a running
    /// <c>ETL-SQL-OrchestratorService</c> at a configured base URL.
    /// Works identically on Windows and Linux.
    /// </summary>
    public class HttpJobChannelClient : IJobChannel, IDisposable
    {
        private readonly HttpClient _http;
        private readonly ILogger<HttpJobChannelClient> _logger;
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        public HttpJobChannelClient(HttpClient http, ILogger<HttpJobChannelClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<string> SubmitJobAsync(JobSubmitRequest request, CancellationToken ct = default)
        {
            _logger.LogDebug("Submitting job (label={Label}) to orchestrator service", request.Label);
            var response = await _http.PostAsJsonAsync("/jobs", request, _json, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SubmitResult>(_json, ct);
            return result?.JobId ?? throw new InvalidOperationException("Orchestrator service did not return a job ID.");
        }

        public async Task CancelJobAsync(string jobId, CancellationToken ct = default)
        {
            _logger.LogDebug("Cancelling job {JobId} via orchestrator service", jobId);
            var response = await _http.DeleteAsync($"/jobs/{jobId}", ct);
            response.EnsureSuccessStatusCode();
        }

        public async Task<JobStatusResponse> GetStatusAsync(string jobId, CancellationToken ct = default)
        {
            var response = await _http.GetAsync($"/jobs/{jobId}", ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JobStatusResponse>(_json, ct)
                ?? throw new InvalidOperationException($"Empty response for job {jobId} status.");
        }

        public void Dispose() => _http.Dispose();

        private sealed record SubmitResult(string JobId);
    }
}
