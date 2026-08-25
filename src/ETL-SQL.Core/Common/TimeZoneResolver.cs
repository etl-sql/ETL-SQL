using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Common;

/// <summary>
/// The single time-zone identifier resolver for the whole language. Schedules, <c>AT TIME ZONE</c>,
/// relative dates, and report formatting all resolve through here so one script cannot accept a
/// spelling another rejects.
/// </summary>
public static class TimeZoneResolver
{
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "UTC", "UTC" },
        { "GMT", "UTC" },
        { "EST", "America/New_York" },
        { "EDT", "America/New_York" },
        { "CST", "America/Chicago" },
        { "CDT", "America/Chicago" },
        { "MST", "America/Denver" },
        { "MDT", "America/Denver" },
        { "PST", "America/Los_Angeles" },
        { "PDT", "America/Los_Angeles" },
        { "CET", "Europe/Paris" },
        { "CEST", "Europe/Paris" },
        { "BST", "Europe/London" },
        { "JST", "Asia/Tokyo" },
        { "AEST", "Australia/Sydney" },
        { "AEDT", "Australia/Sydney" }
    };

    // Hosts without the IANA database (older Windows images) still answer to the Windows registry ids.
    private static readonly Dictionary<string, string> WindowsFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        { "America/New_York", "Eastern Standard Time" },
        { "America/Chicago", "Central Standard Time" },
        { "America/Denver", "Mountain Standard Time" },
        { "America/Los_Angeles", "Pacific Standard Time" },
        { "Europe/London", "GMT Standard Time" },
        { "Europe/Paris", "W. Europe Standard Time" },
        { "Asia/Tokyo", "Tokyo Standard Time" },
        { "Australia/Sydney", "AUS Eastern Standard Time" }
    };

    /// <summary>Resolves a zone identifier, accepting every spelling the rest of the language accepts.</summary>
    /// <exception cref="TimeZoneNotFoundException">The identifier is not a known zone on this host.</exception>
    public static TimeZoneInfo FindTimeZone(string zoneName)
    {
        if (Abbreviations.TryGetValue(zoneName, out var mapped))
            zoneName = mapped;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneName);
        }
        catch
        {
            if (WindowsFallbacks.TryGetValue(zoneName, out var windowsId))
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            throw;
        }
    }

    /// <summary>Resolves a zone identifier, returning <c>false</c> instead of throwing when it is unknown.</summary>
    public static bool TryFindTimeZone(string? zoneName, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(zoneName)) return false;
        try
        {
            zone = FindTimeZone(zoneName.Trim());
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
