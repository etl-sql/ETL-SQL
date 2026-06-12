using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles block statements (BEGIN...END), executing a sequence of nested statements.
    /// </summary>
    public class BlockStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(BlockStatement);
        /// <summary>Executes each statement within the block sequentially.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (BlockStatement)statement;

            for (int i = 0; i < stmt.Statements.Count; i++)
            {
                var s = stmt.Statements[i];
                try
                {
                    await context.EvaluateStatement(s);
                }
                catch (GotoException gotoEx)
                {
                    int targetIdx = -1;
                    for (int j = 0; j < stmt.Statements.Count; j++)
                    {
                        if (stmt.Statements[j] is SectionLabelStatement sls && sls.LabelName.Equals(gotoEx.LabelName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetIdx = j;
                            break;
                        }
                    }

                    if (targetIdx >= 0)
                    {
                        i = targetIdx - 1; // -1 because loop increment will do i++
                        continue;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
    }
}
