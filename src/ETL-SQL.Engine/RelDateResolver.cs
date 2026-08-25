using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine;

/// <summary>
/// Stateless resolver for RELDATE expressions. Converts a relative-date string such as
/// "D-1", "ME-1", "N-2H" or timezone offset versions like "D-1 EST" into a concrete <see cref="DateTimeOffset"/> at execution time.
/// </summary>
public static class RelDateResolver
{
    /// <summary>
    /// Resolves a time-zone identifier. Delegates to <see cref="TimeZoneResolver"/> so RELDATE,
    /// schedules, and report formatting cannot drift on which spellings they accept.
    /// </summary>
    public static TimeZoneInfo FindTimeZone(string zoneName) => TimeZoneResolver.FindTimeZone(zoneName);

    /// <summary>
    /// Resolves a RELDATE expression to a concrete DateTime.
    /// Backward-compatible wrapper calling ResolveToOffset.
    /// </summary>
    public static DateTime Resolve(string expression, DayOfWeek weekStart, DateTime? now = null)
    {
        var refTime = now.HasValue ? new DateTimeOffset(now.Value) : DateTimeOffset.Now;
        var dto = ResolveToOffset(expression, weekStart, refTime);
        return dto.DateTime;
    }

    /// <summary>
    /// Resolves to the legacy timezone-neutral <see cref="DateTime"/> when no timezone suffix is
    /// present, and to <see cref="DateTimeOffset"/> when the expression explicitly names a timezone.
    /// </summary>
    public static object ResolveValue(string expression, DayOfWeek weekStart, DateTimeOffset? now = null)
    {
        var hasZone = TrySplitTimeZone(expression, out _, out _);
        var resolved = ResolveToOffset(expression, weekStart, now);
        if (hasZone) return resolved;
        return resolved.DateTime;
    }

