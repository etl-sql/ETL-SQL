using System.Threading.Tasks;

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
            
            
            foreach (var s in stmt.Statements)
            {
                await context.EvaluateStatement(s);
            }
        }
    }
}



