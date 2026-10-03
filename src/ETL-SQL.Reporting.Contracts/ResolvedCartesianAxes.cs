namespace ETL_SQL.Reporting.Semantics;

/// <summary>The semantic primary scales shared by a composition with guarded axis ownership.</summary>
public sealed record ResolvedCartesianAxes(string XScaleId, string YScaleId);
