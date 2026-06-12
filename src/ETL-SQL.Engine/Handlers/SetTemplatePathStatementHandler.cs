using System;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    public class SetTemplatePathStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;
        public Type SupportedStatementType => typeof(SetTemplatePathStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (SetTemplatePathStatement)statement;
            var val = await context.EvaluateValue(stmt.PathExpression, new Row());

            if (val == null)
            {
                throw new ExecutionException("SET TEMPLATE_PATH: path cannot be null.", null, stmt.Line, stmt.Column);
            }

            string path = val.ToString()!;

            // Security: Use context.ResolvePath to ensure it's within allowed bounds
            string resolvedPath = context.ResolvePath(path);

            context.ReportContext.TemplatePath = resolvedPath;

            _logger.Debug("Template path set to '{Path}'", resolvedPath);
            context.Log($"Template path set to '{resolvedPath}'.");
        }
    }
}

