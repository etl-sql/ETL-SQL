namespace ETL_SQL.Core
{
    public class SetWhatIfStatement : Statement
    {
        public bool Enabled { get; set; }
        public override string ToSql() => $"SET WHAT_IF {(Enabled ? "ON" : "OFF")}";
    }
}
