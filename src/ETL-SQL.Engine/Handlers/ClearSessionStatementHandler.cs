using ETL_SQL.Core;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CLEAR SESSION statement, deleting session files and temporary data from disk.
    /// </summary>
    public class ClearSessionStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ClearSessionStatement);

        /// <summary>Executes the CLEAR SESSION statement in the current context.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ClearSessionStatement)statement;
            var sessionStateManager = context.SessionStateManager;
            var logger = context.LoggingContext.Logger;

            switch (stmt.Mode)
            {
                case ClearSessionMode.Current:
                    if (context.DataContext.SessionId != null)
                    {
                        // Note: Self-clearing is allowed even though it's "in-use" 
                        // because we want scripts to be able to cleanup themselves.
                        sessionStateManager.UnregisterActiveSession(context.DataContext.SessionId);
                        sessionStateManager.ClearSession(context.DataContext.SessionId);
                        logger.Info("Cleared current session: {SessionId}", context.DataContext.SessionId);
                        
                        // We must cast if we need to null it out, or use a new method
                        if (context is IEngineContext engineCtx && engineCtx is ETL_SQL.Engine.Evaluator eval)
                        {
                            eval.SessionId = null; // Prevent future saves
                        }

                        // NEW: Clear live memory (visuals, variables, temp tables)
                        await context.ResetSessionAsync();
                    }
                    break;

                case ClearSessionMode.Single:
                    if (stmt.SessionId != null)
                    {
                        var targetId = await context.EvaluationContext.EvaluateValue(stmt.SessionId, new Row());
                        if (targetId != null)
                        {
                            sessionStateManager.ClearSession(targetId.ToString()!);
                            logger.Info("Cleared specific session: {SessionId}", targetId);
                        }
                    }
                    break;

                case ClearSessionMode.All:
                    var sessions = sessionStateManager.GetSessions().ToList();
                    int clearedCount = 0;
                    foreach (var s in sessions)
                    {
                        if (s.SessionId != context.DataContext.SessionId) // Don't self-destruct in "ALL" mode unless current
                        {
                            if (!sessionStateManager.IsSessionInUse(s.SessionId))
                            {
                                sessionStateManager.ClearSession(s.SessionId);
                                clearedCount++;
                            }
                        }
                    }
                    logger.Info("Cleared {Count} inactive sessions.", clearedCount);
                    break;

                case ClearSessionMode.Stale:
                    var retentionDays = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(context.ServiceProvider).GetValue<int>("Session:StaleSessionRetentionDays", 7);
                    sessionStateManager.ReapStaleSessions(TimeSpan.FromDays(retentionDays));
                    logger.Info("Reaped stale sessions older than {Days} days.", retentionDays);
                    break;
            }
        }
    }
}
