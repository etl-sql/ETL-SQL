using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Dialects;

public class OracleDialect : DefaultSqlDialect
{
    public override string Name => "ORACLE";

    public override string RewriteIdentifier(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper == "NOW" || upper == "GETDATE") return "SYSDATE";
        return name;
    }

    public override string RewriteFunctionCall(string functionName, IReadOnlyList<Expression> arguments, Func<Expression, string> compileArg)
    {
        var funcName = functionName.ToUpperInvariant();

        if (funcName == "SYSDATE" || funcName == "NOW" || funcName == "GETDATE")
            return "SYSDATE";

        if (funcName == "ISNULL" && arguments.Count == 2)
            return $"COALESCE({compileArg(arguments[0])}, {compileArg(arguments[1])})";

        if (funcName == "LEN" && arguments.Count == 1)
            return $"LENGTH({compileArg(arguments[0])})";

        if ((funcName == "YEAR" || funcName == "MONTH" || funcName == "DAY") && arguments.Count == 1)
            return $"EXTRACT({funcName} FROM {compileArg(arguments[0])})";

        if (funcName == "SUBSTRING" && arguments.Count == 3)
            return $"SUBSTR({compileArg(arguments[0])}, {compileArg(arguments[1])}, {compileArg(arguments[2])})";

        return base.RewriteFunctionCall(functionName, arguments, compileArg);
    }

    public override string FormatOffsetLimit(string? compiledOffset, string? compiledLimit)
    {
        var sql = "";
        if (compiledOffset != null)
        {
            sql += $" OFFSET {compiledOffset}";
            // Oracle doesn't natively support LIMIT, only FETCH NEXT in 12c. The original QueryCompiler omitted LIMIT if it was ORACLE.
            // "if (sel.LimitCount != null && !d.Equals("ORACLE", StringComparison.OrdinalIgnoreCase))"
        }
        return sql;
    }

    public override string FormatTableAlias(string alias)
    {
        return $" {alias}";
    }
}
