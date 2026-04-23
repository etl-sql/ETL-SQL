using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles various DOCKER actions like START, STOP, PAUSE, RESUME, and CLOSE on existing containers.
    /// </summary>
    public class DockerActionStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(DockerActionStatement);

        public DockerActionStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the specified Docker action on the targeted container(s).</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            if (statement is not DockerActionStatement actionStmt) return;

            if (actionStmt.TargetMode == DockerTargetMode.All)
            {
                _logger.Debug("Docker Action: {Action} on ALL", actionStmt.Action);
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would execute Docker {actionStmt.Action} on ALL containers", ConsoleColor.Yellow);
                    return;
                }

                if (actionStmt.Action == DockerAction.Close)
                {
                    await context.DockerManager.CloseContainers(null);
                }
                else
                {
                    var state = context.DockerManager.GetState();
                    foreach (var alias in state.Keys)
                    {
                        await ExecuteAction(actionStmt.Action, alias, context);
                    }
                }
            }
            else
            {
                string? alias = actionStmt.TargetMode == DockerTargetMode.Single ? actionStmt.Alias : context.DockerManager.LastAlias;
                _logger.Debug("Docker Action: {Action} on {Alias}", actionStmt.Action, alias ?? "(last)");
                
                if (context.IsWhatIf)
                {
                    _logger.WriteLine($"WHAT IF: Would execute Docker {actionStmt.Action} on {alias ?? "last container"}", ConsoleColor.Yellow);
                    return;
                }

                await ExecuteAction(actionStmt.Action, alias, context);
            }
        }

        private async Task ExecuteAction(DockerAction action, string? alias, IExecutionContext context)
        {
            switch (action)
            {
                case DockerAction.Start:
                case DockerAction.Resume:
                    await context.DockerManager.ResumeContainer(alias);
                    break;
                case DockerAction.Stop:
                    await context.DockerManager.StopContainer(alias);
                    break;
                case DockerAction.Pause:
                    await context.DockerManager.PauseContainer(alias);
                    break;
                case DockerAction.Close:
                    await context.DockerManager.CloseContainers(alias);
                    break;
            }
        }
    }
}
