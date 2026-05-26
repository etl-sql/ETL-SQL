using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles section labels (e.g. LabelName:).
    /// If the label is at the top-level and session persistence is active, it triggers a state checkpoint.
    /// </summary>
    public class SectionLabelStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;

        public Type SupportedStatementType => typeof(SectionLabelStatement);

        public SectionLabelStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var labelStmt = (SectionLabelStatement)statement;

            if (labelStmt.IsTopLevel && context.IsPersistentSession && !string.IsNullOrEmpty(context.SessionId))
            {
                _logger.Info("[CHECKPOINT] Saving state at top-level checkpoint label '{LabelName}'...", labelStmt.LabelName);

                if (!context.VarContext.ContainsVariable("@_LAST_CHECKPOINT_LABEL"))
                {
                    context.VarContext.DeclareVariable("@_LAST_CHECKPOINT_LABEL", labelStmt.LabelName);
                }
                else
                {
                    context.VarContext.SetVariable("@_LAST_CHECKPOINT_LABEL", labelStmt.LabelName);
                }

                await context.SessionStateManager.SaveSession(context.SessionId, context);
            }
        }
    }
}
