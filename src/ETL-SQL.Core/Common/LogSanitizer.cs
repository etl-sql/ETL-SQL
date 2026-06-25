namespace ETL_SQL.Core.Common;
/// <summary>
/// Neutralises user-controlled values before they are written to a log.
/// Stripping carriage-return/line-feed characters prevents log forging (CWE-117),
/// where an attacker embeds newlines to inject forged log entries.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Returns <paramref name="value"/> with CR and LF removed so it cannot span or
    /// fabricate additional log lines. Null is passed through unchanged.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    /// <summary>Convenience overload that renders any value to a sanitised string.</summary>
    public static string? Clean(object? value) =>
        value is null ? null : Clean(value.ToString());
}
