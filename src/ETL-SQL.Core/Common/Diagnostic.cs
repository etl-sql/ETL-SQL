using System;
using ETL_SQL.Core.Governance;

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
        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set => _message = Sanitize(value);
        }
        public int Line { get; set; }
        public int Column { get; set; }
        public DiagnosticSeverity Severity { get; set; } = DiagnosticSeverity.Error;
        public string? Code { get; set; }
        public string Source { get; set; } = "Parser";
        public GovernancePolicyDecision? PolicyDecision { get; set; }

        public Diagnostic() { }

        public Diagnostic(string message, int line, int column, DiagnosticSeverity severity = DiagnosticSeverity.Error, string? code = null)
        {
            Message = message;
            Line = line;
            Column = column;
            Severity = severity;
            Code = code;
        }

        private static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            var sanitized = System.Text.RegularExpressions.Regex.Replace(
                message,
                @"ENC:[A-Za-z0-9+/=]+",
                "ENC:********",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"(PASSWORD|PWD|SECRET|APIKEY|API_KEY|TOKEN|CREDENTIAL|PRIVATEKEY)\s*=\s*['""]?[^'""\s,;]+['""]?",
                "$1=********",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return sanitized;
        }
    }
}
