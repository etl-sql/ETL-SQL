using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Orchestrator.Service;

internal sealed class PortalScheduledToolPrincipalLookup(
    HttpClient http, IConfiguration configuration, ILogger<PortalScheduledToolPrincipalLookup> logger)
    : IScheduledToolPrincipalLookup, IDisposable
{
    private readonly ConcurrentDictionary<string, TenantToken> tokens = new(StringComparer.Ordinal);

    public void Dispose() => http.Dispose();

    public async Task<ToolExecutionPrincipal?> ResolveAsync(string tenantId, string ownerKey, CancellationToken cancellationToken)
    {
        var query = new ScheduledToolPrincipalRequest(ownerKey);
        var settings = configuration.GetSection(ScheduledToolPrincipalRequest.ConfigurationSection);
        if (!TenantId.TryParse(tenantId, out var tenant) || tenantId != tenant.Value
            || !query.TryGetOwner(out _, out _)
            || !TryGetPortalUri(settings["PortalUrl"], out var portal))
            return null;

        var credentials = settings.GetSection($"Tenants:{tenantId}");
        var clientId = credentials["ClientId"];
        var clientSecret = credentials["ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        var entry = tokens.GetOrAdd(tenantId, _ => new TenantToken());
        try
        {
            var token = await entry.GetAsync(http, portal, clientId, clientSecret, cancellationToken);
            if (token is null) return null;

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(portal, ScheduledToolPrincipalRequest.Path))
            {
                Content = JsonContent.Create(query)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                entry.Invalidate(token);
            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<ScheduledToolPrincipalResponse>(cancellationToken);
            return result?.ForOwner(tenantId, query);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or FormatException)
        {
            logger.LogWarning("Scheduled tool principal lookup unavailable for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    private static bool TryGetPortalUri(string? value, out Uri portal)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            && (candidate.Scheme == Uri.UriSchemeHttps || (candidate.IsLoopback && candidate.Scheme == Uri.UriSchemeHttp))
            && candidate.UserInfo.Length == 0 && candidate.Query.Length == 0 && candidate.Fragment.Length == 0
            && candidate.AbsolutePath == "/")
        {
            portal = candidate;
            return true;
        }

        portal = null!;
        return false;
    }

    private sealed class TenantToken
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private CachedToken? cached;

        public void Invalidate(CachedToken token) => Interlocked.CompareExchange(ref cached, null, token);

        public async Task<CachedToken?> GetAsync(
            HttpClient http, Uri portal, string clientId, string clientSecret, CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                var current = Volatile.Read(ref cached);
                if (current is not null && current.ExpiresAt > DateTimeOffset.UtcNow
                    && current.Portal == portal && current.ClientId == clientId)
                    return current;

                using var response = await http.PostAsJsonAsync(new Uri(portal, "/api/auth/service-token"),
                    new { clientId, clientSecret }, ct);
                if (!response.IsSuccessStatusCode) return null;

                var token = await response.Content.ReadFromJsonAsync<ServiceToken>(ct);
                if (string.IsNullOrWhiteSpace(token?.AccessToken) || token.ExpiresIn <= 0) return null;

                var replacement = new CachedToken(token.AccessToken, clientId, portal,
                    DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(token.ExpiresIn - 30, 0, 900)));
                Volatile.Write(ref cached, replacement);
                return replacement;
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private sealed record ServiceToken(string AccessToken, int ExpiresIn);
    private sealed record CachedToken(string AccessToken, string ClientId, Uri Portal, DateTimeOffset ExpiresAt);
}
