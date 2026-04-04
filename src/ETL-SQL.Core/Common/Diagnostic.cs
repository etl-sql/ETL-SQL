using System;

namespace ETL_SQL.Core.Common
{
    public enum DiagnosticSeverity
    {
        Error,
        Warning,
        Info,
        Hint
    }

    public class Diagnostic
    {
        public string Message { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Error;
        public string? Code { get; set; }
        public string Source { get; set; } = "Parser";

        public Diagnostic() { }

        public Diagnostic(string message, int line, int column, DiagnosticSeverity severity = DiagnosticSeverity.Error, string? code = null)
        {
            Message = message;
            Line = line;
            Column = column;
            Severity = severity;
            Code = code;
        }
    }
}
