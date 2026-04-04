namespace ETL_SQL.Core.Linting
{
    public enum LintSeverity
    {
        Info,
        Warning,
        Error
    }

    public class LintResult
    {
        public string RuleName { get; set; } = string.Empty;
        public LintSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
    }
}
