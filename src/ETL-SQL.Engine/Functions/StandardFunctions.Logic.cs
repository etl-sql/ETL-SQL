using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Functions;

namespace ETL_SQL.Engine.Functions
{
    public static partial class StandardFunctions
    {
        private static void RegisterLogicFunctions(IFunctionRegistry registry)
        {
            registry.RegisterWithHelp("COALESCE", (args, ctx) => args.FirstOrDefault(a => !a.IsNull()), "COALESCE(v1, v2, ...): Returns the first non-null value.");
            registry.RegisterWithHelp("ISNULL", IsNull, "ISNULL(v1, v2): Returns v2 if v1 is null.");
            registry.RegisterWithHelp("NVL", IsNull, "NVL(v1, v2): Alias for ISNULL.");
            registry.RegisterWithHelp("NULLIF", (args, ctx) => EvaluationUtils.IsSoftEqual(args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)) ? null : args.ElementAtOrDefault(0), "NULLIF(v1, v2): Returns NULL if v1 equals v2, else v1.");
            registry.RegisterWithHelp("IS_NULL", (args, ctx) => args[0].IsNull(), "IS_NULL(expr): Returns TRUE if the expression is null.");
            registry.RegisterWithHelp("IS_NOT_NULL", (args, ctx) => !args[0].IsNull(), "IS_NOT_NULL(expr): Returns TRUE if the expression is NOT null.");
            registry.RegisterWithHelp("IIF", (args, ctx) => args.Count >= 3 ? (Convert.ToBoolean(args[0]) ? args[1] : args[2]) : args.FirstOrDefault(), "IIF(cond, true_val, false_val): Returns one of two values depending on a condition.");
            registry.RegisterWithHelp("IFNULL", IsNull, "IFNULL(v1, v2): Alias for ISNULL.");
            registry.RegisterWithHelp("GREATEST", (args, ctx) => args.Where(a => !a.IsNull()).OrderByDescending(a => a).FirstOrDefault(), "GREATEST(v1, v2, ...): Returns the largest value in the list.");
            registry.RegisterWithHelp("LEAST", (args, ctx) => args.Where(a => !a.IsNull()).OrderBy(a => a).FirstOrDefault(), "LEAST(v1, v2, ...): Returns the smallest value in the list.");
            registry.RegisterWithHelp("NVL2", (args, ctx) => args.Count >= 3 ? (!args[0].IsNull() ? args[1] : args[2]) : args.FirstOrDefault(), "NVL2(v, if_not_null, if_null): Returns if_not_null if v is not null, else if_null.");
            registry.RegisterWithHelp("DECODE", Decode, "DECODE(val, search, result, ..., default): Returns the result matching the value, or the default.");
        }

        private static object? IsNull(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 2) return args.FirstOrDefault();
            var first = args[0];
            return (first.IsNull()) ? args[1] : first;
        }

        private static object? Decode(List<object?> args, IExecutionContext ctx)
        {
            if (args.Count < 3) return args.FirstOrDefault();
            var val = args[0];
            for (int i = 1; i < args.Count - 1; i += 2)
            {
                if (EvaluationUtils.IsSoftEqual(val, args[i])) return args[i + 1];
            }
            return args.Count % 2 == 0 ? args.Last() : null;
        }
    }
}
