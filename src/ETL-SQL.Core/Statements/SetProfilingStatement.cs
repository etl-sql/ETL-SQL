namespace ETL_SQL.Core
{
    public class SetProfilingStatement : Statement
    {
        public bool Enabled { get; set; }
        public override string ToSql() => $"SET PROFILING {(Enabled ? "ON" : "OFF")}";
    }
}
