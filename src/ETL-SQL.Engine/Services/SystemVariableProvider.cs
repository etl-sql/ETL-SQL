using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Handles resolution of system-prefixed variables (@@).
/// </summary>
public static class SystemVariableProvider
{
    public static bool IsSystemVariable(string name) => name.StartsWith("@@");

    public static object? Resolve(string name, IExecutionContext context)
    {
        if (name.Equals("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)) return context.TranCount;
        if (name.Equals("@@RESULTSETS", StringComparison.OrdinalIgnoreCase)) return context.LastResultSets;
        if (name.Equals("@@VERSION", StringComparison.OrdinalIgnoreCase)) return LanguageMetadata.GetFullVersionString();
        if (name.Equals("@@ROWCOUNT", StringComparison.OrdinalIgnoreCase)) return context.Telemetry.LastStatementRowsProcessed;
        if (name.Equals("@@ERROR", StringComparison.OrdinalIgnoreCase)) return context.PreviousErrorNumber;
        if (name.Equals("@@TOTAL_SPILLED_BYTES", StringComparison.OrdinalIgnoreCase)) return context.Telemetry.TotalSpilledBytes;
        if (name.Equals("@@PARTITIONS_COUNT", StringComparison.OrdinalIgnoreCase)) return context.Telemetry.PartitionsCount;
        if (name.Equals("@@AGGREGATE_GROUPS_COUNT", StringComparison.OrdinalIgnoreCase)) return context.Telemetry.AggregateGroupsCount;
        if (name.Equals("@@AGGREGATE_EXPANSION_RATIO", StringComparison.OrdinalIgnoreCase)) return context.Telemetry.AggregateExpansionRatio;
        if (name.Equals("@@SUBQUERY_CACHE_HITS", StringComparison.OrdinalIgnoreCase)) return context.Telemetry.SubqueryCacheHits;
        if (name.Equals("@@SUBQUERY_CACHE_MISSES", StringComparison.OrdinalIgnoreCase)) return context.Telemetry.SubqueryCacheMisses;

        // Identity variables for row-level security. Null when no identity was injected, so a
        // well-formed predicate (WHERE HAS_GROUP(...)) fails closed rather than exposing all rows.
        if (name.Equals("@@CURRENT_USER", StringComparison.OrdinalIgnoreCase)) return context.ExecutionIdentity?.EffectiveUser;
        if (name.Equals("@@CURRENT_USER_ID", StringComparison.OrdinalIgnoreCase)) return context.ExecutionIdentity?.EffectiveUserId;
        if (name.Equals("@@REAL_USER", StringComparison.OrdinalIgnoreCase)) return context.ExecutionIdentity?.RealUser;
        if (name.Equals("@@IS_ADMIN", StringComparison.OrdinalIgnoreCase)) return context.ExecutionIdentity?.IsAdmin ?? false;

        return null;
    }
}

