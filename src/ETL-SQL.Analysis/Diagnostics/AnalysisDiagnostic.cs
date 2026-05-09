using ETL_SQL.Core.Common;

namespace ETL_SQL.Analysis.Diagnostics
{
    public sealed record AnalysisDiagnostic(
        int StartLine,
        int StartColumn,
        int EndLine,
        int EndColumn,
        DiagnosticSeverity Severity,
        string Message,
        string? Code,
        string Source);
}