    /// <summary>
    /// Resolves a RELDATE expression to a concrete DateTimeOffset.
    /// Supports optional trailing timezone indicators (e.g. "D-1 EST").
    /// </summary>
    public static DateTimeOffset ResolveToOffset(string expression, DayOfWeek weekStart, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ExecutionException("RELDATE expression cannot be empty.");

        TrySplitTimeZone(expression, out var expr, out var tzName);

        // Determine target timezone
        TimeZoneInfo targetTz = TimeZoneInfo.Local;
        if (!string.IsNullOrEmpty(tzName))
        {
            try
            {
                targetTz = FindTimeZone(tzName);
            }
            catch (TimeZoneNotFoundException ex)
            {
                throw new ExecutionException($"Unknown time zone '{tzName}' in RELDATE expression '{expression}'.", ex);
            }
            catch (InvalidTimeZoneException ex)
            {
                throw new ExecutionException($"Invalid time zone configuration for '{tzName}'.", ex);
            }
        }

        var baseTime = now ?? DateTimeOffset.Now;
        // Convert the reference time to the target timezone
        var localNow = TimeZoneInfo.ConvertTime(baseTime, targetTz);

        // Fixed date passthrough: first character is a digit → parse as ISO date and return.
        if (char.IsDigit(expr[0]))
        {
            if (DateTimeOffset.TryParse(expr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fixedDate))
            {
                if (!string.IsNullOrEmpty(tzName))
                {
                    return new DateTimeOffset(fixedDate.DateTime, targetTz.GetUtcOffset(fixedDate.DateTime));
                }
                return fixedDate;
            }
            throw new ExecutionException($"Invalid RELDATE fixed date: '{expression}'.");
        }

        var upper = expr.ToUpperInvariant();
        int pos = 0;
        char baseAnchor;
        bool isEnd = false;
        bool isUtc = false;

        // Parse anchor
        if (upper.StartsWith("NU", StringComparison.Ordinal)) { baseAnchor = 'N'; isUtc = true; pos = 2; }
        else if (upper.StartsWith("WE", StringComparison.Ordinal)) { baseAnchor = 'W'; isEnd = true; pos = 2; }
        else if (upper.StartsWith("WS", StringComparison.Ordinal)) { baseAnchor = 'W'; pos = 2; }
        else if (upper.StartsWith("ME", StringComparison.Ordinal)) { baseAnchor = 'M'; isEnd = true; pos = 2; }
        else if (upper.StartsWith("MS", StringComparison.Ordinal)) { baseAnchor = 'M'; pos = 2; }
        else if (upper.StartsWith("QE", StringComparison.Ordinal)) { baseAnchor = 'Q'; isEnd = true; pos = 2; }
        else if (upper.StartsWith("QS", StringComparison.Ordinal)) { baseAnchor = 'Q'; pos = 2; }
        else if (upper.StartsWith("YE", StringComparison.Ordinal)) { baseAnchor = 'Y'; isEnd = true; pos = 2; }
        else if (upper.StartsWith("YS", StringComparison.Ordinal)) { baseAnchor = 'Y'; pos = 2; }
        else if (upper[0] == 'N') { baseAnchor = 'N'; pos = 1; }
        else if (upper[0] == 'W') { baseAnchor = 'W'; pos = 1; }
        else if (upper[0] == 'M') { baseAnchor = 'M'; pos = 1; }
        else if (upper[0] == 'Q') { baseAnchor = 'Q'; pos = 1; }
        else if (upper[0] == 'Y') { baseAnchor = 'Y'; pos = 1; }
        else if (upper[0] == 'D') { baseAnchor = 'D'; pos = 1; }
        else throw new ExecutionException(
            $"Unknown RELDATE anchor in '{expression}'. Valid anchors: D, W/WS/WE, M/MS/ME, Q/QS/QE, Y/YS/YE, N, NU.");

        // Parse optional +/- magnitude [unit]
        int shift = 0;
        char? unit = null;

        if (pos < upper.Length && (upper[pos] == '+' || upper[pos] == '-'))
        {
            int sign = upper[pos] == '-' ? -1 : 1;
            pos++;

            int numStart = pos;
            while (pos < upper.Length && char.IsDigit(upper[pos])) pos++;
            if (pos == numStart)
                throw new ExecutionException($"Expected numeric magnitude after sign in RELDATE expression '{expression}'.");

            shift = sign * int.Parse(upper.Substring(numStart, pos - numStart), CultureInfo.InvariantCulture);

            if (pos < upper.Length && char.IsLetter(upper[pos]))
            {
                unit = upper[pos];
                pos++;
            }
        }

        if (pos < upper.Length)
            throw new ExecutionException($"Unexpected characters '{upper[pos..]}' in RELDATE expression '{expression}'.");

        // Validate units
        if (baseAnchor == 'N')
        {
            if (unit != null && unit != 'H' && unit != 'I' && unit != 'S')
                throw new ExecutionException(
                    $"Invalid unit '{unit}' for N/NU anchor. Use H (hours), I (minutes), or S (seconds) in '{expression}'.");
            if (shift != 0 && unit == null)
                throw new ExecutionException(
                    $"N/NU arithmetic requires a time unit (H, I, or S) in '{expression}'.");
        }
        else if (unit != null)
        {
            throw new ExecutionException(
                $"Unit suffix '{unit}' is only valid for N/NU anchors in '{expression}'.");
        }

        var today = localNow.Date;
        DateTime resolvedLocalTime;

        switch (baseAnchor)
        {
            case 'D':
                resolvedLocalTime = today.AddDays(shift);
                break;

            case 'N':
                {
                    // For NU: use UTC. If now is explicitly provided (tests), use it directly.
                    var refTime = isUtc ? (now.HasValue ? now.Value : DateTimeOffset.UtcNow) : localNow;
                    if (shift == 0) return refTime;
                    var offsetTime = unit switch
                    {
                        'H' => refTime.AddHours(shift),
                        'I' => refTime.AddMinutes(shift),
                        'S' => refTime.AddSeconds(shift),
                        _ => refTime  // unreachable — validated above
                    };
                    return offsetTime;
                }

            case 'W':
                {
                    var weekStartDate = GetWeekStart(today, weekStart).AddDays(shift * 7);
                    resolvedLocalTime = isEnd ? weekStartDate.AddDays(6) : weekStartDate;
                    break;
                }

            case 'M':
                {
                    var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(shift);
                    resolvedLocalTime = isEnd ? new DateTime(monthStart.Year, monthStart.Month, DateTime.DaysInMonth(monthStart.Year, monthStart.Month)) : monthStart;
                    break;
                }

            case 'Q':
                {
                    int qStartMonth = ((today.Month - 1) / 3) * 3 + 1;
                    var qStart = new DateTime(today.Year, qStartMonth, 1).AddMonths(shift * 3);
                    resolvedLocalTime = isEnd ? new DateTime(qStart.Year, qStart.Month + 2, DateTime.DaysInMonth(qStart.Year, qStart.Month + 2)) : qStart;
                    break;
                }

            case 'Y':
                {
                    var yearStart = new DateTime(today.Year, 1, 1).AddYears(shift);
                    resolvedLocalTime = isEnd ? new DateTime(yearStart.Year, 12, 31) : yearStart;
                    break;
                }

            default:
                throw new ExecutionException($"Unhandled RELDATE anchor '{baseAnchor}'.");
        }

        // Return a DateTimeOffset with the correct offset at that resolved local time
        return CreateZonedValue(resolvedLocalTime, targetTz, expression);
    }

    private static bool TrySplitTimeZone(string expression, out string relativeExpression, out string? zoneName)
    {
        relativeExpression = expression.Trim();
        zoneName = null;
        if (relativeExpression.Length == 0 || char.IsDigit(relativeExpression[0])) return false;

        var firstSpace = relativeExpression.IndexOf(' ');
        if (firstSpace <= 0) return false;
        zoneName = relativeExpression[(firstSpace + 1)..].Trim();
        relativeExpression = relativeExpression[..firstSpace].Trim();
        return zoneName.Length > 0;
    }

    private static DateTimeOffset CreateZonedValue(DateTime localTime, TimeZoneInfo zone, string expression)
    {
        localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(localTime))
            throw new ExecutionException(
                $"RELDATE expression '{expression}' resolves to a nonexistent local time in '{zone.Id}' due to a daylight-saving transition.");

        var offset = zone.IsAmbiguousTime(localTime)
            ? zone.GetAmbiguousTimeOffsets(localTime).Min()
            : zone.GetUtcOffset(localTime);
        return new DateTimeOffset(localTime, offset);
    }

    private static DateTime GetWeekStart(DateTime date, DayOfWeek firstDay)
    {
        int diff = ((int)date.DayOfWeek - (int)firstDay + 7) % 7;
        return date.AddDays(-diff);
    }
}
