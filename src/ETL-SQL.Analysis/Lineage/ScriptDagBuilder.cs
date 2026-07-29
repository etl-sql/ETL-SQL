using System;
using System.Collections.Generic;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Lineage;

/// <summary>A single node in a script flow DAG.</summary>
/// <param name="Type">dataset | visual | page | container | table | statement | conditional | loop | io | outbound | destructive | procedure | connection</param>
public sealed record ScriptDagNode(string Id, string Label, string Type, int Line);

/// <summary>A directed edge between two flow nodes.</summary>
public sealed record ScriptDagEdge(string Source, string Target, string? Label = null);

/// <summary>Complete script flow graph.</summary>
public sealed record ScriptDag(IReadOnlyList<ScriptDagNode> Nodes, IReadOnlyList<ScriptDagEdge> Edges);

/// <summary>
/// Turns a parsed script into the read-only pipeline diagram shown by the Orchestrator job view
/// and the VS Code Visual Flow panel: flat files and connections through temp tables and queries
/// to database targets.
/// </summary>
/// <remarks>
/// Shared so every host renders the same shape through the canonical <c>renderDag</c>. Statement
/// order is the edge order — this is a flow overview, not a data-dependency graph.
/// </remarks>
public static class ScriptDagBuilder
{
    public static ScriptDag Build(Script script)
    {
        var nodes = new List<ScriptDagNode>();
        var edges = new List<ScriptDagEdge>();
        var seq = 0;
        Append(script.Statements, nodes, edges, ref seq, null);
        return new ScriptDag(nodes, edges);
    }

    private static void Append(
        IEnumerable<Statement> statements,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        string? previousId)
    {
        foreach (var statement in statements)
        {
            // Housekeeping statements would dominate the diagram without describing the flow.
            if (statement is DeclareStatement or SetVariableStatement or PrintStatement)
                continue;

            var id = $"s{seq++}";
            var (label, type) = Classify(statement);
            nodes.Add(new ScriptDagNode(id, label, type, statement.Line));

            if (previousId is not null)
                edges.Add(new ScriptDagEdge(previousId, id));

            previousId = id;
        }
    }

    private static (string Label, string Type) Classify(Statement statement) => statement switch
    {
        InsertStatement s => ($"INSERT → {s.TargetTable.TableName}", "io"),
        UpdateStatement s => ($"UPDATE {s.TargetTable.TableName}", "destructive"),
        DeleteStatement s => ($"DELETE {s.TargetTable.TableName}", "destructive"),
        MergeStatement s => ($"MERGE → {s.TargetTable.TableName}", "destructive"),
        SelectStatement s => s.IntoTable is not null
            ? ($"SELECT INTO {s.IntoTable.TableName}", "io")
            : ("SELECT", "statement"),
        CreateTableStatement s => ($"CREATE {s.TargetTable.TableName}", "statement"),
        CreateConnectionStatement s => ($"CONNECT {s.ConnectionName}", "connection"),
        FileTransferStatement s => (s.Type == FileTransferType.Send
            ? $"SEND FILE → {s.ConnectionName}"
            : $"RECEIVE FILE ← {s.ConnectionName}", "outbound"),
        FileOperationStatement s => ($"{s.Type.ToString().ToUpperInvariant()} FILE", s.Type is FileOpType.Delete or FileOpType.Move or FileOpType.Rename ? "destructive" : "io"),
        DirectoryOperationStatement s => ($"{s.Type.ToString().ToUpperInvariant()} DIRECTORY", s.Type is DirectoryOpType.Delete or DirectoryOpType.Move or DirectoryOpType.Rename or DirectoryOpType.DeleteContents ? "destructive" : "io"),
        IfStatement => ("IF", "conditional"),
        WhileStatement => ("WHILE", "loop"),
        ForStatement s => ($"FOR @{s.VariableName}", "loop"),
        ForeachStatement s => ($"FOREACH @{s.VariableName}", "loop"),
        ParallelStatement => ("PARALLEL", "loop"),
        ExecuteStatement s => ($"CALL {s.ProcedureName}", "procedure"),
        RunScriptStatement => ("RUN SCRIPT", "io"),
        BulkInsertStatement s => ($"BULK INSERT → {s.TargetTable.TableName}", "io"),
        ExportStatement s => ($"EXPORT → {s.TargetPath}", "outbound"),
        ExportReportStatement s => ($"EXPORT REPORT → {s.OutputPath.ToSql()}", "outbound"),
        CreateDatasetStatement s => ($"DATASET {s.TempTableName}", "dataset"),
        RefreshPortalDatasetStatement s => ($"REFRESH {s.DatasetName}", "dataset"),
        CreateVisualStatement s => ($"VISUAL {s.Name}", "visual"),
        CreatePageStatement s => ($"PAGE {s.Name}", "page"),
        CreateContainerStatement s => ($"CONTAINER {s.Name}", "container"),
        _ => (statement.GetType().Name.Replace("Statement", string.Empty, StringComparison.Ordinal), "statement"),
    };
}
