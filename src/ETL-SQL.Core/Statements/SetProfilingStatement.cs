namespace ETL_SQL.Core
{
    public record SetProfilingStatement : Statement
    {
        public bool Enabled { get; init; }
        public override string ToSql() => $"SET PROFILING {(Enabled ? "ON" : "OFF")}";
    }
}
