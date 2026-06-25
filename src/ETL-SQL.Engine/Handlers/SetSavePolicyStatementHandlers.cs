using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers;
public class SetNoSaveSensitiveStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetNoSaveSensitiveStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetNoSaveSensitiveStatement)statement;
        context.NoSaveSensitive = stmt.Enabled;

        if (stmt.Enabled)
            context.Log("Warning: NO_SAVE_SENSITIVE is ON. Save helpers will remove sensitive literals from saved source.", ConsoleColor.Yellow);
        else if (context.IsVerbose)
            context.Log("NO_SAVE_SENSITIVE set to OFF");

        return Task.CompletedTask;
    }
}

public class SetNoSaveConnectionStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetNoSaveConnectionStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetNoSaveConnectionStatement)statement;
        context.NoSaveConnection = stmt.Enabled;

        if (stmt.Enabled)
            context.Log("Warning: NO_SAVE_CONNECTION is ON. Save helpers will replace connection details with placeholders.", ConsoleColor.Yellow);
        else if (context.IsVerbose)
            context.Log("NO_SAVE_CONNECTION set to OFF");

        return Task.CompletedTask;
    }
}

public class SetConnectionEncryptionStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetConnectionEncryptionStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetConnectionEncryptionStatement)statement;
        context.ConnectionEncryption = stmt.Enabled;

        if (stmt.Enabled)
            context.Log("CONNECTION_ENCRYPTION is ON. Save helpers will encrypt connection targets and string options.", ConsoleColor.Yellow);
        else if (context.IsVerbose)
            context.Log("CONNECTION_ENCRYPTION set to OFF");

        return Task.CompletedTask;
    }
}
