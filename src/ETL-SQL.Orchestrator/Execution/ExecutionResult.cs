using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Linting;
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
        public bool Success { get; set; }
        /// <summary>Captured log messages for display in the TUI.</summary>
        public List<string> Messages { get; set; } = new();
        /// <summary>Active connections captured from the engine after execution, used for TUI autocomplete.</summary>
        public Dictionary<string, IDataSource> ActiveConnections { get; set; } = new();
    }
}
