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
            switch (stmt.Key.ToUpperInvariant())
            {
                case "TITLE":       context.ReportTitle = stmt.Value; break;
                case "DESCRIPTION": context.ReportDescription = stmt.Value; break;
                case "CSS":         context.ReportCss = stmt.Value; break;
                case "JS":          context.ReportJs = stmt.Value; break;
                case "HEAD":        context.ReportHtmlHead = stmt.Value; break;
                case "BODY":        context.ReportHtmlBody = stmt.Value; break;
                case "FOOTER":      context.ReportHtmlFooter = stmt.Value; break;
                case "FAVICON":     context.ReportFavicon = stmt.Value; break;
                case "LOGO":        context.ReportLogo = stmt.Value; break;
                case "BACKGROUND":  context.ReportBackground = stmt.Value; break;
                case "THEME":       context.ReportTheme = stmt.Value; break;
                case "NAVIGATION":  context.ReportNavigation = stmt.Value; break;
            }
            return Task.CompletedTask;
        }
    }
}
