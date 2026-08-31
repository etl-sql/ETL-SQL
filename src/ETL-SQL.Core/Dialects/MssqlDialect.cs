using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Dialects;

public class MssqlDialect : DefaultSqlDialect
{
    public override string Name => "MSSQL";

    public override string RewriteIdentifier(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper == "SYSDATE" || upper == "NOW") return "GETDATE()";
        return name;
    }

    public override string RewriteFunctionCall(string functionName, IReadOnlyList<Expression> arguments, Func<Expression, string> compileArg)
    {
        var funcName = functionName.ToUpperInvariant();

        if (funcName == "SYSDATE" || funcName == "NOW")
            return "GETDATE()";

        if (funcName == "LENGTH" && arguments.Count == 1)
            return $"LEN({compileArg(arguments[0])})";

        if (funcName == "TRUNC" && arguments.Count == 1)
            return $"CAST({compileArg(arguments[0])} AS DATE)";

        if (funcName == "TRUNC" && arguments.Count == 2)
        {
            var arg = compileArg(arguments[0]);
            var part = arguments[1] is LiteralExpression litVal && litVal.Value != null ? litVal.Value.ToString() ?? "" : "";
            if (part.Equals("MM", StringComparison.OrdinalIgnoreCase) || part.Equals("MONTH", StringComparison.OrdinalIgnoreCase))
                return $"DATEADD(month, DATEDIFF(month, 0, {arg}), 0)";
            if (part.Equals("YY", StringComparison.OrdinalIgnoreCase) || part.Equals("YEAR", StringComparison.OrdinalIgnoreCase))
                return $"DATEADD(year, DATEDIFF(year, 0, {arg}), 0)";
        }

        return base.RewriteFunctionCall(functionName, arguments, compileArg);
    }

    public override bool SupportsTop => true;

    public override string FormatStringConcat(string left, string right) => $"{left} + {right}";

    public override string FormatTop(string compiledTop, bool percent, bool withTies)
    {
        var p = percent ? " PERCENT" : "";
        var t = withTies ? " WITH TIES" : "";
        return $"TOP ({compiledTop}){p}{t}";
    }

    public override string FormatOffsetLimit(string? compiledOffset, string? compiledLimit)
    {
        var sql = "";
        if (compiledOffset != null)
        {
            sql += $" OFFSET {compiledOffset} ROWS";
            if (compiledLimit != null)
            {
                sql += $" FETCH NEXT {compiledLimit} ROWS ONLY";
            }
        }
        return sql;
    }
}
