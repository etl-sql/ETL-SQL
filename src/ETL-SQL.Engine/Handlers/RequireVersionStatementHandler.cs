using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers;
public class RequireVersionStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(RequireVersionStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (RequireVersionStatement)statement;

        if (!Version.TryParse(LanguageMetadata.EngineVersion, out var currentVersion))
        {
            // Fallback if version string is non-numeric (e.g. dev build)
            return Task.CompletedTask;
        }

        if (!Version.TryParse(stmt.Version, out var requiredVersion))
        {
            throw new ExecutionException($"Invalid version string in REQUIRE statement: '{stmt.Version}'", null, stmt.Line, stmt.Column);
        }

        bool satisfied = stmt.Operator switch
        {
            ">=" => currentVersion >= requiredVersion,
            ">" => currentVersion > requiredVersion,
            "<=" => currentVersion <= requiredVersion,
            "<" => currentVersion < requiredVersion,
            "=" => currentVersion == requiredVersion,
            _ => throw new ExecutionException($"Unsupported operator '{stmt.Operator}' in REQUIRE statement", null, stmt.Line, stmt.Column)
        };

        if (!satisfied)
        {
            throw new ExecutionException(
                $"This script requires ETL-SQL version {stmt.Operator} {stmt.Version}, but the current engine version is {LanguageMetadata.EngineVersion}.",
                null, stmt.Line, stmt.Column);
        }

        return Task.CompletedTask;
    }
}
