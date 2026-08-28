using System.Collections.Generic;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;

namespace ETL_SQL.Orchestrator.Execution
{
    /// <summary>
    /// Represents the full results of a script execution.
    /// All data is returned as raw types — rendering (Spectre tables, chart widgets, etc.)
    /// is the responsibility of the presentation layer (TUI, web, etc.).
    /// </summary>
    public class ExecutionResult
    {
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public List<LintResult> LintResults { get; set; } = new();
        /// <summary>Raw execution tree — convert to IRenderable in the TUI layer.</summary>
        public ExecutionTree? ExecutionTree { get; set; }
        /// <summary>All result sets produced by the script, in order.</summary>
        public List<DataTable> ResultsTables { get; set; } = new();
        public long ExecutionTimeMs { get; set; }
        public long RowsProcessed { get; set; }
        /// <summary>Rows removed from output by an <c>@expect</c> QUARANTINE action during this run.</summary>
        public long RowsQuarantined { get; set; }
        /// <summary>Rows that failed a WARN rule but still reached the target during this run.</summary>
        public long RowsWarned { get; set; }
        /// <summary>Compact per-rule failure counts (<c>column:rule=count;…</c>); null when no rule failed.</summary>
        public string? DataQualityFailures { get; set; }
        /// <summary>Column-level run metrics collected for ASSERT JOB predicates.</summary>
        public List<DataQualityColumnMetric> DataQualityColumnMetrics { get; set; } = new();
        /// <summary>Structured counts-only rule failures; never contains sample values.</summary>
        public List<DataQualityRuleFailureMetric> DataQualityRuleFailures { get; set; } = new();
        public bool Success { get; set; }
        /// <summary>False when retrying could duplicate an externally applied mutation.</summary>
        public bool RetryAllowed { get; set; } = true;
        /// <summary>Captured log messages for display in the TUI.</summary>
        public List<LogEntry> Messages { get; set; } = new();
        /// <summary>Active connections captured from the engine after execution, used for TUI autocomplete.</summary>
        public Dictionary<string, IDataSource> ActiveConnections { get; set; } = new();
    }
}
