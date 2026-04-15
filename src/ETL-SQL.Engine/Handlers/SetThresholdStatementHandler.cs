using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    public class SetThresholdStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SetThresholdStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var s = (SetThresholdStatement)statement;
            var val = await context.EvaluateValue(s.Value, new Row());
            
            if (val == null) return;

            int intVal = Convert.ToInt32(val);

            switch (s.Type)
            {
                case ThresholdType.JoinSpill:
                    context.JoinSpillThreshold = intVal;
                    break;
                case ThresholdType.WindowSpill:
                    context.WindowSpillThreshold = intVal;
                    break;
                case ThresholdType.ExternalHashPartitions:
                    context.ExternalHashPartitions = intVal;
                    break;
                case ThresholdType.ExternalSortChunkSize:
                    context.ExternalSortChunkSize = intVal;
                    break;
                case ThresholdType.BatchSize:
                    context.BatchSize = intVal;
                    break;
                case ThresholdType.MaxRecursiveDepth:
                    context.MaxRecursiveDepth = intVal;
                    break;
                case ThresholdType.MaxInMemoryBatches:
                    context.MaxInMemoryBatches = intVal;
                    break;
                case ThresholdType.ForeachPageSize:
                    context.ForeachPageSize = intVal;
                    break;
                case ThresholdType.MaxMessages:
                    context.MaxMessages = intVal;
                    break;
            }

            if (context.IsVerbose)
            {
                context.Logger.Debug("Set {Type} to {Value}", s.Type, intVal);
            }
        }
    }
}
