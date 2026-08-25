using System;
using System.Globalization;
using ETL_SQL.Core.Common;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Core.Reporting;

/// <summary>
/// The report-level formatting a renderer must use. Every value is resolved on the server so the
/// same report renders identically in a browser, a PDF, an email, and a terminal — nothing here
/// is ever inferred from the viewer's machine.
/// </summary>
/// <param name="Locale">A <see cref="CultureInfo"/> name; the empty string is the invariant culture.</param>
/// <param name="TimeZone">A zone id resolvable by <see cref="TimeZoneResolver"/>.</param>
/// <param name="NullLabel">The text rendered in place of a NULL value.</param>
public sealed record ReportFormattingSettings(string Locale, string TimeZone, string NullLabel)
{
    /// <summary>The invariant culture's name. Configuration spells "invariant" as the empty string.</summary>
    public const string InvariantLocale = "";

    /// <summary>The zone used when neither the script nor configuration names one.</summary>
    public const string FallbackTimeZone = "UTC";

    /// <summary>The NULL text used when neither the visual, the script, nor configuration names one.</summary>
    public const string FallbackNullLabel = "-";

    /// <summary>Configuration key holding the default report time zone.</summary>
    public const string TimeZoneConfigurationKey = "Scheduler:DefaultTimeZone";

    /// <summary>Configuration key holding the default report locale.</summary>
    public const string LocaleConfigurationKey = "Reporting:DefaultLocale";

    /// <summary>Configuration key holding the default NULL label.</summary>
    public const string NullLabelConfigurationKey = "Reporting:DefaultNullLabel";

    /// <summary>The last resort of the precedence chain: invariant culture, UTC, and "-".</summary>
    public static readonly ReportFormattingSettings Default =
        new(InvariantLocale, FallbackTimeZone, FallbackNullLabel);

    /// <summary>
    /// Resolves the configured defaults — the tier below any <c>SET REPORT</c> override. An absent or
    /// blank key falls through to <see cref="Default"/>; a present but invalid value is an error, because
    /// silently ignoring it would make a whole deployment render in a zone or locale nobody asked for.
    /// </summary>
    public static ReportFormattingSettings FromConfiguration(IConfiguration? configuration)
    {
        if (configuration is null) return Default;

        var zone = configuration[TimeZoneConfigurationKey];
        zone = string.IsNullOrWhiteSpace(zone) ? FallbackTimeZone : zone.Trim();
        if (!TimeZoneResolver.TryFindTimeZone(zone, out _))
            throw new ArgumentException($"{TimeZoneConfigurationKey} '{zone}' is not a known time zone.");

        var locale = configuration[LocaleConfigurationKey];
        locale = string.IsNullOrWhiteSpace(locale) ? InvariantLocale : locale.Trim();
        if (!TryResolveCulture(locale, out _))
            throw new ArgumentException($"{LocaleConfigurationKey} '{locale}' is not a known locale.");

        // An explicitly empty NullLabel is a real choice ("render nothing"), so only an absent key falls back.
        var nullLabel = configuration[NullLabelConfigurationKey] ?? FallbackNullLabel;

        return new ReportFormattingSettings(locale, zone, nullLabel);
    }

    /// <summary>
    /// Applies the precedence chain: each script override wins over the configured default, which wins
    /// over the built-in fallback. Every report context resolves through this one method.
    /// </summary>
    public static ReportFormattingSettings Resolve(
        ReportFormattingSettings? defaults,
        string? locale,
        string? timeZone,
        string? nullLabel)
    {
        var baseline = defaults ?? Default;
        return new ReportFormattingSettings(
            locale ?? baseline.Locale,
            timeZone ?? baseline.TimeZone,
            nullLabel ?? baseline.NullLabel);
    }

    /// <summary>Resolves <see cref="Locale"/> to a culture. The empty string is the invariant culture.</summary>
    public CultureInfo Culture => TryResolveCulture(Locale, out var culture) ? culture : CultureInfo.InvariantCulture;

    /// <summary>Resolves <see cref="TimeZone"/>. Falls back to UTC when the host cannot resolve the id.</summary>
    public TimeZoneInfo Zone => TimeZoneResolver.TryFindTimeZone(TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>Whether <see cref="Locale"/> names the invariant culture.</summary>
    public bool IsInvariantLocale => string.IsNullOrEmpty(Locale);

    /// <summary>
    /// Resolves a locale name through <see cref="CultureInfo.GetCultureInfo(string)"/>. The empty string
    /// is the invariant culture. Rejects names the runtime only accepts because of its fallback behaviour.
    /// </summary>
    public static bool TryResolveCulture(string? locale, out CultureInfo culture)
    {
        culture = CultureInfo.InvariantCulture;
        if (string.IsNullOrWhiteSpace(locale)) return true;
        try
        {
            culture = CultureInfo.GetCultureInfo(locale.Trim(), predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
