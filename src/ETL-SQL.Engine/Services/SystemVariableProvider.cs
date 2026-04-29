using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Services
{
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
            
            return null;
        }
    }
}

