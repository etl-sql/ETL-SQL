using System.Collections.Generic;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Server-side drill state for a DRILL_IN visual. Not serialized to JSON directly;
    /// converted to <see cref="VisualDrillStateManifest"/> for browser delivery.
    /// </summary>
    public record VisualDrillState(
        string[] Hierarchy,
        List<(string Column, string Value)> Path);
}
