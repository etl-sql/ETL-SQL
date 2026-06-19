namespace ETL_SQL.Core.Governance;

/// <summary>
/// Primitive value shape used to validate and render policy values consistently.
/// </summary>
public enum GovernancePolicyValueKind
{
    Boolean,
    Integer,
    Long,
    String,
    StringList,
    Enum,
    Path,
    PathList,
    HostPatternList,
    ConnectorTypeList,
    Uri,
    SecretReference
}

