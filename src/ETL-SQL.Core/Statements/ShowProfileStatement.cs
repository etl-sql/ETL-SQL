namespace ETL_SQL.Core
{
    public class ShowProfileStatement : Statement
    {
        public string? IntoTable { get; set; }
        public override string ToSql() => "SHOW PROFILE" + (IntoTable != null ? $" INTO {IntoTable}" : "");
    }
}
