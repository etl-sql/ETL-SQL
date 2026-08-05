using System.Text.RegularExpressions;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Reduces an error message to a stable grouping key by removing the parts that vary between
/// otherwise identical failures — identifiers, timestamps, quoted values, paths, and numbers.
///
/// The goal is deliberately modest: cluster the same outage, never merge different ones. When in
/// doubt the normalizer leaves text alone, because over-grouping hides a second incident behind the
/// first and that is the one failure mode an operator cannot recover from by reading more closely.
/// </summary>
public static partial class RunFailureSignature
{
    /// <summary>Shown when a run failed without recording a message.</summary>
    public const string NoMessage = "(no error message)";

    private const int MaxLength = 240;

    /// <summary>
    /// Returns the grouping key for <paramref name="error"/>. Stable across calls and safe to
    /// compare with ordinal equality. Never returns null or empty.
    /// </summary>
    public static string Normalize(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return NoMessage;

        var text = error.Trim();

        // Order matters: the more specific shapes must be replaced before the general ones, or
        // digits inside a GUID or timestamp get rewritten first and the shape stops matching.
        text = GuidPattern().Replace(text, "<id>");
        text = TimestampPattern().Replace(text, "<time>");
        text = PathPattern().Replace(text, "<path>");
        text = QuotedPattern().Replace(text, "<value>");
        text = HexPattern().Replace(text, "<hex>");
        text = NumberPattern().Replace(text, "<n>");
        text = WhitespacePattern().Replace(text, " ").Trim();

        text = text.ToLowerInvariant();
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    /// <summary>
    /// Picks the message shown for an incident. Prefers the longest non-empty message in the group:
    /// providers often truncate under load, and the fullest text is the most useful one to read.
    /// </summary>
    public static string SampleFor(IEnumerable<string?> errors)
    {
        var best = errors
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .OrderByDescending(e => e.Length)
            .FirstOrDefault();
        return best ?? NoMessage;
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    // ISO-8601-ish instants, with or without zone, plus bare clock times.
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?|\b\d{2}:\d{2}:\d{2}(\.\d+)?\b")]
    private static partial Regex TimestampPattern();

    // Windows drive/UNC paths and rooted POSIX paths, including the trailing file name.
    [GeneratedRegex(@"([a-zA-Z]:\\|\\\\)[^\s""',;)]*|(?<![\w<])/[\w.-]+(/[\w.-]+)+")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"'[^']*'|""[^""]*""|\[[^\]]*\]")]
    private static partial Regex QuotedPattern();

    [GeneratedRegex(@"\b0x[0-9a-fA-F]+\b|\b[0-9a-fA-F]{16,}\b")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"\d[\d,._]*")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
