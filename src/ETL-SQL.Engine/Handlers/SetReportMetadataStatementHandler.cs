using System;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Reporting;

namespace ETL_SQL.Engine.Handlers;

public class SetReportMetadataStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(SetReportMetadataStatement);

    public Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (SetReportMetadataStatement)statement;
        switch (stmt.Key.ToUpperInvariant())
        {
            case ReportMetadataKeys.Title: context.ReportContext.ReportTitle = stmt.Value; break;
            case ReportMetadataKeys.Description: context.ReportContext.ReportDescription = stmt.Value; break;
            case ReportMetadataKeys.Css: context.ReportContext.ReportCss = stmt.Value; break;
            case ReportMetadataKeys.Js: context.ReportContext.ReportJs = stmt.Value; break;
            case ReportMetadataKeys.Head: context.ReportContext.ReportHtmlHead = stmt.Value; break;
            case ReportMetadataKeys.Body: context.ReportContext.ReportHtmlBody = stmt.Value; break;
            case ReportMetadataKeys.Footer: context.ReportContext.ReportHtmlFooter = stmt.Value; break;
            case ReportMetadataKeys.Favicon: context.ReportContext.ReportFavicon = stmt.Value; break;
            case ReportMetadataKeys.Logo: context.ReportContext.ReportLogo = stmt.Value; break;
            case ReportMetadataKeys.Background: context.ReportContext.ReportBackground = stmt.Value; break;
            case ReportMetadataKeys.Theme: context.ReportContext.ReportTheme = stmt.Value; break;
            case ReportMetadataKeys.Navigation: context.ReportContext.ReportNavigation = stmt.Value; break;
            case ReportMetadataKeys.TimeZone: context.ReportContext.ReportTimeZone = ValidateTimeZone(stmt.Value); break;
            case ReportMetadataKeys.Locale: context.ReportContext.ReportLocale = ValidateLocale(stmt.Value); break;
            case ReportMetadataKeys.NullLabel: context.ReportContext.ReportNullLabel = stmt.Value; break;
            // Defence in depth: the parser owns the closed key set, so reaching here means the two drifted.
            default: throw new ExecutionException(ReportMetadataKeys.UnknownKeyMessage(stmt.Key));
        }
        return Task.CompletedTask;
    }

    private static string ValidateTimeZone(string value)
    {
        var zone = value?.Trim() ?? string.Empty;
        if (!TimeZoneResolver.TryFindTimeZone(zone, out _))
            throw new ExecutionException(
                $"Invalid SET REPORT TIME_ZONE value: '{value}'. Use an IANA zone id such as 'America/New_York', or 'UTC'.");
        return zone;
    }

    private static string ValidateLocale(string value)
    {
        var locale = value?.Trim() ?? string.Empty;
        if (!ReportFormattingSettings.TryResolveCulture(locale, out _))
            throw new ExecutionException(
                $"Invalid SET REPORT LOCALE value: '{value}'. Use a culture name such as 'de-DE', or '' for the invariant culture.");
        return locale;
    }
}
