using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Short-lived, HMAC-authenticated identity assertion issued by a Portal for the Orchestrator.
/// It is deliberately audience-bound and separate from the browser's Portal JWT, so neither token
/// can be replayed against the other service even when both are rooted in the same deployment.
/// </summary>
public static class OrchestratorIdentityAssertion
{
    public const string HeaderName = "X-Orchestrator-Identity";
    public const string Issuer = "etl-sql-portal";
    public const string Audience = "etl-sql-orchestrator-api";
    public const int CurrentVersion = 1;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(OrchestratorCaller caller, string signingSecret, DateTimeOffset? now = null)
    {
        ValidateSecret(signingSecret);
        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var payload = new OrchestratorIdentityPayload(
            CurrentVersion,
            Issuer,
            Audience,
            issuedAt.ToUnixTimeSeconds(),
            issuedAt.Add(DefaultLifetime).ToUnixTimeSeconds(),
            Guid.NewGuid().ToString("N"),
            caller.SubjectType,
            caller.SubjectId,
            caller.DisplayName,
            caller.Roles,
            caller.GroupIds);
        var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signature = Sign(encodedPayload, signingSecret);
        return encodedPayload + "." + Base64UrlEncode(signature);
    }

    public static bool TryValidate(
        string? assertion,
        string signingSecret,
        out OrchestratorCaller? caller,
        out string error,
        DateTimeOffset? now = null)
    {
        caller = null;
        error = string.Empty;
        try
        {
            ValidateSecret(signingSecret);
            if (string.IsNullOrWhiteSpace(assertion))
            {
                error = "A federated Orchestrator identity assertion is required.";
                return false;
            }

            var separator = assertion.IndexOf('.');
            if (separator <= 0 || separator != assertion.LastIndexOf('.'))
            {
                error = "The Orchestrator identity assertion is malformed.";
                return false;
            }

            var encodedPayload = assertion[..separator];
            var suppliedSignature = Base64UrlDecode(assertion[(separator + 1)..]);
            var expectedSignature = Sign(encodedPayload, signingSecret);
            if (suppliedSignature.Length != expectedSignature.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                error = "The Orchestrator identity assertion signature is invalid.";
                return false;
            }

            var payload = JsonSerializer.Deserialize<OrchestratorIdentityPayload>(
                Base64UrlDecode(encodedPayload), JsonOptions);
            if (payload is null
                || payload.Version != CurrentVersion
                || !string.Equals(payload.Issuer, Issuer, StringComparison.Ordinal)
                || !string.Equals(payload.Audience, Audience, StringComparison.Ordinal))
            {
                error = "The Orchestrator identity assertion has an invalid issuer, audience, or version.";
                return false;
            }

            var observedAt = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            if (payload.IssuedAt > observedAt + 30 || payload.ExpiresAt < observedAt
                || payload.ExpiresAt - payload.IssuedAt > (long)DefaultLifetime.TotalSeconds)
            {
                error = "The Orchestrator identity assertion is expired or outside its allowed lifetime.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(payload.SubjectType)
                || string.IsNullOrWhiteSpace(payload.SubjectId)
                || string.IsNullOrWhiteSpace(payload.Nonce))
            {
                error = "The Orchestrator identity assertion does not identify a principal.";
                return false;
            }

            caller = new OrchestratorCaller(
                Normalize(payload.SubjectType, 32),
                Normalize(payload.SubjectId, 128),
                Normalize(payload.DisplayName, 128),
                NormalizeMany(payload.Roles, 64),
                NormalizeMany(payload.GroupIds, 128));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            error = "The Orchestrator identity assertion is malformed.";
            return false;
        }
    }

    public static void ValidateSecret(string signingSecret)
    {
        if (string.IsNullOrWhiteSpace(signingSecret) || Encoding.UTF8.GetByteCount(signingSecret) < 32)
            throw new ArgumentException("Orchestrator identity signing secret must contain at least 32 UTF-8 bytes.", nameof(signingSecret));
    }

    private static byte[] Sign(string encodedPayload, string signingSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        return hmac.ComputeHash(Encoding.ASCII.GetBytes(encodedPayload));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException() };
        return Convert.FromBase64String(normalized);
    }

    private static string Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value.Trim())
        {
            if (character is < (char)0x20 or > (char)0x7E) continue;
            if (builder.Length == maxLength) break;
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static string[] NormalizeMany(IReadOnlyList<string>? values, int maxLength)
    {
        if (values is null || values.Count == 0) return [];
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var normalized = Normalize(value, maxLength);
            if (normalized.Length > 0) unique.Add(normalized);
        }
        return [.. unique];
    }

    private sealed record OrchestratorIdentityPayload(
        int Version,
        string Issuer,
        string Audience,
        long IssuedAt,
        long ExpiresAt,
        string Nonce,
        string SubjectType,
        string SubjectId,
        string DisplayName,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> GroupIds);
}

public sealed record OrchestratorCaller(
    string SubjectType,
    string SubjectId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> GroupIds)
{
    public string PrincipalKey => $"{SubjectType}:{SubjectId}";
    public string AuditActor => string.IsNullOrWhiteSpace(DisplayName)
        ? PrincipalKey
        : $"{PrincipalKey}:{DisplayName}";

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
