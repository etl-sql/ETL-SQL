using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Reporting;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Implementation of IReportContext that stores definitions for visuals, pages, and other report objects.
/// Extracted from Evaluator to maintain SRP.
/// </summary>
public class ReportRegistry : IReportContext
{
    public IDictionary<string, CreateVisualStatement> VisualDefinitions { get; private set; }
    public IDictionary<string, CreatePageStatement> PageDefinitions { get; private set; }
    public IDictionary<string, CreateDatasetStatement> DatasetDefinitions { get; private set; }
    public IDictionary<string, CreateContainerStatement> ContainerDefinitions { get; private set; }
    public IDictionary<string, CreateNavigationStatement> NavigationDefinitions { get; private set; }
    public IDictionary<string, CreateStyleStatement> StyleDefinitions { get; private set; }
    public IDictionary<string, CreateButtonStatement> ButtonDefinitions { get; private set; }
    public IDictionary<string, CreateBookmarkStatement> BookmarkDefinitions { get; private set; }
    public IDictionary<string, CreateTemplateStatement> TemplateDefinitions { get; private set; }

    public IDictionary<string, CreateThemeStatement> ThemeDefinitions { get; private set; }

    public string TemplatePath { get; set; } = "./Templates";
    public string? ReportTitle { get; set; }
    public IDictionary<string, string> BaselineParameters { get; private set; }
    public bool ReportTitleIsMarkdown { get; set; }
    public string? ReportDescription { get; set; }
    public string? ReportCss { get; set; }
    public string? ReportJs { get; set; }
    public string? ReportHtmlHead { get; set; }
    public string? ReportHtmlBody { get; set; }
    public string? ReportHtmlFooter { get; set; }
    public string? ReportFavicon { get; set; }
    public string? ReportLogo { get; set; }
    public string? ReportBackground { get; set; }
    public string? ReportTheme { get; set; }
    public string? ReportNavigation { get; set; }
    public ReportFormattingSettings FormattingDefaults { get; set; } = ReportFormattingSettings.Default;
    public string? ReportTimeZone { get; set; }
    public string? ReportLocale { get; set; }
    public string? ReportNullLabel { get; set; }
    public ReportFormattingSettings EffectiveFormatting =>
        ReportFormattingSettings.Resolve(FormattingDefaults, ReportLocale, ReportTimeZone, ReportNullLabel);

    public ReportRegistry() : this(null) { }

