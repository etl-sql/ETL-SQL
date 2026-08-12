using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting;

public class PhysicalPageModel
{
    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("layout")]
    public PageLayoutDefinitionManifest Layout { get; set; } = null!;

    [JsonPropertyName("visuals")]
    public List<PlacedVisual> Visuals { get; set; } = new();
}

public class PlacedVisual
{
    [JsonPropertyName("visual")]
    public VisualManifest Visual { get; set; } = null!;

    [JsonPropertyName("topOffset")]
    public double TopOffset { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("startRowIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StartRowIndex { get; set; }

    [JsonPropertyName("endRowIndex")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EndRowIndex { get; set; }
}
