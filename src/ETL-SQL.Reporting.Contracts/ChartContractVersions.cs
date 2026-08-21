namespace ETL_SQL.Reporting.Semantics;

public static class ChartContractVersions
{
    public const int Current = 1;
    public const string ChartSpecSchema = "https://etl-sql.org/schemas/reporting/chart-spec/v1";
    public const string ChartDataSchema = "https://etl-sql.org/schemas/reporting/chart-data/v1";
    public const string PlotPlanSchema = "https://etl-sql.org/schemas/reporting/plot-plan/v1";
}

public interface IVersionedChartContract
{
    string Schema { get; }
    int Version { get; }
    void Validate();
}
