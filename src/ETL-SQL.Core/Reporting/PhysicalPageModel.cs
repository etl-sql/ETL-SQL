using System.Collections.Generic;
using ETL_SQL.Core;

namespace ETL_SQL.Core.Reporting;

public class PhysicalPageModel
{
    public int PageNumber { get; set; }
    public PageLayoutDefinition Layout { get; set; } = null!;
    public List<PlacedVisual> Visuals { get; set; } = new();
}

public class PlacedVisual
{
    public CreateVisualStatement Visual { get; set; } = null!;
    public double TopOffset { get; set; }
    public double Height { get; set; }
}
