using ETL_SQL.Core.Formatting;

namespace ETL_SQL.Core;
public record ShowProfileStatement : Statement
{
    public string? IntoTable { get; init; }
    public override string ToSql() => AstSerializer.Format(this);
}
