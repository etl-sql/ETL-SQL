using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ETL_SQL.ReportBuilder
{
    // ════════════════════════════════════════════════════════════════════════
    // ReportManifest — Phase 9B
    //
    // Serialisable POCOs that describe a fully-evaluated report.
    // Produced by ManifestBuilder; consumed by ChartJsRenderer,
    // MarkdownRenderer, SnapshotStore, and the VS Code preview WebviewPanel.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The root manifest for a single .rptsql report.</summary>
    public class ReportManifest
    {
        /// <summary>Script file path that produced this manifest.</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        /// <summary>UTC time the manifest was built.</summary>
        [JsonPropertyName("builtAt")]
        public DateTime BuiltAt { get; set; } = DateTime.UtcNow;

        /// <summary>Named visuals in script-definition order.</summary>
        [JsonPropertyName("visuals")]
        public List<VisualManifest> Visuals { get; set; } = new();

        /// <summary>Named pages in script-definition order.</summary>
        [JsonPropertyName("pages")]
        public List<PageManifest> Pages { get; set; } = new();

        /// <summary>Named datasets (materialized #temp tables).</summary>
        [JsonPropertyName("datasets")]
        public List<DatasetManifest> Datasets { get; set; } = new();
    }

    /// <summary>A single visual with its data snapshot and Chart.js config.</summary>
    public class VisualManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("visualType")]
        public string VisualType { get; set; } = string.Empty;

        /// <summary>Resolved Chart.js config JSON object (as a pre-serialised string).</summary>
        [JsonPropertyName("chartConfig")]
        public string? ChartConfig { get; set; }

        /// <summary>Column headers for TABLE visuals (and raw data access).</summary>
        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; } = new();

        /// <summary>Data rows — each row is a list of cell values (strings for portability).</summary>
        [JsonPropertyName("rows")]
        public List<List<string?>> Rows { get; set; } = new();

        /// <summary>Flat options (title, legend, etc.).</summary>
        [JsonPropertyName("options")]
        public Dictionary<string, string> Options { get; set; } = new();
    }

    /// <summary>A layout page with its slot→visual mapping.</summary>
    public class PageManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("structure")]
        public string Structure { get; set; } = string.Empty;

        /// <summary>Slot letter → visual name.</summary>
        [JsonPropertyName("slotMap")]
        public Dictionary<string, string> SlotMap { get; set; } = new();

        /// <summary>Parameter names and their default values.</summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, string?> Parameters { get; set; } = new();
    }

    /// <summary>Metadata for a CREATE DATASET entry.</summary>
    public class DatasetManifest
    {
        [JsonPropertyName("tempTableName")]
        public string TempTableName { get; set; } = string.Empty;

        [JsonPropertyName("refreshInterval")]
        public string? RefreshInterval { get; set; }

        [JsonPropertyName("ttl")]
        public string? Ttl { get; set; }

        [JsonPropertyName("lastRefresh")]
        public DateTime? LastRefresh { get; set; }

        [JsonPropertyName("rowCount")]
        public long RowCount { get; set; }
    }
}
