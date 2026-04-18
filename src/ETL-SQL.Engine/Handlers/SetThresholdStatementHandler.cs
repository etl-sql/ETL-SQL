using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;
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

            context.SecurityService.ValidateThresholdOverride(s.Type, val, context);

            int intVal = Convert.ToInt32(val);
            
            if (s.Type == ThresholdType.ExternalHashPartitions && intVal < 1)
            {
                throw new ExecutionException("EXTERNAL_HASH_PARTITIONS must be at least 1.");
            }


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
                case ThresholdType.MaxParallelDegree:
                    context.MaxParallelDegree = intVal;
                    break;
                case ThresholdType.MaxStringResultSize:
                    context.MaxStringResultSize = Convert.ToInt64(val);
                    break;
                case ThresholdType.RegexMatchTimeout:
                    context.RegexMatchTimeoutMs = intVal;
                    break;
                case ThresholdType.MaxFileOperations:
                    context.MaxFileOperations = intVal;
                    break;
            }

            if (context.IsVerbose)
            {
                context.Logger.Debug("Set {Type} to {Value}", s.Type, intVal);
            }
        }
    }
}
