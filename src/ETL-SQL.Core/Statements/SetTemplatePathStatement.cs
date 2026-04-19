using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core
{
    /// <summary>
    /// SET TEMPLATE_PATH = '<path>';
    /// Sets the directory where the engine searches for custom style templates.
    /// </summary>
    public record SetTemplatePathStatement : Statement
    {
        public Expression PathExpression { get; init; }

        public SetTemplatePathStatement(Expression pathExpression)
        {
            PathExpression = pathExpression;
        }

        public override string ToSql() => $"SET TEMPLATE_PATH = {PathExpression.ToSql()};";
    }
}
