using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;

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
        string Source,
        GovernancePolicyDecision? PolicyDecision = null,
        // Plain-language help for the diagnostics a beginner is likely to hit, attached here rather
        // than in one host's response shape: every surface that shows diagnostics — Studio, VS Code
        // through the language server, the CLI's lint output — builds them through the same builder,
        // and help that only reached one of them would be help most authors never see.
        DiagnosticGuidance? Guidance = null);
}
