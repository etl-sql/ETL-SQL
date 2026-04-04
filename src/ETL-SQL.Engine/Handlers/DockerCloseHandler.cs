using System;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the DOCKER CLOSE statement, stopping and removing Docker containers.
    /// </summary>
    public class DockerCloseHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(DockerCloseStatement);
        /// <summary>Executes the DOCKER CLOSE statement, cleaning up containers by name or alias.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            if (statement is not DockerCloseStatement stmt) return;
            
            
            string? nameOrAlias = stmt.Alias;
            if (nameOrAlias == null && stmt.ImageName != null)
            {
                var val = await context.EvaluateValue(stmt.ImageName, new Data.Row());
                nameOrAlias = val?.ToString();
            }

            await context.DockerManager.CloseContainers(nameOrAlias);
        }
    }
}



