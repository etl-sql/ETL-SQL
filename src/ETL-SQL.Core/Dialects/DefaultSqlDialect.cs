using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Dialects;

public class DefaultSqlDialect : ISqlDialect
{
    public virtual string Name => "DEFAULT";

    public virtual string RewriteIdentifier(string name) => name;

    public virtual string RewriteFunctionCall(string functionName, IReadOnlyList<Expression> arguments, Func<Expression, string> compileArg)
    {
        if (functionName.Equals("CAST", StringComparison.OrdinalIgnoreCase) ||
            functionName.Equals("TRY_CAST", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Count >= 2)
            {
                var castExpr = compileArg(arguments[0]);
                var typeStr = arguments[1] is LiteralExpression litType ? litType.Value?.ToString() ?? "" : compileArg(arguments[1]);
                return $"{functionName.ToUpperInvariant()}({castExpr} AS {typeStr})";
            }
        }
        var args = string.Join(", ", arguments.Select(compileArg));
        return $"{functionName.ToUpperInvariant()}({args})";
    }

    public virtual bool SupportsTop => false;

    public virtual string FormatTop(string compiledTop, bool percent, bool withTies) => "";

    public virtual string FormatOffsetLimit(string? compiledOffset, string? compiledLimit)
    {
        var sql = "";
        if (compiledOffset != null)
        {
            sql += $" OFFSET {compiledOffset}";
            if (compiledLimit != null)
            {
                sql += $" LIMIT {compiledLimit}";
            }
        }
        else if (compiledLimit != null)
        {
            sql += $" LIMIT {compiledLimit}";
        }
        return sql;
    }

    public virtual string FormatTableAlias(string alias) => $" AS {alias}";
}
