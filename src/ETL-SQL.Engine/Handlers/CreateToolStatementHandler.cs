using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles CREATE TOOL statements.
/// Registers the tool definition in memory for the current session.
/// (In a full enterprise deployment, this would be pushed to a catalog).
/// </summary>
public class CreateToolStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;
    public Type SupportedStatementType => typeof(CreateToolStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateToolStatement)statement;

        var alreadyExists = context.ReportContext.ToolDefinitions.ContainsKey(stmt.ToolName);
        if (stmt.Mode == ObjectCreationMode.Create && alreadyExists)
        {
            throw new ExecutionException($"Tool '{stmt.ToolName}' already exists.", null, stmt.Line, stmt.Column);
        }

        context.ReportContext.ToolDefinitions[stmt.ToolName] = stmt;

        _logger.Debug("Tool '{ToolName}' registered.", stmt.ToolName);
        context.Log($"Tool '{stmt.ToolName}' {(alreadyExists ? "updated" : "created")}.");

        var actor = context.ExecutionIdentity?.RealUser ?? context.ExecutionPolicy?.Actor ?? "system";
        var effective = context.ExecutionIdentity?.EffectiveUser ?? context.ExecutionPolicy?.Actor ?? actor;
        var policy = context.ExecutionPolicy;

        SecurityEventRuntime.Emit(SecurityEventContract.Create(
            SecurityEventSeverity.Information,
            SecurityEventType.CatalogMutation,
            actor,
            effective,
            $"Tool:{stmt.ToolName}",
            SecurityEventDecision.Allowed,
            alreadyExists ? "Updated Tool" : "Created Tool") with
        {
            ScriptHash = policy?.ScriptHash,
            JobId = policy?.JobId,
            CorrelationId = policy?.CorrelationId,
            PolicyVersion = policy?.PolicyVersion,
            PolicyHash = policy?.PolicyHash
        });

        return Task.CompletedTask;
    }
}
