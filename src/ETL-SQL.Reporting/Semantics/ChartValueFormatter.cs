using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core.Reporting;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>
/// Turns a typed <see cref="ChartValue"/> into display text using the report's resolved locale,
/// time zone, and NULL label. Every renderer shares one instance per chart so a browser, a PDF, an
/// email, and a terminal cannot disagree about what a value looks like.
/// </summary>
/// <remarks>
/// This is deliberately separate from <c>PlotPlanResolver.Display</c>, which stays invariant because it
/// produces join and category <em>keys</em>. Formatting keys by locale would make category matching
/// depend on the server's configuration.
/// </remarks>
public sealed class ChartValueFormatter
{
    private readonly CultureInfo _culture;
    private readonly TimeZoneInfo _zone;
    private readonly bool _invariant;
    private readonly Dictionary<string, string?> _fieldFormats;
    private readonly Dictionary<string, string?> _fieldNullLabels;

    /// <summary>The text rendered in place of a NULL value.</summary>
    public string NullLabel { get; }

    public ChartValueFormatter(FormattingSpec formatting)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        var settings = new ReportFormattingSettings(formatting.Locale, formatting.TimeZone, formatting.NullLabel);
        _culture = settings.Culture;
        _zone = settings.Zone;
        _invariant = settings.IsInvariantLocale;
        NullLabel = formatting.NullLabel;
        var fields = formatting.Fields.IsDefaultOrEmpty
            ? []
            : formatting.Fields.GroupBy(field => field.Field, StringComparer.OrdinalIgnoreCase).ToList();
        _fieldFormats = fields.ToDictionary(group => group.Key, group => group.Last().Format, StringComparer.OrdinalIgnoreCase);
        _fieldNullLabels = fields.ToDictionary(group => group.Key,
            group => group.Select(field => field.NullLabel).LastOrDefault(label => label is not null),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The NULL label a specific field asks for, falling back to the report-level one.</summary>
    public string NullLabelFor(string? field) =>
        field is not null && _fieldNullLabels.TryGetValue(field, out var label) && label is not null ? label : NullLabel;

    /// <summary>Formats a value for display. NULL renders as the resolved NULL label.</summary>
    public string Format(ChartValue value, string? field = null)
    {
        if (value.Kind == ChartValueKind.Null) return NullLabelFor(field);
        var format = field is not null ? _fieldFormats.GetValueOrDefault(field) : null;
        return value.Kind switch
        {
            ChartValueKind.Integer => Number(value.Integer!.Value, format),
            ChartValueKind.FloatingPoint => format is not null
                ? value.FloatingPoint!.Value.ToString(format, _culture)
                : value.FloatingPoint!.Value.ToString("G", _culture),
            ChartValueKind.Decimal => Number(value.Decimal!.Value, format),
            ChartValueKind.Text => value.Text ?? string.Empty,
            ChartValueKind.Date => Temporal(value.Date!.Value.ToDateTime(TimeOnly.MinValue), format, DateShape.Date),
            ChartValueKind.Time => format is not null
                ? value.Time!.Value.ToString(format, _culture)
                : _invariant ? value.Time!.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : value.Time!.Value.ToString(_culture.DateTimeFormat.LongTimePattern, _culture),
            // A local date-time carries no offset, so there is nothing to convert; only the locale applies.
            ChartValueKind.LocalDateTime => Temporal(value.LocalDateTime!.Value, format, DateShape.DateTime),
            ChartValueKind.OffsetDateTime => Temporal(
                TimeZoneInfo.ConvertTime(value.OffsetDateTime!.Value, _zone).DateTime, format, DateShape.DateTime),
            ChartValueKind.Boolean => value.Boolean == true ? "true" : "false",
            _ => string.Empty
        };
    }

    /// <summary>Formats a value with an explicit format pattern.</summary>
    public string FormatWithPattern(ChartValue value, string? format)
    {
        if (value.Kind == ChartValueKind.Null) return NullLabel;
        if (format is null) return Format(value);
        return value.Kind switch
        {
            ChartValueKind.Integer => Number(value.Integer!.Value, format),
            ChartValueKind.FloatingPoint => value.FloatingPoint!.Value.ToString(format, _culture),
            ChartValueKind.Decimal => Number(value.Decimal!.Value, format),
            ChartValueKind.Text => value.Text ?? string.Empty,
            ChartValueKind.Date => Temporal(value.Date!.Value.ToDateTime(TimeOnly.MinValue), format, DateShape.Date),
            ChartValueKind.Time => value.Time!.Value.ToString(format, _culture),
            ChartValueKind.LocalDateTime => Temporal(value.LocalDateTime!.Value, format, DateShape.DateTime),
            ChartValueKind.OffsetDateTime => Temporal(
                TimeZoneInfo.ConvertTime(value.OffsetDateTime!.Value, _zone).DateTime, format, DateShape.DateTime),
            ChartValueKind.Boolean => value.Boolean == true ? "true" : "false",
            _ => string.Empty
        };
    }

    /// <summary>Formats a computed number — an axis tick, a domain bound — in the report's locale.</summary>
    public string Number(decimal value, string? format = null) =>
        value.ToString(format ?? "0.##", _culture);

    private enum DateShape { Date, DateTime }

    private string Temporal(DateTime value, string? format, DateShape shape)
    {
        if (format is not null) return value.ToString(format, _culture);
        // The invariant culture keeps the ISO shapes the plan hashes and golden SVGs are pinned to.
        if (_invariant)
            return shape == DateShape.Date || value.TimeOfDay == TimeSpan.Zero
                ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return shape == DateShape.Date || value.TimeOfDay == TimeSpan.Zero
            ? value.ToString(_culture.DateTimeFormat.ShortDatePattern, _culture)
            : value.ToString($"{_culture.DateTimeFormat.ShortDatePattern} {_culture.DateTimeFormat.LongTimePattern}", _culture);
    }
}
