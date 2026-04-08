namespace ETL_SQL.Core
{
    public record SetWhatIfStatement : Statement
    {
        public bool Enabled { get; init; }
        public override string ToSql() => $"SET WHAT_IF {(Enabled ? "ON" : "OFF")}";
    }
}
