using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Services
{
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
        public IDictionary<string, CreateTemplateStatement> TemplateDefinitions { get; private set; }

        public string TemplatePath { get; set; } = "./Templates";
        public string? ReportTitle { get; set; }
        public string? ReportDescription { get; set; }

        public ReportRegistry()
        {
            VisualDefinitions = new Dictionary<string, CreateVisualStatement>(StringComparer.OrdinalIgnoreCase);
            PageDefinitions = new Dictionary<string, CreatePageStatement>(StringComparer.OrdinalIgnoreCase);
            DatasetDefinitions = new Dictionary<string, CreateDatasetStatement>(StringComparer.OrdinalIgnoreCase);
            ContainerDefinitions = new Dictionary<string, CreateContainerStatement>(StringComparer.OrdinalIgnoreCase);
            NavigationDefinitions = new Dictionary<string, CreateNavigationStatement>(StringComparer.OrdinalIgnoreCase);
            StyleDefinitions = new Dictionary<string, CreateStyleStatement>(StringComparer.OrdinalIgnoreCase);
            ButtonDefinitions = new Dictionary<string, CreateButtonStatement>(StringComparer.OrdinalIgnoreCase);
            TemplateDefinitions = new Dictionary<string, CreateTemplateStatement>(StringComparer.OrdinalIgnoreCase);
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
                TemplateDefinitions = new Dictionary<string, CreateTemplateStatement>(TemplateDefinitions, StringComparer.OrdinalIgnoreCase),
                TemplatePath = this.TemplatePath,
                ReportTitle = this.ReportTitle,
                ReportDescription = this.ReportDescription
            };
        }
    }
}
