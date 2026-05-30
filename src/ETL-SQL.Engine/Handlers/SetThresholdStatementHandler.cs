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

            int intVal = 0;
            if (s.Type != ThresholdType.LineageNamespace)
            {
                intVal = Convert.ToInt32(val);
            }
            
            if (s.Type != ThresholdType.LineageNamespace)
            {
                if (s.Type == ThresholdType.ExternalHashPartitions && intVal < 1)
                {
                    throw new ExecutionException("EXTERNAL_HASH_PARTITIONS must be at least 1.");
                }

                if ((s.Type == ThresholdType.BatchSize || 
                     s.Type == ThresholdType.ForeachPageSize || 
                     s.Type == ThresholdType.ExternalSortChunkSize || 
                     s.Type == ThresholdType.MaxMessages) && intVal < 1)
                {
                    throw new ExecutionException($"{s.Type.ToString().ToUpperInvariant()} must be at least 1.");
                }

                if (s.Type == ThresholdType.MaxSmtpEmailsPerScript && intVal < 0)
                {
                    throw new ExecutionException("MAX_SMTP_EMAILS_PER_SCRIPT must be zero or greater.");
                }
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
                case ThresholdType.MaxGroupingSets:
                    context.MaxGroupingSets = intVal;
                    break;
                case ThresholdType.MaxSessionSize:
                    context.MaxSessionSize = Convert.ToInt64(val);
                    break;
                case ThresholdType.Telemetry:
                    context.TelemetryEnabled = Convert.ToBoolean(val);
                    break;
                case ThresholdType.TempTableSpill:
                    context.TempTableSpillThresholdRows = Convert.ToInt64(val);
                    break;
                case ThresholdType.MaxLastResultRows:
                    context.MaxLastResultRows = intVal;
                    break;
                case ThresholdType.MaxGenerateRows:
                    context.MaxGenerateRows = intVal;
                    break;
                case ThresholdType.MaxSmtpEmailsPerScript:
                    context.MaxSmtpEmailsPerScript = intVal;
                    break;
                case ThresholdType.MaxInternalOperations:
                    context.SecurityService.MaxInternalOperations = intVal;
                    break;
                case ThresholdType.InteractiveMode:
                    context.InteractiveMode = Convert.ToBoolean(val);
                    break;
                case ThresholdType.CaseSensitive:
                    context.CaseSensitiveComparison = Convert.ToBoolean(val);
                    break;
                case ThresholdType.Lineage:
                    context.LineageEnabled = Convert.ToBoolean(val);
                    break;
                case ThresholdType.LineageNamespace:
                    context.LineageNamespace = val.ToString();
                    break;
                case ThresholdType.LineageImportCatalog:
                    context.LineageImportCatalog = Convert.ToBoolean(val);
                    break;
                case ThresholdType.TruncateString:
                    context.TruncateString = Convert.ToBoolean(val);
                    break;
                case ThresholdType.SkipError:
                    context.SkipError = Convert.ToBoolean(val);
                    break;
            }


            if (context.IsVerbose)
            {
                context.Logger.Debug("Set {Type} to {Value}", s.Type, s.Type == ThresholdType.LineageNamespace ? val : intVal);
            }
        }
    }
}

