namespace ETL_SQL.Reporting.Semantics;

public static class ChartContractVersions
{
    public const int ChartSpecCurrent = 2;
    public const int ChartDataCurrent = 1;
    public const int ConnectedChartSpecVersion = 3;
    public const string ConnectedChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v3";
    public const int ConnectedPlotPlanVersion = 5;
    public const string ConnectedPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v5";
    // COMPAT_BREAK: 0.19 — PlotPlan v3 removes the redundant per-datum tooltip string.
    public const int PlotPlanCurrent = 3;
    // Radial plans need an envelope older renderers reject; existing plans retain v3.
    public const int RadialPlotPlanVersion = 4;
    public const string RadialPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v4";
    public const string LegacyChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v1";
    public const string ChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v2";
    public const string ChartDataSchema = "https://etl-sql.org/schemas/reporting/chart-data/v1";
    public const string LegacyPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v1";
    public const string LegacyPlotPlanV2Schema = "https://etl-sql.org/schemas/reporting/plot-plan/v2";
    public const string PlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v3";
}

public interface IVersionedChartContract
{
    string Schema { get; }
    int Version { get; }
    void Validate();
}
