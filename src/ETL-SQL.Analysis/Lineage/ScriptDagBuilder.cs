using System;
using System.Collections.Generic;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Lineage;

/// <summary>A single node in a script flow DAG.</summary>
/// <param name="Type">dataset | visual | page | container | table | statement | conditional | loop | parallel | validation | io | outbound | destructive | procedure | connection</param>
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
/// Shared so every host renders the same shape through the canonical <c>renderDag</c>. Sequential
/// statements remain ordered, while structured control-flow statements project their real branch
/// and convergence edges. Loops stay acyclic in this design-time graph so the shared layered layout
/// can render them deterministically; their body and completion paths are labeled explicitly.
/// </remarks>
public static class ScriptDagBuilder
{
    private sealed record FlowExit(string NodeId, string? Label = null);

    public static ScriptDag Build(Script script)
    {
        var nodes = new List<ScriptDagNode>();
        var edges = new List<ScriptDagEdge>();
        var seq = 0;
        AppendSequence(script.Statements, nodes, edges, ref seq, []);
        return new ScriptDag(nodes, edges);
    }

    private static IReadOnlyList<FlowExit> AppendSequence(
        IEnumerable<Statement> statements,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        var exits = incoming;
        foreach (var statement in statements)
        {
            // Housekeeping statements would dominate the diagram without describing the flow.
            if (statement is DeclareStatement or SetVariableStatement or PrintStatement)
                continue;

            exits = AppendStatement(statement, nodes, edges, ref seq, exits);
        }

        return exits;
    }

    private static IReadOnlyList<FlowExit> AppendStatement(
        Statement statement,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        if (statement is BlockStatement block)
            return AppendSequence(block.Statements, nodes, edges, ref seq, incoming);

        if (statement is IfStatement conditional)
            return AppendConditional(conditional, nodes, edges, ref seq, incoming);

        if (statement is ParallelStatement parallel)
            return AppendParallel(parallel, nodes, edges, ref seq, incoming);

        if (statement is TryCatchStatement tryCatch)
            return AppendTryCatch(tryCatch, nodes, edges, ref seq, incoming);

        if (statement is WhileStatement whileStatement)
            return AppendLoop(statement, whileStatement.Body, nodes, edges, ref seq, incoming);

        if (statement is ForStatement forStatement)
            return AppendLoop(statement, forStatement.Body, nodes, edges, ref seq, incoming);

        if (statement is ForeachStatement foreachStatement)
            return AppendLoop(statement, foreachStatement.Body, nodes, edges, ref seq, incoming);

        if (statement is ParallelForStatement parallelFor)
            return AppendLoop(statement, parallelFor.Body, nodes, edges, ref seq, incoming);

        var id = AddNode(statement, nodes, edges, ref seq, incoming);
        return [new FlowExit(id)];
    }

    private static IReadOnlyList<FlowExit> AppendConditional(
        IfStatement conditional,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        var branchExits = new List<FlowExit>();
        var conditionId = AddNode("IF", "conditional", conditional.Line, nodes, edges, ref seq, incoming);
        branchExits.AddRange(AppendBody(conditional.IfBody, nodes, edges, ref seq, [new FlowExit(conditionId, "TRUE")]));

        var falseExit = new FlowExit(conditionId, "FALSE");
        foreach (var elseIf in conditional.ElseIfClauses ?? [])
        {
            var elseIfId = AddNode("ELSE IF", "conditional", elseIf.Line, nodes, edges, ref seq, [falseExit]);
            branchExits.AddRange(AppendBody(elseIf.Body, nodes, edges, ref seq, [new FlowExit(elseIfId, "TRUE")]));
            falseExit = new FlowExit(elseIfId, "FALSE");
        }

        if (conditional.ElseBody is not null)
            branchExits.AddRange(AppendBody(conditional.ElseBody, nodes, edges, ref seq, [falseExit with { Label = "ELSE" }]));
        else
            branchExits.Add(falseExit);

        return branchExits;
    }

    private static IReadOnlyList<FlowExit> AppendParallel(
        ParallelStatement parallel,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        var parallelId = AddNode(parallel, nodes, edges, ref seq, incoming);
        var exits = new List<FlowExit>();
        var branchNumber = 0;

        foreach (var branch in parallel.Body.Statements)
        {
            if (branch is DeclareStatement or SetVariableStatement or PrintStatement)
                continue;

            branchNumber++;
            exits.AddRange(AppendStatement(
                branch,
                nodes,
                edges,
                ref seq,
                [new FlowExit(parallelId, $"BRANCH {branchNumber}")]));
        }

        return exits.Count > 0 ? exits : [new FlowExit(parallelId)];
    }

    private static IReadOnlyList<FlowExit> AppendTryCatch(
        TryCatchStatement tryCatch,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        var tryId = AddNode("TRY / CATCH", "conditional", tryCatch.Line, nodes, edges, ref seq, incoming);
        var exits = new List<FlowExit>();
        exits.AddRange(AppendBody(tryCatch.TryBody, nodes, edges, ref seq, [new FlowExit(tryId, "TRY")]));
        exits.AddRange(AppendBody(tryCatch.CatchBody, nodes, edges, ref seq, [new FlowExit(tryId, "CATCH")]));
        return exits;
    }

    private static IReadOnlyList<FlowExit> AppendLoop(
        Statement loop,
        Statement body,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        var loopId = AddNode(loop, nodes, edges, ref seq, incoming);
        var bodyExits = AppendBody(body, nodes, edges, ref seq, [new FlowExit(loopId, "BODY")]);
        return [.. bodyExits, new FlowExit(loopId, "DONE")];
    }

    private static IReadOnlyList<FlowExit> AppendBody(
        Statement body,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming) =>
        body is BlockStatement block
            ? AppendSequence(block.Statements, nodes, edges, ref seq, incoming)
            : AppendStatement(body, nodes, edges, ref seq, incoming);

    private static string AddNode(
        Statement statement,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        var (label, type) = Classify(statement);
        return AddNode(label, type, statement.Line, nodes, edges, ref seq, incoming);
    }

    private static string AddNode(
        string label,
        string type,
        int line,
        List<ScriptDagNode> nodes,
        List<ScriptDagEdge> edges,
        ref int seq,
        IReadOnlyList<FlowExit> incoming)
    {
        var id = $"s{seq++}";
        nodes.Add(new ScriptDagNode(id, label, type, line));

        foreach (var exit in incoming)
            edges.Add(new ScriptDagEdge(exit.NodeId, id, exit.Label));

        return id;
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
        TransformStatement s => ($"TRANSFORM → {s.TargetTable.TableName}", "io"),
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
        ParallelStatement => ("PARALLEL", "parallel"),
        ParallelForStatement s => ($"PARALLEL FOR @{s.VariableName}", "parallel"),
        TryCatchStatement => ("TRY / CATCH", "conditional"),
        AssertStatement => ("ASSERT", "validation"),
        AssertTableStatement s => ($"ASSERT TABLE {s.ActualTable}", "validation"),
        AssertJobStatement s => ($"ASSERT JOB {s.JobName}", "validation"),
        ExpectSchemaStatement s => ($"EXPECT SCHEMA {s.Target}", "validation"),
        ValidateBundleStatement s => ($"VALIDATE BUNDLE {s.BundleName}", "validation"),
        ValidatePortalReportStatement => ("VALIDATE PORTAL REPORT", "validation"),
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
