using System;

namespace ETL_SQL.Core.Common.Exceptions
{
    public class ETLException : Exception
    {
        public ETLException(string message) : base(message) { }
        public ETLException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class SyntaxException : ETLException
    {
        public int Line { get; }
        public int Column { get; }

        public SyntaxException(string message, int line = 0, int column = 0) 
            : base($"{message} at line {line}, col {column}")
        {
            Line = line;
            Column = column;
        }
    }

    public class ExecutionException : ETLException
    {
        public string? StatementContext { get; }
        public int Line { get; }
        public int Column { get; }

        public ExecutionException(string message, string? statementContext = null, int line = 0, int column = 0) : base(message)
        {
            StatementContext = statementContext;
            Line = line;
            Column = column;
        }

        public ExecutionException(string message, Exception innerException, string? statementContext = null, int line = 0, int column = 0) 
            : base(message, innerException)
        {
            StatementContext = statementContext;
            Line = line;
            Column = column;
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
}
