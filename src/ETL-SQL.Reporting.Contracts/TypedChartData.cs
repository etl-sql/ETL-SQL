using System.Collections.Immutable;

namespace ETL_SQL.Reporting.Semantics;

public enum ChartValueKind
{
    Null,
    Integer,
    FloatingPoint,
    Decimal,
    Text,
    Date,
    Time,
    LocalDateTime,
    OffsetDateTime,
    Boolean
}

public sealed record ChartValue(
    ChartValueKind Kind,
    long? Integer = null,
    double? FloatingPoint = null,
    decimal? Decimal = null,
    string? Text = null,
    DateOnly? Date = null,
    TimeOnly? Time = null,
    DateTime? LocalDateTime = null,
    DateTimeOffset? OffsetDateTime = null,
    bool? Boolean = null)
{
    /// <summary>The single shared null value. <see cref="ChartValue"/> is an immutable record with
    /// value equality, so one instance serves every null channel; the resolver and renderers use
    /// <see cref="Null"/> as a per-mark coalesce fallback, which allocated on every call.</summary>
    private static readonly ChartValue NullValue = new(ChartValueKind.Null);

    public static ChartValue Null() => NullValue;
    public static ChartValue From(long value) => new(ChartValueKind.Integer, Integer: value);
    public static ChartValue From(double value) => new(ChartValueKind.FloatingPoint, FloatingPoint: value);
    public static ChartValue From(decimal value) => new(ChartValueKind.Decimal, Decimal: value);
    public static ChartValue From(string value) => new(ChartValueKind.Text, Text: value ?? throw new ArgumentNullException(nameof(value)));
    public static ChartValue From(DateOnly value) => new(ChartValueKind.Date, Date: value);
    public static ChartValue From(TimeOnly value) => new(ChartValueKind.Time, Time: value);
    public static ChartValue FromLocal(DateTime value) => new(ChartValueKind.LocalDateTime, LocalDateTime: value);
    public static ChartValue From(DateTimeOffset value) => new(ChartValueKind.OffsetDateTime, OffsetDateTime: value);
    public static ChartValue From(bool value) => new(ChartValueKind.Boolean, Boolean: value);

    public void Validate()
    {
        // Counted without an array or LINQ: PlotPlan.Validate() calls this for every channel of
        // every datum, so a 5,000-row plan allocated 15,000 arrays and enumerators per pass.
        var populated = 0;
        if (Integer.HasValue) populated++;
        if (FloatingPoint.HasValue) populated++;
        if (Decimal.HasValue) populated++;
        if (Text is not null) populated++;
        if (Date.HasValue) populated++;
        if (Time.HasValue) populated++;
        if (LocalDateTime.HasValue) populated++;
        if (OffsetDateTime.HasValue) populated++;
        if (Boolean.HasValue) populated++;

        if (Kind == ChartValueKind.Null && populated != 0)
            throw new InvalidDataException("A null chart value cannot carry a raw value.");
        if (Kind != ChartValueKind.Null && populated != 1)
            throw new InvalidDataException($"A {Kind} chart value must carry exactly one raw value.");

        var matches = Kind switch
        {
            ChartValueKind.Null => populated == 0,
            ChartValueKind.Integer => Integer.HasValue,
            ChartValueKind.FloatingPoint => FloatingPoint.HasValue && double.IsFinite(FloatingPoint.Value),
            ChartValueKind.Decimal => Decimal.HasValue,
            ChartValueKind.Text => Text is not null,
            ChartValueKind.Date => Date.HasValue,
            ChartValueKind.Time => Time.HasValue,
            ChartValueKind.LocalDateTime => LocalDateTime.HasValue && LocalDateTime.Value.Kind == DateTimeKind.Unspecified,
            ChartValueKind.OffsetDateTime => OffsetDateTime.HasValue,
            ChartValueKind.Boolean => Boolean.HasValue,
            _ => false
        };
        if (!matches)
            throw new InvalidDataException($"Chart value kind {Kind} does not match its raw value.");
    }
}

public sealed record ChartColumn(
    string Name,
    ChartValueKind ValueKind,
    DataSemanticKind SemanticKind,
    ImmutableArray<ChartValue> Values,
    ImmutableArray<string?> DisplayValues)
{
    public void Validate(int rowCount)
    {
        ChartContractValidation.RequireName(Name, nameof(Name));
        if (Values.IsDefault || Values.Length != rowCount)
            throw new InvalidDataException($"Column '{Name}' has {Values.Length} values; expected {rowCount}.");
        if (!DisplayValues.IsDefaultOrEmpty && DisplayValues.Length != rowCount)
            throw new InvalidDataException($"Column '{Name}' display values do not match its raw value count.");
        foreach (var value in Values)
        {
            value.Validate();
            if (value.Kind != ChartValueKind.Null && value.Kind != ValueKind)
                throw new InvalidDataException($"Column '{Name}' expects {ValueKind} but contains {value.Kind}.");
        }
    }
}

public sealed record ChartDataSet(
    string Schema,
    int Version,
    string Name,
    int RowCount,
    ImmutableArray<ChartColumn> Columns) : IVersionedChartContract
{
    public static ChartDataSet Create(string name, int rowCount, ImmutableArray<ChartColumn> columns) => new(
        ChartContractVersions.ChartDataSchema,
        ChartContractVersions.ChartDataCurrent,
        name,
        rowCount,
        columns);

    public void Validate()
    {
        ChartContractValidation.RequireVersion(Schema, Version, ChartContractVersions.ChartDataSchema, ChartContractVersions.ChartDataCurrent, nameof(ChartDataSet));
        ChartContractValidation.RequireName(Name, nameof(Name));
        if (RowCount < 0) throw new InvalidDataException("Chart data row count cannot be negative.");
        ChartContractValidation.RequireUnique(Columns.Select(column => column.Name), "column name");
        foreach (var column in Columns) column.Validate(RowCount);
    }
}
