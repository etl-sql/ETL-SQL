using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles various DOCKER actions like START, STOP, PAUSE, RESUME, and CLOSE on existing containers.
/// </summary>
public class DockerActionStatementHandler : IStatementHandler
{
    private readonly ILogger _logger;
    private readonly IConfiguration? _config;
    public Type SupportedStatementType => typeof(DockerActionStatement);

    public DockerActionStatementHandler(ILogger logger, IConfiguration? config = null)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>Executes the specified Docker action on the targeted container(s).</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        if (statement is not DockerActionStatement actionStmt) return;

        var scope = DockerScope.For(context);
        if (actionStmt.TargetMode == DockerTargetMode.All)
        {
            _logger.Debug("Docker Action: {Action} on ALL", actionStmt.Action);
            var state = context.DockerManager.GetState(scope);
            if (actionStmt.Action is DockerAction.Start or DockerAction.Resume)
            {
                foreach (var alias in state.Keys)
                    EnsureActionAuthorized(actionStmt.Action, alias, context);
            }

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would execute Docker {actionStmt.Action} on ALL containers", ConsoleColor.Yellow);
                return;
            }

            if (actionStmt.Action == DockerAction.Close)
            {
                await context.DockerManager.CloseContainers(scope, null, context.CancellationToken);
            }
            else
            {
                foreach (var alias in state.Keys)
                {
                    await ExecuteAction(actionStmt.Action, alias, context);
                }
            }
        }
        else
        {
            string? alias = actionStmt.TargetMode == DockerTargetMode.Single ? actionStmt.Alias : context.DockerManager.GetLastAlias(scope);
            _logger.Debug("Docker Action: {Action} on {Alias}", actionStmt.Action, alias ?? "(last)");

            EnsureActionAuthorized(actionStmt.Action, alias, context);

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would execute Docker {actionStmt.Action} on {alias ?? "last container"}", ConsoleColor.Yellow);
                return;
            }

            await ExecuteAction(actionStmt.Action, alias, context);
        }
    }

    private void EnsureActionAuthorized(DockerAction action, string? alias, IExecutionContext context)
    {
        if (action is not (DockerAction.Start or DockerAction.Resume))
            return;

        var scope = DockerScope.For(context);
        var effectiveAlias = alias ?? context.DockerManager.GetLastAlias(scope);
        if (string.IsNullOrWhiteSpace(effectiveAlias))
            return;

        var trackedImage = context.DockerManager.GetImageName(scope, effectiveAlias);
        var state = context.DockerManager.GetState(scope);
        if (string.IsNullOrWhiteSpace(trackedImage))
        {
            if (state.ContainsKey(effectiveAlias))
            {
                throw new ExecutionException(
                    $"Docker container '{effectiveAlias}' has no approved image record and cannot be started or resumed.");
            }
            return;
        }

        ProcessPolicyRules.EnforceDockerImage(context, trackedImage, _config);
    }

    private async Task ExecuteAction(DockerAction action, string? alias, IExecutionContext context)
    {
        switch (action)
        {
            case DockerAction.Start:
            case DockerAction.Resume:
                EnsureActionAuthorized(action, alias, context);
                await context.DockerManager.ResumeContainer(DockerScope.For(context), alias, context.CancellationToken);
                break;
            case DockerAction.Stop:
                await context.DockerManager.StopContainer(DockerScope.For(context), alias, context.CancellationToken);
                break;
            case DockerAction.Pause:
                await context.DockerManager.PauseContainer(DockerScope.For(context), alias, context.CancellationToken);
                break;
            case DockerAction.Close:
                await context.DockerManager.CloseContainers(DockerScope.For(context), alias, context.CancellationToken);
                break;
        }
    }
}
