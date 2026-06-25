using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the PRINT statement, outputting messages or expression values to the logger.
/// </summary>
public class PrintStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    public Type SupportedStatementType => typeof(PrintStatement);

    public PrintStatementHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Executes the PRINT statement, evaluating and concatenating all provided arguments.</summary>
    /// <summary>Executes the PRINT statement, evaluating and concatenating all provided arguments.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (PrintStatement)statement;
        var values = new List<string>();

        foreach (var arg in stmt.Arguments)
        {
            var val = await context.EvaluateValue(arg, new Row());
            string strVal = val?.ToString() ?? "NULL";

            // Security: Mask sensitive variables if SHOW_PASSWORD is OFF
            if (!context.ShowPassword && arg is VariableExpression varExpr)
            {
                if (context.VarContext.VariableMetadata.TryGetValue(varExpr.Name, out var meta) && (meta.IsSensitive || meta.IsSecret))
                {
                    strVal = "*******";
                }
            }

            values.Add(strVal);
        }

        var message = string.Join(" ", values);

        if (stmt.ShowTimestamp != null && (bool)(await context.EvaluateValue(stmt.ShowTimestamp, new Row()) ?? false))
        {
            string format = (await context.EvaluateValue(stmt.TimestampFormat, new Row()))?.ToString() ?? "yyyy-MM-dd HH:mm:ss";
            message = $"[{DateTime.Now.ToString(format)}] {message}";
        }

        context.Log(message);
    }
}
