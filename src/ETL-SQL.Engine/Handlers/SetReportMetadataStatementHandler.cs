using System;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers;
public class SetReportMetadataStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetReportMetadataStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetReportMetadataStatement)statement;
        switch (stmt.Key.ToUpperInvariant())
        {
            case "TITLE": context.ReportContext.ReportTitle = stmt.Value; break;
            case "DESCRIPTION": context.ReportContext.ReportDescription = stmt.Value; break;
            case "CSS": context.ReportContext.ReportCss = stmt.Value; break;
            case "JS": context.ReportContext.ReportJs = stmt.Value; break;
            case "HEAD": context.ReportContext.ReportHtmlHead = stmt.Value; break;
            case "BODY": context.ReportContext.ReportHtmlBody = stmt.Value; break;
            case "FOOTER": context.ReportContext.ReportHtmlFooter = stmt.Value; break;
            case "FAVICON": context.ReportContext.ReportFavicon = stmt.Value; break;
            case "LOGO": context.ReportContext.ReportLogo = stmt.Value; break;
            case "BACKGROUND": context.ReportContext.ReportBackground = stmt.Value; break;
            case "THEME": context.ReportContext.ReportTheme = stmt.Value; break;
            case "NAVIGATION": context.ReportContext.ReportNavigation = stmt.Value; break;
        }
        return Task.CompletedTask;
    }
}

