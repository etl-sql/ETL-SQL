using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Common;
/// <summary>
/// Shared last-mile redaction for text that may leave the engine through logs,
/// diagnostics, audit rows, result payloads, or operator-facing status.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "********";
    private const int RuntimeSecretMinimumLength = 4;
    private const int MaxRuntimeSecrets = 1024;
    private static readonly object RuntimeSecretsLock = new();
    private static readonly List<string> RuntimeSecrets = [];
    private static string[] _cachedOrderedSecrets = [];

    [GeneratedRegex(@"\b(ENC|DPAPI-M|DPAPI|MACHINE):[A-Za-z0-9+/=_:.\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ProtectedValuePattern();

    [GeneratedRegex(@"\bSECRET:[A-Za-z0-9_.:/@\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SecretReferencePattern();

    [GeneratedRegex(@"\bCAPABILITY:[A-Za-z0-9_.:/@\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CapabilityReferencePattern();

    [GeneratedRegex(@"\bSHARED:[A-Za-z0-9_.:/@\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SharedReferencePattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BearerPattern();

    // URI userinfo credentials (bolt://user:pass@host, postgres://user:pass@host/db, ...).
    // RFC 3986 forbids raw '/', '@', and whitespace in userinfo, so the password segment
    // stops at those; masks the password and keeps the username for diagnostics.
    [GeneratedRegex(@"(://[^/\s:@""']+:)[^@\s/""']+@", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex UrlCredentialPattern();

    [GeneratedRegex(@"\bsas_[A-Za-z0-9_-]{40,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ServiceAccountSecretPattern();

    [GeneratedRegex(@"([""']?)(PASSWORD|PWD|SECRET|SECRET_KEY|SECRETKEY|APIKEY|API_KEY|TOKEN|ACCESS_TOKEN|REFRESH_TOKEN|CLIENT_SECRET|CLIENTSECRET|CREDENTIAL|PRIVATEKEY|PRIVATE_KEY|ACCESS_KEY|ACCESSKEY|ACCOUNT_KEY|ACCOUNTKEY|SAS_TOKEN|PASSPHRASE|SASL_PASSWORD|SASL_JAAS_CONFIG|AUTHORIZATION)(\1)\s*:\s*([""']?)[^,""'}\]\s;]+(\4)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(@"\b(PASSWORD|PWD|SECRET|SECRET_KEY|SECRETKEY|APIKEY|API_KEY|TOKEN|ACCESS_TOKEN|REFRESH_TOKEN|CLIENT_SECRET|CLIENTSECRET|CREDENTIAL|PRIVATEKEY|PRIVATE_KEY|ACCESS_KEY|ACCESSKEY|ACCOUNT_KEY|ACCOUNTKEY|SAS_TOKEN|PASSPHRASE|SASL_PASSWORD|SASL_JAAS_CONFIG|AUTHORIZATION)\s*=\s*(['""]?)[^'""\s,;)]*(\2)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex AssignmentSecretPattern();

    public static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (Governance.SecretResolvableFields.IsOrganizationDesignated(key)) return true;
        return key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            || key.Equals("PWD", StringComparison.OrdinalIgnoreCase)
            || key.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            || key.Contains("APIKEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
            || key.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
            || key.Contains("PRIVATEKEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("PRIVATE_KEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ACCESS_KEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ACCESSKEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ACCOUNT_KEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("ACCOUNTKEY", StringComparison.OrdinalIgnoreCase)
            || key.Contains("SASL_JAAS_CONFIG", StringComparison.OrdinalIgnoreCase)
            || key.Contains("PASSPHRASE", StringComparison.OrdinalIgnoreCase)
            || key.Equals("AUTHORIZATION", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksSensitiveValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return value.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("DPAPI:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("DPAPI-M:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("MACHINE:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("CAPABILITY:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    public static string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var redacted = ProtectedValuePattern().Replace(text, match =>
        {
            var prefix = match.Value.Split(':', 2)[0];
            return $"{prefix}:{Mask}";
        });
        redacted = SecretReferencePattern().Replace(redacted, $"SECRET:{Mask}");
        redacted = CapabilityReferencePattern().Replace(redacted, $"CAPABILITY:{Mask}");
        redacted = SharedReferencePattern().Replace(redacted, $"SHARED:{Mask}");
        redacted = BearerPattern().Replace(redacted, $"Bearer {Mask}");
        redacted = UrlCredentialPattern().Replace(redacted, $"$1{Mask}@");
        redacted = ServiceAccountSecretPattern().Replace(redacted, $"sas_{Mask}");
        redacted = JsonSecretPattern().Replace(redacted, match =>
        {
            var key = match.Groups[2].Value;
            return $"{match.Groups[1].Value}{key}{match.Groups[3].Value}:{match.Groups[4].Value}{Mask}{match.Groups[5].Value}";
        });
        redacted = AssignmentSecretPattern().Replace(redacted, match =>
        {
            var key = match.Groups[1].Value;
            return $"{key}={match.Groups[2].Value}{Mask}{match.Groups[3].Value}";
        });
        foreach (var secret in RegisteredRuntimeSecrets())
            redacted = redacted.Replace(secret, Mask, StringComparison.Ordinal);
        return redacted;
    }

    /// <summary>
    /// Registers a concrete secret value that was resolved inside this process so later log,
    /// diagnostic, audit, and exception redaction also catches provider errors that echo the bare
    /// value without a sensitive key shape.
    /// </summary>
    public static void RegisterRuntimeSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length < RuntimeSecretMinimumLength
            || string.Equals(value, Mask, StringComparison.Ordinal))
            return;

        lock (RuntimeSecretsLock)
        {
            if (RuntimeSecrets.Contains(value, StringComparer.Ordinal))
                return;
            if (RuntimeSecrets.Count >= MaxRuntimeSecrets)
                RuntimeSecrets.RemoveAt(0);
            RuntimeSecrets.Add(value);
            _cachedOrderedSecrets = RuntimeSecrets
                .OrderByDescending(v => v.Length)
                .ToArray();
        }
    }

    private static string[] RegisteredRuntimeSecrets() => _cachedOrderedSecrets;

    public static object? RedactValue(string? key, object? value)
    {
        if (value is null) return null;
        if (IsSensitiveKey(key)) return Mask;
        return value is string text
            ? Redact(text)
            : value;
    }

    public static string MaskIfSensitive(string? key, string? value)
    {
        if (IsSensitiveKey(key) || LooksSensitiveValue(value)) return Mask;
        return Redact(value) ?? string.Empty;
    }

    public static Exception? RedactException(Exception? exception)
    {
        if (exception is null) return null;
        return new RedactedException(exception.GetType().FullName ?? exception.GetType().Name,
            Redact(exception.Message) ?? string.Empty,
            Redact(exception.StackTrace),
            RedactException(exception.InnerException));
    }

    private sealed class RedactedException : Exception
    {
        private readonly string _typeName;
        private readonly string? _redactedStackTrace;

        public RedactedException(string typeName, string message, string? redactedStackTrace, Exception? inner)
            : base(message, inner)
        {
            _typeName = typeName;
            _redactedStackTrace = redactedStackTrace;
        }

        public override string? StackTrace => _redactedStackTrace;

        public override string ToString()
        {
            var text = $"{_typeName}: {Message}";
            if (!string.IsNullOrWhiteSpace(_redactedStackTrace))
                text += Environment.NewLine + _redactedStackTrace;
            if (InnerException is not null)
                text += Environment.NewLine + "---> " + InnerException;
            return text;
        }
    }
}
