namespace ETL_SQL.Reporting.Semantics;

public static class ChartContractVersions
{
    public const int TransposedConfidenceChartSpecVersion = 13;
    public const string TransposedConfidenceChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v13";
    public const int TransposedConfidencePlotPlanVersion = 16;
    public const string TransposedConfidencePlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v16";
    public const int OrdinaryAreaInterpolationChartSpecVersion = 12;
    public const string OrdinaryAreaInterpolationChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v12";
    public const int OrdinaryAreaInterpolationPlotPlanVersion = 15;
    public const string OrdinaryAreaInterpolationPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v15";
    public const int OrdinaryInterpolationChartSpecVersion = 11;
    public const string OrdinaryInterpolationChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v11";
    public const int OrdinaryInterpolationPlotPlanVersion = 14;
    public const string OrdinaryInterpolationPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v14";
    public const int ChartSpecCurrent = 2;
    public const int ChartDataCurrent = 1;
    public const int ConnectedChartSpecVersion = 3;
    public const string ConnectedChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v3";
    public const int ConnectChartSpecVersion = 4;
    public const string ConnectChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v4";
    public const int TransposedAreaChartSpecVersion = 5;
    public const string TransposedAreaChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v5";
    public const int TransposedAreaPlotPlanVersion = 8;
    public const string TransposedAreaPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v8";
    public const int ConnectedCompositionChartSpecVersion = 6;
    public const string ConnectedCompositionChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v6";
    public const int ConnectedCompositionPlotPlanVersion = 9;
    public const string ConnectedCompositionPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v9";
    public const int ZeroConnectedChartSpecVersion = 7;
    public const string ZeroConnectedChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v7";
    public const int ZeroConnectedPlotPlanVersion = 10;
    public const string ZeroConnectedPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v10";
    public const int TransposedConnectedChartSpecVersion = 8;
    public const string TransposedConnectedChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v8";
    public const int TransposedConnectedPlotPlanVersion = 11;
    public const string TransposedConnectedPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v11";
    public const int DecoratedConnectedChartSpecVersion = 9;
    public const string DecoratedConnectedChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v9";
    public const int DecoratedConnectedPlotPlanVersion = 12;
    public const string DecoratedConnectedPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v12";
    public const int InterpolatedConnectedChartSpecVersion = 10;
    public const string InterpolatedConnectedChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v10";
    public const int InterpolatedConnectedPlotPlanVersion = 13;
    public const string InterpolatedConnectedPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v13";
    public const int ConnectedPlotPlanVersion = 5;
    public const string ConnectedPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v5";
    public const int ScaledRibbonPlotPlanVersion = 6;
    public const string ScaledRibbonPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v6";
    public const int ConnectPlotPlanVersion = 7;
    public const string ConnectPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v7";
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
