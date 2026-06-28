using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the USE PASSWORD = '...' statement, setting the script-level password for encryption/decryption.
/// </summary>
public class UsePasswordStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(UsePasswordStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (UsePasswordStatement)statement;
        if (stmt.Prompt)
        {
            var promptProvider = context.ServiceProvider.GetService<IPasswordPromptProvider>()
                ?? ConsolePasswordPromptProvider.Instance;
            context.ScriptPassword = !string.IsNullOrEmpty(context.MasterPassword)
                ? context.MasterPassword
                : promptProvider.ReadPassword("ETL-SQL password: ");
        }
        else
        {
            context.ScriptPassword = stmt.Password;
        }

        if (context.IsVerbose)
        {
            var masked = stmt.ToSql(true);
            context.Log($"Script password set: {masked}");
        }

        return Task.CompletedTask;
    }
}
