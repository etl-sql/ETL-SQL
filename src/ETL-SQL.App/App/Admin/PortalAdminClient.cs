using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App.Admin;

/// <summary>
/// Distinct outcomes an admin CLI verb can produce. Scripts branch on these, so each is a stable
/// part of the CLI's contract — see <c>docs/reference/cli/admin-identity.md</c>.
/// </summary>
public enum AdminExitCode
{
    Success = 0,
    /// <summary>Credentials missing, malformed, or rejected by the Portal.</summary>
    AuthFailure = 3,
    /// <summary>Authenticated, but the token lacks the scope or role for this route.</summary>
    ScopeDenied = 4,
    /// <summary>The named user, group, or session does not exist.</summary>
    NotFound = 5,
    /// <summary>A name matched more than one record, so the caller must disambiguate by id.</summary>
    AmbiguousMatch = 6,
    /// <summary>The record changed since it was read, or a uniqueness constraint was violated.</summary>
    Conflict = 7,
    /// <summary>The Portal rejected the request as invalid.</summary>
    ValidationError = 8,
    /// <summary>The Portal could not be reached.</summary>
    Unreachable = 9
}

/// <summary>Raised when a verb cannot proceed, carrying the exit code the process should report.</summary>
public sealed class AdminCliException(AdminExitCode code, string message) : Exception(message)
{
    public AdminExitCode Code { get; } = code;
}

/// <summary>
/// Talks to a Portal's administration API over HTTP.
///
/// <para><b>HTTP, deliberately.</b> <c>ETL-SQL.App</c> does not reference <c>ETL-SQL.Portal</c>, and
/// must not: going over the wire is what lets the CLI administer a <i>remote</i> Portal from a jump
/// box, which is the point of having it. An architecture-boundary test enforces the missing
/// reference so it cannot be added later by accident.</para>
///
/// <para>Credentials are never accepted on argv — see <see cref="PortalAdminCredentials"/>.</para>
/// </summary>
public sealed class PortalAdminClient(HttpClient http, string baseUrl)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PortalAdminClient Create(string baseUrl) =>
        new(PolicyBoundHttp.CreateClient(), baseUrl.TrimEnd('/'));

    private string? _token;

    /// <summary>
    /// Exchanges service-account credentials for a short-lived token. The secret is sent in the
    /// request body and never logged, echoed, or placed on a command line.
    /// </summary>
    public async Task AuthenticateAsync(PortalAdminCredentials credentials, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync($"{baseUrl}/api/auth/service-token",
                new { clientId = credentials.ClientId, clientSecret = credentials.ClientSecret }, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AdminCliException(AdminExitCode.Unreachable,
                $"Could not reach the Portal at {baseUrl}: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
            throw new AdminCliException(AdminExitCode.AuthFailure,
                $"The Portal rejected the service-account credentials ({(int)response.StatusCode}).");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json, ct);
        _token = body?["accessToken"]?.GetValue<string>()
                 ?? throw new AdminCliException(AdminExitCode.AuthFailure,
                     "The Portal returned no access token.");
    }

    /// <summary>Identity, roles, and scopes carried by the current token. Prints no secret.</summary>
    public WhoAmI DescribeIdentity()
    {
        if (_token is null)
            throw new AdminCliException(AdminExitCode.AuthFailure, "Not authenticated.");

        // Read the unprotected JWT payload locally: this reports what the CLI is presenting, which
        // is what an operator debugging a runbook needs, without spending an API call.
        var parts = _token.Split('.');
        if (parts.Length < 2)
            throw new AdminCliException(AdminExitCode.AuthFailure, "The Portal returned a malformed token.");

        var payload = JsonNode.Parse(Base64UrlDecode(parts[1]))?.AsObject()
                      ?? throw new AdminCliException(AdminExitCode.AuthFailure, "The Portal returned a malformed token.");

        return new WhoAmI(
            payload["unique_name"]?.GetValue<string>() ?? "(unknown)",
            Values(payload, "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"),
            Values(payload, "scope"),
            payload["identity_type"]?.GetValue<string>() ?? "user",
            payload["exp"] is { } exp
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetValue<long>()).UtcDateTime
                : null);
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}");
        return await SendAsync(request, ct);
    }

    public Task<JsonNode?> PostAsync(string path, object? payload, CancellationToken ct, long? expectedVersion = null) =>
        SendAsync(Build(HttpMethod.Post, path, payload, expectedVersion), ct);

    public Task<JsonNode?> PutAsync(string path, object? payload, CancellationToken ct, long? expectedVersion = null) =>
        SendAsync(Build(HttpMethod.Put, path, payload, expectedVersion), ct);

    public Task<JsonNode?> DeleteAsync(string path, CancellationToken ct, long? expectedVersion = null) =>
        SendAsync(Build(HttpMethod.Delete, path, null, expectedVersion), ct);

    private HttpRequestMessage Build(HttpMethod method, string path, object? payload, long? expectedVersion)
    {
        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        if (payload is not null) request.Content = JsonContent.Create(payload, options: Json);
        // The Portal expects the version it handed out, quoted, in If-Match. Sending it is what
        // turns a blind overwrite into a detectable conflict.
        if (expectedVersion is { } version)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return request;
    }

    private async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_token is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_token}");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AdminCliException(AdminExitCode.Unreachable,
                $"Could not reach the Portal at {baseUrl}: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode) throw await ToExceptionAsync(response, ct);

        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static async Task<AdminCliException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var detail = (await response.Content.ReadAsStringAsync(ct)).Trim();
        // A 403 from a service identity is nearly always the scope gate, and telling the operator
        // that is far more useful than "forbidden" — it points at the account's grant.
        var code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => AdminExitCode.AuthFailure,
            HttpStatusCode.Forbidden => AdminExitCode.ScopeDenied,
            HttpStatusCode.NotFound => AdminExitCode.NotFound,
            HttpStatusCode.Conflict => AdminExitCode.Conflict,
            // 428 means the caller omitted If-Match. Reported as a conflict because it is the same
            // "your view of this record is not good enough to write from" problem, and a script
            // should react the same way: re-read and retry.
            HttpStatusCode.PreconditionRequired => AdminExitCode.Conflict,
            HttpStatusCode.PreconditionFailed => AdminExitCode.Conflict,
            HttpStatusCode.BadRequest => AdminExitCode.ValidationError,
            _ => AdminExitCode.ValidationError
        };
        return new AdminCliException(code,
            $"The Portal returned {(int)response.StatusCode}{(detail.Length > 0 ? ": " + detail : ".")}");
    }

    private static string[] Values(JsonObject payload, string claim) => payload[claim] switch
    {
        JsonArray array => array.Select(value => value?.GetValue<string>() ?? "").Where(v => v.Length > 0).ToArray(),
        JsonValue value => [value.GetValue<string>()],
        _ => []
    };

    private static string Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    public sealed record WhoAmI(
        string Name, string[] Roles, string[] Scopes, string IdentityType, DateTime? ExpiresUtc);
}
