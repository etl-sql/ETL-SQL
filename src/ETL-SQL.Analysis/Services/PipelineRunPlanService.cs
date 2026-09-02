using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// One thing the plan would do that outlives the run.
/// </summary>
/// <param name="TaskId">The labelled task it sits in, or null when it is ambient script.</param>
/// <param name="Action">What it does, in the words the script uses: <c>MERGE</c>, <c>SEND EMAIL</c>.</param>
/// <param name="Target">What it does it to — a connection-qualified table, an address, a path.</param>
public sealed record PipelineRunEffect(string? TaskId, string Action, string Target, int Line);

/// <summary>
/// What running to a selected task would execute, and what that would cost.
/// </summary>
/// <param name="Resolved">
/// False when the script does not parse, or holds no task by that name. The caller says so rather
/// than offering an empty plan, which would read as "this node needs nothing".
/// </param>
/// <param name="Script">The slice to execute. Empty when <paramref name="Resolved"/> is false.</param>
/// <param name="Included">The labelled tasks the slice runs, in script order.</param>
/// <param name="Skipped">
/// The labelled tasks above the selection that were left out because the selection does not depend
/// on them. Named rather than dropped silently: a skipped sibling is the most likely reason a run
/// that "should have worked" did not.
/// </param>
/// <param name="Effects">
/// Everything in the slice that writes outside the session or reaches outward. The caller must show
/// these and get an answer before running; an empty list is what makes a run safe to start without
/// asking.
/// </param>
public sealed record PipelineRunPlan(
    bool Resolved,
    string? Error,
    string Script,
    IReadOnlyList<string> Included,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<PipelineRunEffect> Effects)
{
    public static PipelineRunPlan Failed(string error) => new(false, error, string.Empty, [], [], []);
}

public interface IPipelineRunPlanProjection
{
    PipelineRunPlan To(string? scriptText, string? taskId);
}

/// <summary>
/// Builds the script that runs a pipeline up to and including a selected task.
///
/// <para>This does not execute anything. It answers two questions the canvas cannot answer for
/// itself — which bytes to send to the ordinary run route, and what the author is about to be
/// responsible for — and leaves both the running and the asking to the host.</para>
///
/// <para><b>The slice is the selection's dependency closure, not everything above it.</b> A task's
/// prerequisites are the ones its <c>-- @after:</c> tag names; a task with no tag inherits the plain
/// sequential reading the canvas already uses for a reorder, and waits for the task directly above
/// it in the same scope. So declaring a dependency narrows the run to what was declared, which is
/// the point of declaring it, while an untagged script still runs top to bottom the way it reads.
/// </para>
///
/// <para><b>Everything the canvas does not model is kept.</b> <c>CREATE CONNECTION</c>,
/// <c>DECLARE</c>, <c>SET</c>, and unlabelled staging are ambient: they carry no dependency
/// information, so there is nothing to prove they are unrelated, and dropping one would break the
/// run in a way the author could not see. Only a labelled task — something the canvas can point at
/// and the author can reason about — is ever left out.</para>
///
/// <para><b>Nothing is cut mid-statement.</b> Excluded tasks come out by the same span a delete uses,
/// declaration and dependency tag included, and the tail is cut at the end of the top-level
/// statement containing the selection. A slice that truncated inside a <c>PARALLEL</c> block would
/// not parse, and a slice that dropped a container but kept its children would run them
/// unsupervised.</para>
/// </summary>
public sealed class PipelineRunPlanService : IPipelineRunPlanProjection
{
    public PipelineRunPlan To(string? scriptText, string? taskId)
    {
        if (string.IsNullOrWhiteSpace(scriptText)) return PipelineRunPlan.Failed("There is no script to run.");
        if (string.IsNullOrWhiteSpace(taskId)) return PipelineRunPlan.Failed("No task is selected.");

        if (!PipelineTaskAuthoringService.TryParse(scriptText, out var ast, out var parseError))
            return PipelineRunPlan.Failed(parseError);

        var tasks = PipelineTaskAuthoringService.ReadTasks(scriptText, ast);
        var selected = PipelineTaskAuthoringService.Find(tasks, taskId);
        if (selected is null) return PipelineRunPlan.Failed($"'{taskId}' is not a task in this script.");

        var closure = Closure(tasks, selected);

        // The tail is cut at the end of the top-level statement holding the selection, so a task
        // inside a container takes its whole container with it. Running half a PARALLEL block is not
        // something the language can express, and a slice that tried would not parse.
        var root = Root(tasks, selected);
        var tail = PipelineTaskAuthoringService.EndOfLine(scriptText, root.EndOffset);

        // Cut back to front so each span's offsets are still the ones that were measured.
        var builder = new StringBuilder(scriptText[..tail]);
        var skipped = new List<string>();
        foreach (var task in tasks.OrderByDescending(task => task.StartOffset))
        {
            if (closure.Contains(task.Id)) continue;
            if (task.StartOffset >= tail) continue; // Below the selection: already gone with the tail.

            skipped.Add(task.Id);
            var (start, end) = PipelineTaskAuthoringService.RemovableSpan(scriptText, task);
            builder.Remove(start, Math.Min(end, tail) - start);
        }

        skipped.Reverse();
        var script = builder.ToString();

        // Effects are read off the slice, not the original: a mutating task that was skipped is not
        // something to warn about, and warning about it would teach the author to click through.
        return new PipelineRunPlan(
            true,
            null,
            script,
            [.. tasks.Where(task => closure.Contains(task.Id)).Select(task => task.Id)],
            skipped,
            ReadEffects(script));
    }

