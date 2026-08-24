namespace ETL_SQL.Reporting.Semantics;

public static class ChartContractVersions
{
    public const int ChartSpecCurrent = 2;
    public const int ChartDataCurrent = 1;
    public const int PlotPlanCurrent = 2;
    public const string LegacyChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v1";
    public const string ChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v2";
    public const string ChartDataSchema = "https://etl-sql.org/schemas/reporting/chart-data/v1";
    public const string LegacyPlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v1";
    public const string PlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v2";
}

public interface IVersionedChartContract
{
    string Schema { get; }
    int Version { get; }
    void Validate();
}
