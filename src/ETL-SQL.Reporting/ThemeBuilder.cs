using System.Collections.Generic;
using System.Text.Json.Nodes;
using ETL_SQL.Core;

namespace ETL_SQL.Reporting
{
    public static class ThemeBuilder
    {
        /// <summary>
        /// Translates Report-SQL theme properties to the ECharts theme JSON structure.
        /// </summary>
        public static JsonObject BuildEChartsTheme(Dictionary<string, string> props) =>
            ReportingThemeBuilder.BuildEChartsTheme(props);
    }
}
