namespace ETL_SQL.Core
{
    public record ShowProfileStatement : Statement
    {
        public string? IntoTable { get; init; }
        public override string ToSql() => "SHOW PROFILE" + (IntoTable != null ? $" INTO {IntoTable}" : "");
    }
}
