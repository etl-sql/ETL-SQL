namespace ETL_SQL.Core;
public enum TransformationKind
{
    Unknown,
    PassThrough,
    Cast,
    FunctionCall,
    CaseExpression,
    Arithmetic,
    StringOperation,
    Aggregation,
    WindowFunction,
    Conditional,
    Literal,
    Subquery
}
