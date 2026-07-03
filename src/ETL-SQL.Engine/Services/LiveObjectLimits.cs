using System;
using System.Linq;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Services;

internal static class LiveObjectLimits
{
    public static void EnsureConnectionCapacity(IExecutionContext context)
        => Ensure("connections", context.MaxConnectionsPerScript,
            context.Connections.Keys.Count(name => !name.StartsWith("#", StringComparison.Ordinal)));

    public static void EnsureTempTableCapacity(IExecutionContext context)
        => Ensure("#temp tables", context.MaxTempTablesPerScript,
            context.Connections.Keys.Count(name => name.StartsWith("#", StringComparison.Ordinal)));

    public static void EnsureVariableCapacity(IExecutionContext context)
        => Ensure("variables", context.MaxVariablesPerScript, context.VarContext.CurrentVariables.Count);

    public static void EnsureVisualCapacity(IExecutionContext context)
        => Ensure("visuals", context.MaxVisualsPerScript, context.ReportContext.VisualDefinitions.Count);

    private static void Ensure(string objectKind, int limit, int currentCount)
    {
        if (limit > 0 && currentCount >= limit)
        {
            throw new ExecutionException(
                $"Live {objectKind} limit exceeded ({limit}). Drop or reuse an existing object, " +
                $"split the script, or raise the corresponding Engine:Max*PerScript setting.");
        }
    }
}
