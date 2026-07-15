namespace ETL_SQL.Core.Security;

/// <summary>Classifies query ASTs that cannot create or replace an engine or remote table.</summary>
public static class ReadOnlyQueryPolicy
{
    public static bool IsReadOnly(Statement statement) => statement switch
    {
        SelectStatement { IntoTable: null } => true,
        SetOperationStatement setOperation =>
            IsReadOnly(setOperation.Left) && IsReadOnly(setOperation.Right),
        _ => false
    };
}
