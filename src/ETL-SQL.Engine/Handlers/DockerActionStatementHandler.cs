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

        /// <summary>Executes the specified Docker action on the targeted container.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            if (statement is not DockerActionStatement actionStmt) return;

            _logger.Debug($"Docker Action: {actionStmt.Action} on {actionStmt.Alias}");

            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would execute Docker {actionStmt.Action} on {actionStmt.Alias}", ConsoleColor.Yellow);
                return;
            }

            switch (actionStmt.Action)
            {
                case DockerAction.Start:
                    await context.DockerManager.ResumeContainer(actionStmt.Alias);
                    break;
                case DockerAction.Stop:
                    await context.DockerManager.StopContainer(actionStmt.Alias);
                    break;
                case DockerAction.Pause:
                    await context.DockerManager.PauseContainer(actionStmt.Alias);
                    break;
                case DockerAction.Resume:
                    await context.DockerManager.ResumeContainer(actionStmt.Alias);
                    break;
                case DockerAction.Close:
                    await context.DockerManager.CloseContainers(actionStmt.Alias);
                    break;
            }
        }
    }
}
