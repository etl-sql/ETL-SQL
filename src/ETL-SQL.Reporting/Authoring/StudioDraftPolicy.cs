using System.Text.RegularExpressions;

namespace ETL_SQL.Reporting.Authoring;

/// <summary>
/// What a recovery draft may hold. A draft is unsaved text, so it has had none of the checks a save
/// runs; the one thing it must never carry is a credential in the clear, which would then sit in the
/// catalog database outside the secret store.
/// </summary>
public static partial class StudioDraftPolicy
{
    /// <summary>
    /// True when the script assigns a password, key, or token as a literal that is not an
    /// <c>ENC:</c>, <c>SECRET:</c>, or <c>SHARED:</c> reference. Mirrors
    /// <c>detectPlaintextSecrets</c> in studio-security.ts, which refuses the same text in the browser.
    /// </summary>
    public static bool ContainsPlaintextSecret(string script) =>
        PlaintextPassword().IsMatch(script) || PlaintextKey().IsMatch(script);

    [GeneratedRegex("""\b(PASSWORD|PWD)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2""", RegexOptions.IgnoreCase)]
    private static partial Regex PlaintextPassword();

    [GeneratedRegex("""\b(API_KEY|APIKEY|SECRET_KEY|SECRETKEY|TOKEN|ACCESS_TOKEN)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2""", RegexOptions.IgnoreCase)]
    private static partial Regex PlaintextKey();
}
