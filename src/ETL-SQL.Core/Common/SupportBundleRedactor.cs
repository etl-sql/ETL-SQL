using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Common;

/// <summary>
/// Redaction for anything leaving the deployment as diagnostics: configuration JSON and free text.
///
/// This lives in Core rather than beside the CLI's bundle builder because two hosts now produce
/// support material — the <c>admin support-bundle</c> command and the Portal's online-safe bundle —
/// and a second copy of these rules would drift. Redaction that is <em>almost</em> the same in two
/// places is worse than none: it produces two artifacts that look equally safe and are not.
///
/// The bias is deliberate: values are masked unless a key is recognisably non-secret metadata, and
/// data-shaped text (table rows, addresses, host paths) is masked wholesale. Over-redacting costs a
/// support engineer a round trip; under-redacting sends customer data to a third party.
/// </summary>
public static class SupportBundleRedactor
{
    // Key names whose values are treated as secrets and masked. "version"/"note" suffixes are
    // excluded so non-secret metadata (e.g. AtRestKeyVersion) stays visible for diagnostics.
    private static readonly Regex SecretKeyPattern = new(
        "(password|passwd|pwd|secret|apikey|api_key|token|accountkey|sharedaccesskey|privatekey|clientsecret|connectionstring|atrestkey)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Suffixes that demote a secret-looking key back to non-secret metadata.
    private static readonly Regex SecretKeyExemptPattern = new(
        "(version|note|expiry|expires|enabled|count|limit|window|days|minutes|seconds|policy|provider|path|name|mode)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Embedded credentials inside connection-string-like values (e.g. "...;Password=hunter2;...").
    private static readonly Regex EmbeddedSecretPattern = new(
        "((?:password|pwd|secret|accountkey|sharedaccesskey)\\s*=)([^;]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UrlQueryValuePattern = new(
        @"([?&][^=\s&#?]+)=([^&\s#]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IpAddressPattern = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex WindowsPathPattern = new(
        @"\b[A-Z]:\\[^\s""'<>|]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UserPathPattern = new(
        @"/(?:Users|home)/[^\s""'<>|]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TableLikeLinePattern = new(
        @"^\s*(?:[|│].*[|│]|[^,\r\n]+,[^,\r\n]+,[^,\r\n]+).*$",
        RegexOptions.Compiled);

    public const string RedactedMarker = "***REDACTED***";
    public const string RedactedValueMarker = "***REDACTED_VALUE***";
    public const string RedactedPathMarker = "***REDACTED_PATH***";
    public const string RedactedTableRowMarker = "***REDACTED_TABLE_ROW***";

    /// <summary>
    /// True when a configuration key names a secret value (and is not an exempt metadata suffix
    /// such as <c>AtRestKeyVersion</c>). Shared so every redactor and the backup config-secret
    /// splitter agree on what counts as a secret.
    /// </summary>
    public static bool IsSecretKey(string key) =>
        SecretKeyPattern.IsMatch(key) && !SecretKeyExemptPattern.IsMatch(key);

    /// <summary>Masks secret-bearing values in a configuration JSON document.</summary>
    public static string RedactConfigJson(string json)
    {
        var root = JsonNode.Parse(json);
        if (root != null) Redact(root);
        return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
    }

    /// <summary>
    /// Masks credentials, addresses, host paths, and data-shaped rows in free diagnostic text.
    /// Stack traces and log lines are preserved; anything that looks like a row of customer data is
    /// replaced wholesale, because a redactor cannot tell whose data it is.
    /// </summary>
    public static string RedactDiagnosticText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var redacted = SecretRedactor.Redact(text) ?? string.Empty;
        redacted = UrlQueryValuePattern.Replace(redacted, m => $"{m.Groups[1].Value}={RedactedValueMarker}");
        redacted = EmailPattern.Replace(redacted, RedactedValueMarker);
        redacted = IpAddressPattern.Replace(redacted, RedactedValueMarker);
        redacted = WindowsPathPattern.Replace(redacted, RedactedPathMarker);
        redacted = UserPathPattern.Replace(redacted, RedactedPathMarker);

        var lines = redacted.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (LooksLikePrivateTableRow(lines[i]))
                lines[i] = RedactedTableRowMarker;
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Recursively masks secret-bearing values in a parsed JSON tree, in place.</summary>
    /// <param name="forceMaskStrings">
    /// When true (inside a secret-keyed container such as <c>PreviousSecrets</c> or
    /// <c>ConnectionStrings</c>), every string leaf is masked entirely rather than only its
    /// embedded credential portion.
    /// </param>
    private static void Redact(JsonNode node, bool forceMaskStrings = false)
    {
        switch (node)
        {
            case JsonObject obj:
                // Snapshot keys to avoid mutating while enumerating.
                foreach (var key in obj.Select(kvp => kvp.Key).ToList())
                {
                    var child = obj[key];
                    bool isSecretKey = IsSecretKey(key);

                    if (child is JsonValue val)
                    {
                        if (val.TryGetValue<string>(out var s))
                        {
                            if (string.IsNullOrEmpty(s))
                                continue; // empty string carries no secret; keep it visible
                            obj[key] = (isSecretKey || forceMaskStrings) ? RedactedMarker : RedactEmbedded(s);
                        }
                        // Non-string scalars (numbers/bools) are config knobs, never secrets — leave them.
                    }
                    else if (child != null)
                    {
                        // Recurse; a secret-keyed container masks all string leaves beneath it.
                        Redact(child, forceMaskStrings || isSecretKey);
                    }
                }
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is JsonValue val && val.TryGetValue<string>(out var s))
                    {
                        if (string.IsNullOrEmpty(s)) continue;
                        arr[i] = forceMaskStrings ? RedactedMarker : RedactEmbedded(s);
                    }
                    else if (item != null)
                    {
                        Redact(item, forceMaskStrings);
                    }
                }
                break;
        }
    }

    private static string RedactEmbedded(string value) =>
        EmbeddedSecretPattern.Replace(value, m => m.Groups[1].Value + RedactedMarker);

    private static bool LooksLikePrivateTableRow(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains(" at ", StringComparison.Ordinal)
            || line.Contains("=>", StringComparison.Ordinal))
        {
            return false;
        }

        return TableLikeLinePattern.IsMatch(line);
    }
}
