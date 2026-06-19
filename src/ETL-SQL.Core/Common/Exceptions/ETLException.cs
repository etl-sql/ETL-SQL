using System;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Core.Common.Exceptions
{
    public class ETLException : Exception
    {
        public ETLException(string message) : base(SecretRedactor.Redact(message) ?? string.Empty) { }
        public ETLException(string message, Exception innerException) : base(SecretRedactor.Redact(message) ?? string.Empty, innerException) { }
    }

    public class SyntaxException : ETLException
    {
        public int Line { get; }
        public int Column { get; }

        public SyntaxException(string message, int line = 0, int column = 0)
            : base(Sanitize(message, line, column))
        {
            Line = line;
            Column = column;
        }

        private static string Sanitize(string message, int line, int column)
        {
            if (string.IsNullOrEmpty(message)) return $"at line {line}, col {column}";
            var sanitized = SecretRedactor.Redact(message);
            return $"{sanitized} at line {line}, col {column}";
        }
    }

    public class ExecutionException : ETLException
    {
        public string? StatementContext { get; }
        public int Line { get; }
        public int Column { get; }
        public int ErrorNumber { get; }
        public int Severity { get; }
        public int State { get; }

        public ExecutionException(string message, string? statementContext = null, int line = 0, int column = 0, int errorNumber = 50000, int severity = 16, int state = 1) : base(message)
        {
            StatementContext = statementContext;
            Line = line;
            Column = column;
            ErrorNumber = errorNumber;
            Severity = severity;
            State = state;
        }

        public ExecutionException(string message, Exception innerException, string? statementContext = null, int line = 0, int column = 0, int errorNumber = 50000, int severity = 16, int state = 1)
            : base(message, innerException)
        {
            StatementContext = statementContext;
            Line = line;
            Column = column;
            ErrorNumber = errorNumber;
            Severity = severity;
            State = state;
        }
    }

    public class ConnectionException : ExecutionException
    {
        public string ConnectionAlias { get; }
        public ConnectionException(string message, string alias, Exception? inner = null)
            : base($"Connection '{alias}' failed: {message}", inner!, null)
        {
            ConnectionAlias = alias;
        }
    }

    public class RowSkipException : ETLException
    {
        public RowSkipException(string message) : base(message) { }
    }
}
