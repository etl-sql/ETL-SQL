using System;

namespace ETL_SQL.Core
{
    public class ExecutionMetrics
    {
        public string Sql { get; set; } = string.Empty;
        public long DurationMs { get; set; }
        public long MemoryDeltaBytes { get; set; }
        public long RowsProcessed { get; set; }
        public long SpilledBytes { get; set; }
        public long SubqueryCacheHits { get; set; }
        public long SubqueryCacheMisses { get; set; }
        public long SubquerySpilledBytes { get; set; }
        public int PartitionsCount { get; set; }
        public int RecursiveDepth { get; set; }
        public string? IndexName { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
