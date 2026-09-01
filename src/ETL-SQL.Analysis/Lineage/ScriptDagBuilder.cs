using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Lineage;

/// <summary>A single node in a script flow DAG.</summary>
/// <param name="Type">dataset | visual | page | container | table | statement | conditional | loop | parallel | transaction | validation | io | outbound | destructive | procedure | connection</param>
/// <param name="Key">
/// The section label introducing this statement, when it has one, or null.
///
/// <para><c>Id</c> is positional — inserting a statement by hand renumbers every node after it — so
/// it cannot say "this is still the same box" across an edit. A section label is written into the
/// script by the author, so a node that has one is addressable: the pipeline canvas tracks selection
/// and editing by this key rather than by id.</para>
/// </param>
public sealed record ScriptDagNode(string Id, string Label, string Type, int Line, string? Key = null);

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

    /// <summary>
    /// One walk's accumulating graph plus the section labels found in the source.
    ///
    /// <para>Passed explicitly rather than held in a static: the builder is called concurrently by
    /// the designer route on every host, and a shared mutable index is the kind of hidden coupling
    /// that shows up later as a node wearing another statement's name under load.</para>
    /// </summary>
    private sealed class FlowGraph(IReadOnlyDictionary<Statement, string> keys, IReadOnlySet<Statement> namedLabels)
    {
        public List<ScriptDagNode> Nodes { get; } = [];
        public List<ScriptDagEdge> Edges { get; } = [];
        public int Sequence { get; set; }

        /// <summary>Wrapper start offset → the start offset of the task inside it.</summary>
        public IReadOnlyDictionary<int, int> Collapsed { get; set; } = new Dictionary<int, int>();

        /// <summary>The section label introducing this statement, or null.</summary>
        public string? KeyFor(Statement statement) => keys.TryGetValue(statement, out var key) ? key : null;

        /// <summary>True when this label names a following statement, so it is that node's key.</summary>
        public bool NamesAStatement(Statement label) => namedLabels.Contains(label);
    }

    /// <summary>
    /// Builds the flow graph.
    /// </summary>
    /// <param name="collapsed">
    /// Wrapper statements to draw as the task inside them, as start offset → inner start offset.
    ///
    /// <para>A conditional precedence edge is written into the script as a <c>BEGIN TRY</c> guard
    /// around the task it watches and an <c>IF</c> around the task that waits. Drawn literally, one
    /// task becomes three stages — a TRY/CATCH, an IF, and the statement — and the card the author
    /// dragged is no longer the card the label names. Collapsed, the map shows the tasks and puts
    /// the condition on the edge, which is where the author drew it.</para>
    ///
    /// <para>Offsets rather than statement references because the caller identifies a task by
    /// re-reading the source, not by walking this AST.</para>
    /// </param>
    public static ScriptDag Build(Script script, IReadOnlyDictionary<int, int>? collapsed = null)
    {
        var graph = MapLabels(script.Statements);
        graph.Collapsed = collapsed ?? new Dictionary<int, int>();
        AppendSequence(script.Statements, graph, []);
        return new ScriptDag(graph.Nodes, graph.Edges);
    }

    /// <summary>The statement at this offset, anywhere inside <paramref name="statement"/>.</summary>
    private static Statement? StatementAt(Statement statement, int offset)
    {
        if (statement.StartOffset == offset) return statement;
        foreach (var block in NestedBlocks(statement))
        {
            foreach (var nested in block.Statements)
            {
                if (StatementAt(nested, offset) is { } found) return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Indexes each section label against the statement it introduces.
    ///
    /// <para>Reference equality is required: statements are records, so two identical statements are
    /// equal by value, and a value-keyed map would put one statement's label on the other.</para>
    /// </summary>
    private static FlowGraph MapLabels(IReadOnlyList<Statement> statements)
    {
        var keys = new Dictionary<Statement, string>(ReferenceEqualityComparer.Instance);
        var named = new HashSet<Statement>(ReferenceEqualityComparer.Instance);
        Walk(statements);
        return new FlowGraph(keys, named);

        void Walk(IReadOnlyList<Statement> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is SectionLabelStatement label && i + 1 < list.Count)
                {
                    keys[list[i + 1]] = label.LabelName;
                    named.Add(label);
                }

                foreach (var nested in NestedBlocks(list[i]))
                    Walk(nested.Statements);
            }
        }
    }

    private static IEnumerable<BlockStatement> NestedBlocks(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block: yield return block; break;
            case ParallelStatement parallel: yield return parallel.Body; break;
            case IfStatement conditional:
                if (conditional.IfBody is BlockStatement ifBody) yield return ifBody;
                foreach (var clause in conditional.ElseIfClauses ?? [])
                    if (clause.Body is BlockStatement elseIfBody) yield return elseIfBody;
                if (conditional.ElseBody is BlockStatement elseBody) yield return elseBody;
                break;
            case TryCatchStatement tryCatch:
                if (tryCatch.TryBody is BlockStatement tryBody) yield return tryBody;
                if (tryCatch.CatchBody is BlockStatement catchBody) yield return catchBody;
                break;
            case WhileStatement loop when loop.Body is BlockStatement body: yield return body; break;
            case ForStatement loop when loop.Body is BlockStatement body: yield return body; break;
            case ForeachStatement loop when loop.Body is BlockStatement body: yield return body; break;
            case ParallelForStatement loop when loop.Body is BlockStatement body: yield return body; break;
        }
    }

    private static IReadOnlyList<FlowExit> AppendSequence(
        IEnumerable<Statement> statements,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming)
    {
        var exits = incoming;
        foreach (var statement in statements)
        {
            // Housekeeping statements would dominate the diagram without describing the flow.
            if (statement is DeclareStatement or SetVariableStatement or PrintStatement)
                continue;

            // A label that names the next statement becomes that node's key instead of a node of its
            // own: on the canvas the label *is* the task's name, not a separate stage before it.
            if (statement is SectionLabelStatement label && graph.NamesAStatement(label))
                continue;

            exits = AppendStatement(statement, graph, exits);
        }

        return exits;
    }

    private static IReadOnlyList<FlowExit> AppendStatement(
        Statement statement,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming)
    {
        // A guard or a gate the pipeline canvas wrote is not a stage of its own: it is how the
        // script says when the task inside it runs, and that belongs on the edge.
        if (graph.Collapsed.TryGetValue(statement.StartOffset, out var innerOffset)
            && StatementAt(statement, innerOffset) is { } task)
        {
            var (taskLabel, taskType) = Classify(task);
            return [new FlowExit(AddNode(taskLabel, taskType, statement.Line, graph, incoming, graph.KeyFor(statement)))];
        }

        if (statement is BlockStatement block)
            return AppendSequence(block.Statements, graph, incoming);

        if (statement is IfStatement conditional)
            return AppendConditional(conditional, graph, incoming);

        if (statement is ParallelStatement parallel)
            return AppendParallel(parallel, graph, incoming);

        if (statement is TryCatchStatement tryCatch)
            return AppendTryCatch(tryCatch, graph, incoming);

        if (statement is WhileStatement whileStatement)
            return AppendLoop(statement, whileStatement.Body, graph, incoming);

        if (statement is ForStatement forStatement)
            return AppendLoop(statement, forStatement.Body, graph, incoming);

        if (statement is ForeachStatement foreachStatement)
            return AppendLoop(statement, foreachStatement.Body, graph, incoming);

        if (statement is ParallelForStatement parallelFor)
            return AppendLoop(statement, parallelFor.Body, graph, incoming);

        var id = AddNode(statement, graph, incoming);
        return [new FlowExit(id)];
    }

    private static IReadOnlyList<FlowExit> AppendConditional(
        IfStatement conditional,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming)
    {
        var branchExits = new List<FlowExit>();
        var conditionId = AddNode("IF", "conditional", conditional.Line, graph, incoming);
        branchExits.AddRange(AppendBody(conditional.IfBody, graph, [new FlowExit(conditionId, "TRUE")]));

        var falseExit = new FlowExit(conditionId, "FALSE");
        foreach (var elseIf in conditional.ElseIfClauses ?? [])
        {
            var elseIfId = AddNode("ELSE IF", "conditional", elseIf.Line, graph, [falseExit]);
            branchExits.AddRange(AppendBody(elseIf.Body, graph, [new FlowExit(elseIfId, "TRUE")]));
            falseExit = new FlowExit(elseIfId, "FALSE");
        }

        if (conditional.ElseBody is not null)
            branchExits.AddRange(AppendBody(conditional.ElseBody, graph, [falseExit with { Label = "ELSE" }]));
        else
            branchExits.Add(falseExit);

        return branchExits;
    }

    private static IReadOnlyList<FlowExit> AppendParallel(
        ParallelStatement parallel,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming)
    {
        var parallelId = AddNode(parallel, graph, incoming);
        var exits = new List<FlowExit>();
        var branchNumber = 0;

        foreach (var branch in parallel.Body.Statements)
        {
            if (branch is DeclareStatement or SetVariableStatement or PrintStatement)
                continue;

            branchNumber++;
            exits.AddRange(AppendStatement(
                branch,
                graph,
                [new FlowExit(parallelId, $"BRANCH {branchNumber}")]));
        }

        return exits.Count > 0 ? exits : [new FlowExit(parallelId)];
    }

    private static IReadOnlyList<FlowExit> AppendTryCatch(
        TryCatchStatement tryCatch,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming)
    {
        // A transaction scope the pipeline canvas wrote is one container, not an error handler: its
        // CATCH is the rollback boilerplate the canvas emitted, and drawing that as a branch of the
        // pipeline would put three stages nobody authored next to the work that was.
        if (tryCatch.TryBody is BlockStatement { Statements: [BeginTransactionStatement, ..] } scope)
        {
            var scopeId = AddNode("TRANSACTION", "transaction", tryCatch.Line, graph, incoming, graph.KeyFor(tryCatch));
            var body = scope.Statements
                .Where(statement => statement is not (BeginTransactionStatement or CommitTransactionStatement))
                .ToList();
            var scopeExits = AppendSequence(body, graph, [new FlowExit(scopeId, "SCOPE")]);
            return scopeExits.Count > 0 ? scopeExits : [new FlowExit(scopeId)];
        }

        var tryId = AddNode("TRY / CATCH", "conditional", tryCatch.Line, graph, incoming);
        var exits = new List<FlowExit>();
        exits.AddRange(AppendBody(tryCatch.TryBody, graph, [new FlowExit(tryId, "TRY")]));
        exits.AddRange(AppendBody(tryCatch.CatchBody, graph, [new FlowExit(tryId, "CATCH")]));
        return exits;
    }

    private static IReadOnlyList<FlowExit> AppendLoop(
        Statement loop,
        Statement body,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming)
    {
        var loopId = AddNode(loop, graph, incoming);
        var bodyExits = AppendBody(body, graph, [new FlowExit(loopId, "BODY")]);
        return [.. bodyExits, new FlowExit(loopId, "DONE")];
    }

    private static IReadOnlyList<FlowExit> AppendBody(
        Statement body,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming) =>
        body is BlockStatement block
            ? AppendSequence(block.Statements, graph, incoming)
            : AppendStatement(body, graph, incoming);

    private static string AddNode(
        Statement statement,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming)
    {
        var (label, type) = Classify(statement);
        var key = graph.KeyFor(statement);
        return AddNode(label, type, statement.Line, graph, incoming, key);
    }

    private static string AddNode(
        string label,
        string type,
        int line,
        FlowGraph graph,
        IReadOnlyList<FlowExit> incoming,
        string? key = null)
    {
        var id = $"s{graph.Sequence++}";
        graph.Nodes.Add(new ScriptDagNode(id, label, type, line, key));

        foreach (var exit in incoming)
            graph.Edges.Add(new ScriptDagEdge(exit.NodeId, id, exit.Label));

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
        ExecutePushdownStatement s => ($"EXECUTE {s.ConnectionName.ToSql()}", "procedure"),
        ExecuteRemoteBlockStatement s => ($"EXECUTE {s.ConnectionName.ToSql()}", "procedure"),
        // A label with no statement after it still marks a checkpoint the engine can resume from.
        SectionLabelStatement s => ($"{s.LabelName}:", "statement"),
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
