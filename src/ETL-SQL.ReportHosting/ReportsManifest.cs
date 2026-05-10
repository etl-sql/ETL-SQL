using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ETL_SQL.ReportHosting
{
    /// <summary>Deserialised from reports.json — lists all reports the server will host.</summary>
    public class ReportsManifest
    {
        [JsonPropertyName("reports")]
        public List<ReportEntry> Reports { get; set; } = new();
    }

    public class ReportEntry
    {
        /// <summary>Display name and URL slug (e.g. "Sales" → /reports/Sales).</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Path to the .rptsql file — relative to the manifest file or absolute.</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>Optional short description shown on the catalog page.</summary>
        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }
    }
}
