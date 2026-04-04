namespace ETL_SQL.Core
{
    public class ShowProfileStatement : Statement
    {
        public override string ToSql() => "SHOW PROFILE";
    }
}
