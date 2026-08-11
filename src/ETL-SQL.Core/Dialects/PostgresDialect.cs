using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Dialects;

public class PostgresDialect : DefaultSqlDialect
{
    public override string Name => "POSTGRES";

    public override string RewriteIdentifier(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper == "SYSDATE" || upper == "GETDATE") return "NOW()";
        return name;
    }

    public override string RewriteFunctionCall(string functionName, IReadOnlyList<Expression> arguments, Func<Expression, string> compileArg)
    {
        var funcName = functionName.ToUpperInvariant();

        if (funcName == "SYSDATE" || funcName == "GETDATE")
            return "NOW()";

        if (funcName == "ISNULL" && arguments.Count == 2)
            return $"COALESCE({compileArg(arguments[0])}, {compileArg(arguments[1])})";

        if (funcName == "LEN" && arguments.Count == 1)
            return $"LENGTH({compileArg(arguments[0])})";

        if ((funcName == "YEAR" || funcName == "MONTH" || funcName == "DAY") && arguments.Count == 1)
            return $"EXTRACT({funcName} FROM {compileArg(arguments[0])})";

        if (funcName == "TRUNC" && arguments.Count == 1)
            return $"DATE_TRUNC('day', {compileArg(arguments[0])})";

        if (funcName == "TRUNC" && arguments.Count == 2)
        {
            var arg = compileArg(arguments[0]);
            var part = arguments[1] is LiteralExpression litVal2 && litVal2.Value != null ? litVal2.Value.ToString() ?? "day" : "day";
            return $"DATE_TRUNC('{part}', {arg})";
        }

        return base.RewriteFunctionCall(functionName, arguments, compileArg);
    }
}
