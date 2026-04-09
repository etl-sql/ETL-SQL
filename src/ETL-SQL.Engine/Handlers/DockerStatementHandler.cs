using System;
using ETL_SQL.Core.Common.Exceptions;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DOCKER RUN statement, initializing and starting a new Docker container.
    /// </summary>
    public class DockerStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(DockerStatement);

        public DockerStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the DOCKER RUN statement, resolving the image and starting the container.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            if (statement is not DockerStatement dockerStmt) return;

            
            var imageName = (await context.EvaluateValue(dockerStmt.ImageName, new Data.Row()))?.ToString();

            if (string.IsNullOrEmpty(imageName))
            {
                throw new ExecutionException("Docker image name cannot be null or empty.");
            }

            // Start the container and track it in the evaluator's manager
            _logger.WriteLine($"Initializing Docker container: {imageName} with alias {dockerStmt.Alias ?? "none"}...", ConsoleColor.Cyan);
            
            if (context.IsWhatIf)
            {
                _logger.WriteLine($"WHAT IF: Would start Docker container: {imageName}", ConsoleColor.Yellow);
                return;
            }

            await context.DockerManager.StartContainer(imageName, dockerStmt.Alias);
            
            _logger.WriteLine($"Docker container started: {imageName}", ConsoleColor.Green);
        }
    }
}
