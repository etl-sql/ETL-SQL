using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    public class SetReportMetadataStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(SetReportMetadataStatement);

        public Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetReportMetadataStatement)statement;
            if (stmt.Key == "TITLE")
                context.ReportTitle = stmt.Value;
            else
                context.ReportDescription = stmt.Value;
            return Task.CompletedTask;
        }
    }
}
