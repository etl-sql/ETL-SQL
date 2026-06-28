namespace ETL_SQL.Analysis.Linting;

using ETL_SQL.Core.Governance;

public enum LintSeverity
{
    Info,
    Warning,
    Error
}

public class LintResult
{
    public string RuleName { get; set; } = string.Empty;
    public string? Code { get; set; }
    public LintSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public int ColumnNumber { get; set; }
    public GovernancePolicyDecision? PolicyDecision { get; set; }
}
