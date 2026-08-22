using System.Collections.Generic;
using System.Text.Json.Nodes;
using ETL_SQL.Core;

namespace ETL_SQL.Reporting
{
    public static class ThemeBuilder
    {
        /// <summary>
        /// Translates Report-SQL theme properties to the native theme JSON structure.
        /// </summary>
        public static JsonObject BuildNativeTheme(Dictionary<string, string> props) =>
            ReportingThemeBuilder.BuildNativeTheme(props);
    }
}