    /// <param name="configuration">
    /// Supplies the formatting defaults. Resolved once here rather than per visual so a report cannot
    /// render half of its charts against one default and half against another.
    /// </param>
    public ReportRegistry(IConfiguration? configuration)
    {
        FormattingDefaults = ReportFormattingSettings.FromConfiguration(configuration);
        VisualDefinitions = new Dictionary<string, CreateVisualStatement>(StringComparer.OrdinalIgnoreCase);
        PageDefinitions = new Dictionary<string, CreatePageStatement>(StringComparer.OrdinalIgnoreCase);
        DatasetDefinitions = new Dictionary<string, CreateDatasetStatement>(StringComparer.OrdinalIgnoreCase);
        ContainerDefinitions = new Dictionary<string, CreateContainerStatement>(StringComparer.OrdinalIgnoreCase);
        NavigationDefinitions = new Dictionary<string, CreateNavigationStatement>(StringComparer.OrdinalIgnoreCase);
        StyleDefinitions = new Dictionary<string, CreateStyleStatement>(StringComparer.OrdinalIgnoreCase);
        ButtonDefinitions = new Dictionary<string, CreateButtonStatement>(StringComparer.OrdinalIgnoreCase);
        BookmarkDefinitions = new Dictionary<string, CreateBookmarkStatement>(StringComparer.OrdinalIgnoreCase);
        TemplateDefinitions = new Dictionary<string, CreateTemplateStatement>(StringComparer.OrdinalIgnoreCase);

        ThemeDefinitions = new Dictionary<string, CreateThemeStatement>(StringComparer.OrdinalIgnoreCase);
        BaselineParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Creates a thread-safe shallow clone of the registry for parallel execution branches.</summary>
    public ReportRegistry Clone()
    {
        return new ReportRegistry
        {
            VisualDefinitions = new Dictionary<string, CreateVisualStatement>(VisualDefinitions, StringComparer.OrdinalIgnoreCase),
            PageDefinitions = new Dictionary<string, CreatePageStatement>(PageDefinitions, StringComparer.OrdinalIgnoreCase),
            DatasetDefinitions = new Dictionary<string, CreateDatasetStatement>(DatasetDefinitions, StringComparer.OrdinalIgnoreCase),
            ContainerDefinitions = new Dictionary<string, CreateContainerStatement>(ContainerDefinitions, StringComparer.OrdinalIgnoreCase),
            NavigationDefinitions = new Dictionary<string, CreateNavigationStatement>(NavigationDefinitions, StringComparer.OrdinalIgnoreCase),
            StyleDefinitions = new Dictionary<string, CreateStyleStatement>(StyleDefinitions, StringComparer.OrdinalIgnoreCase),
            ButtonDefinitions = new Dictionary<string, CreateButtonStatement>(ButtonDefinitions, StringComparer.OrdinalIgnoreCase),
            BookmarkDefinitions = new Dictionary<string, CreateBookmarkStatement>(BookmarkDefinitions, StringComparer.OrdinalIgnoreCase),
            TemplateDefinitions = new Dictionary<string, CreateTemplateStatement>(TemplateDefinitions, StringComparer.OrdinalIgnoreCase),

            ThemeDefinitions = new Dictionary<string, CreateThemeStatement>(ThemeDefinitions, StringComparer.OrdinalIgnoreCase),
            BaselineParameters = new Dictionary<string, string>(BaselineParameters, StringComparer.OrdinalIgnoreCase),
            TemplatePath = this.TemplatePath,
            ReportTitle = this.ReportTitle,
            ReportDescription = this.ReportDescription,
            ReportCss = this.ReportCss,
            ReportJs = this.ReportJs,
            ReportHtmlHead = this.ReportHtmlHead,
            ReportHtmlBody = this.ReportHtmlBody,
            ReportHtmlFooter = this.ReportHtmlFooter,
            ReportFavicon = this.ReportFavicon,
            ReportLogo = this.ReportLogo,
            ReportBackground = this.ReportBackground,
            ReportTheme = this.ReportTheme,
            ReportNavigation = this.ReportNavigation,
            FormattingDefaults = this.FormattingDefaults,
            ReportTimeZone = this.ReportTimeZone,
            ReportLocale = this.ReportLocale,
            ReportNullLabel = this.ReportNullLabel
        };
    }
    /// <summary>Clears all visual, page, dataset, and report-level definitions.</summary>
    public void Clear()
    {
        VisualDefinitions.Clear();
        PageDefinitions.Clear();
        DatasetDefinitions.Clear();
        ContainerDefinitions.Clear();
        NavigationDefinitions.Clear();
        StyleDefinitions.Clear();
        ButtonDefinitions.Clear();
        BookmarkDefinitions.Clear();
        TemplateDefinitions.Clear();

        ThemeDefinitions.Clear();
        ReportTitle = null;
        ReportDescription = null;
        ReportCss = null;
        ReportJs = null;
        ReportHtmlHead = null;
        ReportHtmlBody = null;
        ReportHtmlFooter = null;
        ReportFavicon = null;
        ReportLogo = null;
        ReportBackground = null;
        ReportTheme = null;
        ReportNavigation = null;
        // Only the script's own overrides clear. FormattingDefaults came from configuration, not from
        // the script, so clearing it here would silently demote a deployment default to the fallback.
        ReportTimeZone = null;
        ReportLocale = null;
        ReportNullLabel = null;
    }
}
