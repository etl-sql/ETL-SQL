namespace ETL_SQL.ReportPortal;

/// <summary>
/// Stable anchor type used by WebApplicationFactory in integration tests.
/// WebApplicationFactory needs a type from this assembly; using a named marker class
/// avoids the Program-class ambiguity caused by the ETL-SQL.App transitive reference.
/// </summary>
public sealed class PortalMarker { }
