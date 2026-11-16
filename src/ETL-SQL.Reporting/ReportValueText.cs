using System;
using System.Globalization;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Reporting;

/// <summary>
/// A value as a report carries it in cells, chart labels and parameter text: the same on every
/// host. It is the engine's CAST AS STRING text (ISO dates, invariant numbers), except that a date
/// whose time is exactly midnight is just <c>yyyy-MM-dd</c>, so a date column reads as dates on an
/// axis (owner, 2026-11-10). Display formatting in a chosen culture stays with FORMAT and the
/// report's formatting options.
/// </summary>
public static class ReportValueText
{
    public static string? Of(object? value) => value switch
    {
        DateTime date when date.TimeOfDay == TimeSpan.Zero => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => TypeConverter.ToInvariantText(value)
    };
}
