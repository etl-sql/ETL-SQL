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

        /// <summary>Optional report title (from SET REPORT TITLE = '...').</summary>
        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        /// <summary>Optional report description (from SET REPORT DESCRIPTION = '...').</summary>
        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        /// <summary>Named visuals in script-definition order.</summary>
        [JsonPropertyName("visuals")]
        public List<VisualManifest> Visuals { get; set; } = new();

        /// <summary>Named pages in script-definition order.</summary>
        [JsonPropertyName("pages")]
        public List<PageManifest> Pages { get; set; } = new();

        /// <summary>Named datasets (materialized #temp tables).</summary>
        [JsonPropertyName("datasets")]
        public List<DatasetManifest> Datasets { get; set; } = new();

        [JsonPropertyName("containers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ContainerManifest>? Containers { get; set; }

        [JsonPropertyName("navigations")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<NavigationManifest>? Navigations { get; set; }
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

        /// <summary>Set when data fetch fails; causes the runtime to render an error card.</summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>Click/change action bindings from the ACTIONS clause.</summary>
        [JsonPropertyName("actions")]
        public List<VisualActionManifest> Actions { get; set; } = new();

        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }

        [JsonPropertyName("seriesDefs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SeriesDefManifest>? SeriesDefs { get; set; }
    }

    /// <summary>A serialisable representation of one ACTIONS entry (DRILL_DOWN or SET_PARAMETER).</summary>
    public class VisualActionManifest
    {
        /// <summary>"DRILL_DOWN" or "SET_PARAMETER".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>"ON_CLICK" or "ON_CHANGE".</summary>
        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = string.Empty;

        // DRILL_DOWN fields
        [JsonPropertyName("targetVisual")]
        public string? TargetVisual { get; set; }

        [JsonPropertyName("keyColumn")]
        public string? KeyColumn { get; set; }

        // SET_PARAMETER fields
        [JsonPropertyName("parameterName")]
        public string? ParameterName { get; set; }

        [JsonPropertyName("valueExpression")]
        public string? ValueExpression { get; set; }
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

        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }
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

    public class SeriesDefManifest
    {
        [JsonPropertyName("seriesType")] public string SeriesType { get; set; } = string.Empty;
        [JsonPropertyName("column")]     public string Column { get; set; } = string.Empty;
    }

    public class ContainerManifest
    {
        [JsonPropertyName("name")]          public string Name { get; set; } = string.Empty;
        [JsonPropertyName("containerType")] public string ContainerType { get; set; } = string.Empty;
        [JsonPropertyName("visuals")]       public List<string> Visuals { get; set; } = new();
        [JsonPropertyName("styles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Styles { get; set; }
    }

    public class NavigationManifest
    {
        [JsonPropertyName("name")]        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("navType")]     public string NavType { get; set; } = string.Empty;
        [JsonPropertyName("orientation")] public string Orientation { get; set; } = string.Empty;
        [JsonPropertyName("defaultPage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DefaultPage { get; set; }
        [JsonPropertyName("pages")]       public List<string> Pages { get; set; } = new();
    }
}
