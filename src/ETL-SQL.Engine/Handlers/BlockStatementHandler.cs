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

            // Build a label → index map once upfront so that GOTO resolution is O(1)
            // instead of O(n) per throw. Only built when the block actually contains labels.
            Dictionary<string, int>? labelIndex = null;
            for (int k = 0; k < stmt.Statements.Count; k++)
            {
                if (stmt.Statements[k] is SectionLabelStatement lbl)
                {
                    labelIndex ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    labelIndex[lbl.LabelName] = k;
                }
            }

            for (int i = 0; i < stmt.Statements.Count; i++)
            {
                var s = stmt.Statements[i];
                try
                {
                    await context.EvaluateStatement(s);
                }
                catch (GotoException gotoEx)
                {
                    if (labelIndex != null && labelIndex.TryGetValue(gotoEx.LabelName, out int targetIdx))
                    {
                        i = targetIdx - 1; // -1 because loop increment will do i++
                        continue;
                    }
                    throw;
                }
            }
        }
    }
}