    // ── The closure ──────────────────────────────────────────────────────────

    /// <summary>
    /// The selection, everything it transitively waits for, and every container holding any of them.
    /// </summary>
    private static HashSet<string> Closure(IReadOnlyList<PipelineTask> tasks, PipelineTask selected)
    {
        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<PipelineTask>();
        pending.Push(selected);

        while (pending.Count > 0)
        {
            var task = pending.Pop();
            if (!closure.Add(task.Id)) continue;

            foreach (var prerequisite in Prerequisites(tasks, task))
                pending.Push(prerequisite);

            // A task cannot run without the block it lives in, and a container cannot run without
            // what it contains — its children are its body, not its dependents.
            if (Container(tasks, task) is { } container) pending.Push(container);
            foreach (var child in tasks.Where(candidate =>
                string.Equals(candidate.Container, task.Id, StringComparison.OrdinalIgnoreCase)))
            {
                pending.Push(child);
            }
        }

        return closure;
    }

    /// <summary>
    /// What a task waits for.
    ///
    /// <para>A declared <c>-- @after:</c> tag is the whole answer when there is one: the author said
    /// what this needs, and honouring anything more would make declaring a dependency pointless. With
    /// no tag, the answer is the task directly above it in the same scope — the same reading of plain
    /// sequence the canvas uses when a drag reorders one task after another.</para>
    /// </summary>
    private static IEnumerable<PipelineTask> Prerequisites(IReadOnlyList<PipelineTask> tasks, PipelineTask task)
    {
        if (task.DependsOn.Count > 0)
        {
            return tasks.Where(candidate =>
                task.DependsOn.Any(dependency =>
                    string.Equals(dependency.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)));
        }

        var previous = tasks
            .Where(candidate => SameScope(candidate, task) && candidate.StartOffset < task.StartOffset)
            .OrderByDescending(candidate => candidate.StartOffset)
            .FirstOrDefault();

        return previous is null ? [] : [previous];
    }

    private static bool SameScope(PipelineTask left, PipelineTask right) =>
        string.Equals(left.Container, right.Container, StringComparison.OrdinalIgnoreCase);

    private static PipelineTask? Container(IReadOnlyList<PipelineTask> tasks, PipelineTask task) =>
        task.Container is null ? null : PipelineTaskAuthoringService.Find(tasks, task.Container);

    /// <summary>The outermost task containing the selection, or the selection when it is top level.</summary>
    private static PipelineTask Root(IReadOnlyList<PipelineTask> tasks, PipelineTask task)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = task;

