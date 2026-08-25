using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Reporting;

/// <summary>
/// The closed set of <c>SET REPORT</c> keys. The parser and the handler share it so an unknown key
/// fails at author time instead of being silently discarded at run time.
/// </summary>
public static class ReportMetadataKeys
{
    public const string Title = "TITLE";
    public const string Description = "DESCRIPTION";
    public const string Css = "CSS";
    public const string Js = "JS";
    public const string Head = "HEAD";
    public const string Body = "BODY";
    public const string Footer = "FOOTER";
    public const string Favicon = "FAVICON";
    public const string Logo = "LOGO";
    public const string Background = "BACKGROUND";
    public const string Theme = "THEME";
    public const string Navigation = "NAVIGATION";
    public const string TimeZone = "TIME_ZONE";
    public const string Locale = "LOCALE";
    public const string NullLabel = "NULL_LABEL";

    private static readonly string[] Ordered =
    [
        Title, Description, Css, Js, Head, Body, Footer, Favicon, Logo,
        Background, Theme, Navigation, TimeZone, Locale, NullLabel
    ];

    private static readonly HashSet<string> Known = new(Ordered, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every supported key, in documentation order.</summary>
    public static IReadOnlyList<string> All => Ordered;

    public static bool IsKnown(string? key) => key is not null && Known.Contains(key);

    /// <summary>The message shown when an author writes a key the engine does not implement.</summary>
    public static string UnknownKeyMessage(string key) =>
        $"'{key}' is not a valid SET REPORT key. Supported keys: {string.Join(", ", Ordered)}.";
}
