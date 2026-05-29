namespace ETL_SQL.ReportPortal.Models;

/// <summary>A single node in a DAG returned by structure/flow endpoints.</summary>
public record DagNodeDto(
    string  Id,
    string  Label,
    string  Type,  // dataset | visual | page | table | statement | conditional | loop | io | procedure | connection
    object? Meta
);

/// <summary>A directed edge between two DAG nodes.</summary>
public record DagEdgeDto(
    string  Source,
    string  Target,
    string? Label
);

/// <summary>Complete DAG graph response.</summary>
public record DagDto(
    IReadOnlyList<DagNodeDto> Nodes,
    IReadOnlyList<DagEdgeDto> Edges
);
