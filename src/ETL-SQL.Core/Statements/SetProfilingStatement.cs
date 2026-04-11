using ETL_SQL.Core.Formatting;

namespace ETL_SQL.Core
{
    public record SetProfilingStatement : Statement
    {
        public bool Enabled { get; init; }
        public override string ToSql() => AstSerializer.Format(this);
    }
}