        // A malformed container chain must not spin here; the run refuses on a parse error long
        // before this, but a cycle in the labels is cheaper to guard than to diagnose.
        while (seen.Add(current.Id) && Container(tasks, current) is { } container)
            current = container;

        return current;
    }

    // ── The effects ──────────────────────────────────────────────────────────

    /// <summary>
    /// Everything in the slice that outlives the run, in script order.
    ///
    /// <para>Session-local <c>#temp</c> targets are not effects: they die with the session and are
    /// the ordinary working material of a staging script. Everything else that writes to a
    /// connection, touches the file system, sends mail, or hands control to another script is one —
    /// including <c>EXECUTE</c>, whose body is pushed to the connection unparsed and so cannot be
    /// claimed to be read-only.</para>
    /// </summary>
    private static IReadOnlyList<PipelineRunEffect> ReadEffects(string script)
    {
        if (!PipelineTaskAuthoringService.TryParse(script, out var ast, out _)) return [];

        var tasks = PipelineTaskAuthoringService.ReadTasks(script, ast);
        var found = new List<PipelineRunEffect>();
        Walk(ast.Statements, found);

        // Attribute each effect to the labelled task whose span holds it, so the confirmation can
        // say "publish_orders will MERGE into warehouse.Customers" rather than quoting a line number.
        return
        [
            .. found
                .OrderBy(effect => effect.Line)
                .Select(effect => effect with { TaskId = OwningTask(tasks, script, effect.Line) })
        ];
    }

    private static string? OwningTask(IReadOnlyList<PipelineTask> tasks, string script, int line) =>
        tasks
            .Where(task => task.Line <= line && LineOf(script, task.EndOffset) >= line)
            // The innermost one: a task inside a container is a better answer than the container.
            .OrderByDescending(task => task.StartOffset)
            .FirstOrDefault()
            ?.Id;

    private static int LineOf(string script, int offset)
    {
        var line = 1;
        for (var index = 0; index < Math.Clamp(offset, 0, script.Length); index++)
            if (script[index] == '\n') line++;
        return line;
    }

    private static void Walk(IEnumerable<Statement> statements, List<PipelineRunEffect> found)
    {
        foreach (var statement in statements)
        {
            if (Describe(statement) is { } effect) found.Add(effect);

            // An effect inside control flow still happens. Whether the branch is taken is not
            // knowable here, so it is listed: over-listing costs the author a glance, under-listing
            // costs them the write they were not told about.
            switch (statement)
            {
                case BlockStatement block:
                    Walk(block.Statements, found);
                    break;
                case IfStatement ifStatement:
                    Walk([ifStatement.IfBody], found);
                    foreach (var clause in ifStatement.ElseIfClauses ?? [])
                        Walk([clause.Body], found);
                    if (ifStatement.ElseBody is not null) Walk([ifStatement.ElseBody], found);
                    break;
                case WhileStatement whileStatement:
                    Walk([whileStatement.Body], found);
                    break;
                case ForStatement forStatement:
                    Walk([forStatement.Body], found);
                    break;
                case ForeachStatement foreachStatement:
                    Walk([foreachStatement.Body], found);
                    break;
                case ParallelStatement parallel:
                    Walk([parallel.Body], found);
                    break;
                case ParallelForStatement parallelFor:
                    Walk([parallelFor.Body], found);
                    break;
                case TryCatchStatement tryCatch:
                    Walk([tryCatch.TryBody, tryCatch.CatchBody], found);
                    break;
            }
        }
    }

    private static PipelineRunEffect? Describe(Statement statement) => statement switch
    {
        // ── Writes to a connection ───────────────────────────────────────────
        InsertStatement s when Persistent(s.TargetTable) => Effect("INSERT INTO", s.TargetTable, s.Line),
        UpdateStatement s when Persistent(s.TargetTable) => Effect("UPDATE", s.TargetTable, s.Line),
        DeleteStatement s when Persistent(s.TargetTable) => Effect("DELETE FROM", s.TargetTable, s.Line),
        MergeStatement s when Persistent(s.TargetTable) => Effect("MERGE INTO", s.TargetTable, s.Line),
        BulkInsertStatement s when Persistent(s.TargetTable) => Effect("BULK INSERT INTO", s.TargetTable, s.Line),
        SelectStatement { IntoTable: not null } s when Persistent(s.IntoTable!) => Effect("SELECT INTO", s.IntoTable!, s.Line),

        DropTableStatement s when Persistent(s.TargetTable) => Effect("DROP TABLE", s.TargetTable, s.Line),
        TruncateTableStatement s when Persistent(s.TargetTable) => Effect("TRUNCATE TABLE", s.TargetTable, s.Line),
        CreateTableStatement s when Persistent(s.TargetTable) => Effect("CREATE TABLE", s.TargetTable, s.Line),
        AlterTableStatement s when Persistent(s.TargetTable) => Effect("ALTER TABLE", s.TargetTable, s.Line),

        // ── Pushed to a connection unparsed ──────────────────────────────────
        // The body is the connection's own dialect, not ETL-SQL, so nothing here can establish that
        // it only reads. Treating it as an effect is the only honest answer.
        ExecutePushdownStatement s => new PipelineRunEffect(null, "EXECUTE on", Text(s.ConnectionName), s.Line),
        ExecuteRemoteBlockStatement s => new PipelineRunEffect(null, "EXECUTE on", Text(s.ConnectionName), s.Line),

        // ── Reaches outside the process ──────────────────────────────────────
        EmailStatement s => new PipelineRunEffect(null, "SEND EMAIL to", Text(s.To), s.Line),
        ExportStatement s => new PipelineRunEffect(null, "EXPORT to", s.TargetPath, s.Line),
        ExportDatasetStatement => new PipelineRunEffect(null, "EXPORT DATASET", "a published dataset", statement.Line),
        PublishDatasetStatement => new PipelineRunEffect(null, "PUBLISH DATASET", "the dataset catalog", statement.Line),
        FileOperationStatement s => new PipelineRunEffect(
            null, $"{s.Type.ToString().ToUpperInvariant()} FILE", Text(s.Destination ?? s.Source), s.Line),
        DirectoryOperationStatement s => new PipelineRunEffect(
            null, $"{s.Type.ToString().ToUpperInvariant()} DIRECTORY", Text(s.Destination ?? s.Path), s.Line),
        FileTransferStatement s => new PipelineRunEffect(
            null, $"{s.Type.ToString().ToUpperInvariant()} on {s.ConnectionName}", Text(s.RemotePath), s.Line),
        RunScriptStatement s => new PipelineRunEffect(null, "RUN SCRIPT", Text(s.PathExpression), s.Line),
        DockerStatement => new PipelineRunEffect(null, "DOCKER", "a container", statement.Line),

        _ => null,
    };

    private static PipelineRunEffect Effect(string action, TableReference table, int line) =>
        new(null, action, Qualify(table), line);

    /// <summary>
    /// The connection-qualified name. A bare "MERGE INTO Customers" does not tell the author which
    /// database is about to change, which is the one thing the confirmation exists to say.
    /// </summary>
    private static string Qualify(TableReference table) =>
        string.IsNullOrEmpty(table.ConnectionName)
            ? table.TableName
            : $"{table.ConnectionName}.{table.TableName}";

    private static bool Persistent(TableReference table) => !table.TableName.StartsWith('#');

    /// <summary>
    /// A readable rendering of a target expression.
    ///
    /// <para>Literals and names are printed as written. Anything computed becomes a description
    /// rather than a guess, because a confirmation that shows the wrong address is worse than one
    /// that admits it cannot know the address yet.</para>
    /// </summary>
    private static string Text(Expression? expression) => expression switch
    {
        null => "an unnamed target",
        LiteralExpression { Value: { } value } => value.ToString() ?? "an unnamed target",
        IdentifierExpression identifier => identifier.Name,
        VariableExpression variable => $"@{variable.Name.TrimStart('@')}",
        _ => "a value computed at run time",
    };
}
