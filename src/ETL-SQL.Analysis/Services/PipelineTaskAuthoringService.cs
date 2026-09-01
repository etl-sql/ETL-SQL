using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// One editable task on the pipeline canvas: a top-level section label immediately followed by the
/// <c>EXECUTE &lt;connection&gt; BEGIN … END</c> block it introduces.
///
/// <para><c>Id</c> is the label name, and it is the whole reason a task is addressable at all. Node
/// ids in the read-only projection are positional (<c>s0</c>, <c>s1</c>, …), so inserting a statement
/// by hand renumbers every node after it and the canvas would lose track of which box the author was
/// editing. A label is written into the script, survives a hand edit, and is what the author sees.</para>
/// </summary>
public sealed record PipelineTask(
    string Id,
    PipelineTaskKind Kind,
    string Connection,
    string Body,
    int Line,
    int StartOffset,
    int EndOffset,
    IReadOnlyList<PipelineDependency> DependsOn,
    bool Guarded = false,
    string? Gate = null,
    int StatementStart = -1,
    int InnerStart = -1,
    int InnerEnd = -1);

/// <summary>
/// When a task runs relative to the one it waits for.
///
/// <para>Every condition other than <see cref="Always"/> costs the script something visible: the
/// task it waits for is wrapped in a <c>BEGIN TRY</c> / <c>BEGIN CATCH</c> guard that records its
/// outcome, and the dependent is wrapped in the <c>IF</c> that reads it. Both are written into the
/// file, so the run-time behaviour is the script's, not the canvas's.</para>
/// </summary>
public enum PipelineEdgeCondition
{
    /// <summary>Plain precedence. A failure upstream still aborts the run, as it always has.</summary>
    Always,

    /// <summary>Runs only when the task it waits for finished without throwing.</summary>
    OnSuccess,

    /// <summary>Runs only when the task it waits for threw.</summary>
    OnFailure,

    /// <summary>
    /// Runs either way. The upstream failure is caught and no longer aborts the run, which is the
    /// whole point of the edge and the reason it is not the default.
    /// </summary>
    OnCompletion,

    /// <summary>Runs when the author's own expression is true.</summary>
    Expression,
}

/// <summary>
/// One declared prerequisite, and the condition under which it hands over.
/// </summary>
/// <param name="Expression">The author's condition, for <see cref="PipelineEdgeCondition.Expression"/>.</param>
public sealed record PipelineDependency(
    string Id,
    PipelineEdgeCondition Condition = PipelineEdgeCondition.Always,
    string? Expression = null);

/// <summary>
/// What a task does, and therefore which statement the canvas writes for it.
///
/// <para>Every kind is one top-level statement under one label. That is the constraint that keeps a
/// task addressable and a delete exact; a kind that needed several statements would need a different
/// identity model, so it does not belong here.</para>
/// </summary>
public enum PipelineTaskKind
{
    /// <summary><c>EXECUTE &lt;connection&gt; BEGIN … END</c> — SQL pushed to a connection.</summary>
    Execution,

    /// <summary><c>COPY FILE &lt;source&gt; TO &lt;target&gt;</c>.</summary>
    FileOperation,

    /// <summary><c>ASSERT &lt;condition&gt;, &lt;message&gt;</c> — a quality gate that halts the run.</summary>
    Validation,

    /// <summary><c>SEND EMAIL TO … SUBJECT … BODY … AT &lt;connection&gt;</c>.</summary>
    Notification,
}

/// <summary>
/// A task to add. Which fields matter depends on <c>Kind</c>; the rest are ignored rather than
/// validated, so a caller can carry one draft across a kind change without losing what it typed.
/// </summary>
/// <param name="After">Id of the task it follows, or null to append at the end of the script.</param>
public sealed record PipelineTaskDraft(
    string Id,
    PipelineTaskKind Kind = PipelineTaskKind.Execution,
    string? Connection = null,
    string? Body = null,
    string? Source = null,
    string? Target = null,
    string? Condition = null,
    string? Message = null,
    string? Recipient = null,
    string? Sender = null,
    string? Subject = null,
    string? After = null);

/// <summary>
/// The outcome of an edit. A refusal carries the reason rather than handing back the original script
/// as if nothing had been asked: a canvas that silently keeps its old bytes looks exactly like a
/// canvas that applied the edit, which is the failure shape this surface must not have.
/// </summary>
public sealed record PipelineEditResult(bool Applied, string Script, string? Error = null)
{
    public static PipelineEditResult Ok(string script) => new(true, script);
    public static PipelineEditResult Refused(string script, string error) => new(false, script, error);
}

/// <summary>
/// Reads and edits pipeline tasks in `.etlsql` text without disturbing anything else in the file.
///
/// <para>Every edit is a span replacement computed from the canonical parse: an insertion, a
/// relocation, or a replacement of one token run. Bytes outside the affected span — including line
/// endings, comments, indentation, and every statement the canvas does not model — come through
/// unchanged, and the result is reparsed before it is returned. An edit that would produce a script
/// the parser rejects is refused with its reason, never applied and never silently dropped.</para>
///
/// <para>Deliberately narrow: only a top-level label followed directly by a statement this service
/// models is a task. A label inside a block is scoped to that block, and a statement with no label
/// has no stable identity to edit against, so both are left alone and are visible on the canvas as
/// ordinary read-only projection stages.</para>
///
/// <para>A conditional precedence edge is the one thing that puts a wrapper around a task's
/// statement: a <c>BEGIN TRY</c> guard on the task being watched, recording its outcome in
/// <c>@&lt;label&gt;_status</c>, and an <c>IF</c> gate on the task that waits, reading it. Both are
/// derived from the <c>-- @after:</c> declaration rather than tracked beside it, so a hand-edited
/// tag produces the control flow it describes and a removed edge takes its wrapper with it. Only a
/// wrapper carrying that bookkeeping is treated as this service's; anything else is the author's
/// control flow and is never rewritten.</para>
/// </summary>
public sealed partial class PipelineTaskAuthoringService
{
    /// <summary>Labels the canvas writes: an identifier the lexer reads back as one token.</summary>
    public static bool IsValidTaskId(string? id) =>
        !string.IsNullOrEmpty(id)
        && (char.IsLetter(id[0]) || id[0] == '_')
        && id.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>The tasks a script declares, in script order.</summary>
    public IReadOnlyList<PipelineTask> Read(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)) return [];
        if (!TryParse(script, out var ast, out _)) return [];
        return ReadTasks(script, ast);
    }

    /// <summary>Adds a task after <c>draft.After</c>, or at the end of the script.</summary>
    public PipelineEditResult Add(string? script, PipelineTaskDraft draft)
    {
        var source = script ?? string.Empty;
        ArgumentNullException.ThrowIfNull(draft);

        if (!IsValidTaskId(draft.Id))
            return PipelineEditResult.Refused(source, $"'{draft.Id}' is not a usable task label.");
        if (Incomplete(draft) is { } missing)
            return PipelineEditResult.Refused(source, missing);

        if (!TryParse(source, out var ast, out var parseError))
            return PipelineEditResult.Refused(source, parseError);

        var tasks = ReadTasks(source, ast);
        if (tasks.Any(task => string.Equals(task.Id, draft.Id, StringComparison.OrdinalIgnoreCase)))
            return PipelineEditResult.Refused(source, $"This script already has a task called '{draft.Id}'.");

        var lineEnding = DetectLineEnding(source);
        var text = RenderTask(draft, lineEnding);

        int insertAt;
        if (draft.After is null)
        {
            insertAt = source.Length;
        }
        else
        {
            var anchor = Find(tasks, draft.After);
            if (anchor is null)
                return PipelineEditResult.Refused(source, $"No task called '{draft.After}' to add after.");
            insertAt = EndOfLine(source, anchor.EndOffset);
        }

        var prefix = NeedsBlankLineBefore(source, insertAt) ? lineEnding : string.Empty;
        var suffix = insertAt >= source.Length ? string.Empty : lineEnding;
        return Commit(source, Splice(source, insertAt, insertAt, prefix + text + suffix));
    }

    /// <summary>
    /// The reason this draft cannot be written yet, or null.
    ///
    /// <para>Checked before anything is rendered, so a half-filled task never reaches the script and
    /// is never refused later by the parser with a message about syntax the author never typed.</para>
    /// </summary>
    private static string? Incomplete(PipelineTaskDraft draft) => draft.Kind switch
    {
        PipelineTaskKind.Execution when !IsValidTaskId(draft.Connection) =>
            $"'{draft.Connection}' is not a usable connection alias.",
        PipelineTaskKind.Execution when string.IsNullOrWhiteSpace(draft.Body) =>
            "An execution task needs the SQL it runs.",

        PipelineTaskKind.FileOperation when string.IsNullOrWhiteSpace(draft.Source) =>
            "A file task needs a source path.",
        PipelineTaskKind.FileOperation when string.IsNullOrWhiteSpace(draft.Target) =>
            "A file task needs a target path.",

        PipelineTaskKind.Validation when string.IsNullOrWhiteSpace(draft.Condition) =>
            "A validation task needs a condition to assert.",
        PipelineTaskKind.Validation when string.IsNullOrWhiteSpace(draft.Message) =>
            "A validation task needs the message it fails with.",

        PipelineTaskKind.Notification when !IsValidTaskId(draft.Connection) =>
            $"'{draft.Connection}' is not a usable connection alias.",
        PipelineTaskKind.Notification when string.IsNullOrWhiteSpace(draft.Recipient) =>
            "A notification task needs a recipient.",
        PipelineTaskKind.Notification when string.IsNullOrWhiteSpace(draft.Sender) =>
            "A notification task needs a sender address.",
        PipelineTaskKind.Notification when string.IsNullOrWhiteSpace(draft.Subject) =>
            "A notification task needs a subject.",
        PipelineTaskKind.Notification when string.IsNullOrWhiteSpace(draft.Body) =>
            "A notification task needs a body.",

        _ => null,
    };

    /// <summary>Removes a task and the label that names it.</summary>
    public PipelineEditResult Remove(string? script, string id)
    {
        var source = script ?? string.Empty;
        if (!TryParse(source, out var ast, out var parseError))
            return PipelineEditResult.Refused(source, parseError);

        var task = Find(ReadTasks(source, ast), id);
        if (task is null) return PipelineEditResult.Refused(source, $"No task called '{id}'.");
        if (GotoTargets(ast).Contains(task.Id))
            return PipelineEditResult.Refused(source, $"'{task.Id}' is a GOTO target; removing it would break the jump.");

        var (start, end) = RemovableSpan(source, task);
        var committed = Commit(source, Splice(source, start, end, string.Empty));

        // A gate reading the status of a task that is no longer in the script would fail the run on
        // a line the author never wrote, so the edges that watched it come out with it.
        return committed.Applied ? Normalize(source, committed.Script) : committed;
    }

    /// <summary>
    /// Moves a task so it runs after <paramref name="afterId"/>, or first when that is null.
    ///
    /// <para>This is what "connect" means for a sequential flow: order in the script is the
    /// dependency. The task's own bytes are relocated, not regenerated, so a hand-formatted body
    /// comes out of a move exactly as it went in.</para>
    /// </summary>
    public PipelineEditResult Move(string? script, string id, string? afterId)
    {
        var source = script ?? string.Empty;
        if (!TryParse(source, out var ast, out var parseError))
            return PipelineEditResult.Refused(source, parseError);

        var tasks = ReadTasks(source, ast);
        var task = Find(tasks, id);
        if (task is null) return PipelineEditResult.Refused(source, $"No task called '{id}'.");

        if (string.Equals(id, afterId, StringComparison.OrdinalIgnoreCase))
            return PipelineEditResult.Refused(source, "A task cannot be moved after itself.");
        if (GotoTargets(ast).Contains(task.Id))
            return PipelineEditResult.Refused(source, $"'{task.Id}' is a GOTO target; moving it would change where the jump lands.");

        PipelineTask? anchor = null;
        if (afterId is not null)
        {
            anchor = Find(tasks, afterId);
            if (anchor is null) return PipelineEditResult.Refused(source, $"No task called '{afterId}' to move after.");
        }

        var lineEnding = DetectLineEnding(source);
        var (start, end) = RemovableSpan(source, task);

        // Relocate exactly what the span covers — declaration included — rather than the statement
        // alone, so a task keeps what it waits on when it is dragged somewhere else.
        var moved = source[start..end].Trim('\r', '\n');

        var insertAt = anchor is null ? FirstTaskAnchor(source, tasks) : EndOfLine(source, anchor.EndOffset);
        if (insertAt >= start && insertAt <= end)
            return PipelineEditResult.Ok(source); // Already in that position; moving it would be a no-op edit.

        // Cut first or insert first, whichever keeps the other offset valid.
        var builder = new StringBuilder(source);
        var payload = lineEnding + moved + lineEnding;
        if (insertAt > end)
        {
            builder.Insert(insertAt, payload);
            builder.Remove(start, end - start);
        }
        else
        {
            builder.Remove(start, end - start);
            builder.Insert(insertAt, payload);
        }

        return Commit(source, Tidy(builder.ToString(), lineEnding));
    }

    /// <summary>
    /// Relabels a task, repoints its connection, or replaces its body. Null leaves that part alone,
    /// and each part is written over its own token run, so changing a label does not reflow a body.
    /// </summary>
    public PipelineEditResult Update(
        string? script,
        string id,
        string? newId = null,
        string? connection = null,
        string? body = null)
    {
        var source = script ?? string.Empty;
        if (!TryParse(source, out var ast, out var parseError))
            return PipelineEditResult.Refused(source, parseError);

        var tasks = ReadTasks(source, ast);
        var task = Find(tasks, id);
        if (task is null) return PipelineEditResult.Refused(source, $"No task called '{id}'.");

        if (newId is not null)
        {
            if (!IsValidTaskId(newId))
                return PipelineEditResult.Refused(source, $"'{newId}' is not a usable task label.");
            if (!string.Equals(newId, task.Id, StringComparison.OrdinalIgnoreCase)
                && tasks.Any(other => string.Equals(other.Id, newId, StringComparison.OrdinalIgnoreCase)))
                return PipelineEditResult.Refused(source, $"This script already has a task called '{newId}'.");
            if (GotoTargets(ast).Contains(task.Id))
                return PipelineEditResult.Refused(source, $"'{task.Id}' is a GOTO target; renaming it would break the jump.");
        }
        if (connection is not null && !IsValidTaskId(connection))
            return PipelineEditResult.Refused(source, $"'{connection}' is not a usable connection alias.");

        // A rename changes the status variable the guard writes and the gate reads. The wrappers
        // come off first and the normalisation puts them back under the new name; rewriting them in
        // place would mean editing several spans whose offsets all move as the label changes length,
        // which is exactly the shape of edit this service exists to avoid.
        if (newId is not null
            && !string.Equals(newId, task.Id, StringComparison.OrdinalIgnoreCase)
            && (task.Guarded || task.Gate is not null)
            && task.InnerStart >= 0
            && task.StatementStart >= 0)
        {
            var lineEnding = DetectLineEnding(source);
            var unwrapped = Splice(
                source,
                task.StatementStart,
                task.EndOffset,
                ComposeStatement(source[task.InnerStart..task.InnerEnd], null, null, lineEnding));

            // The status DECLARE names the old label too, and it sits above the statement, so its
            // span survives the splice above and is removed second.
            var bare = Commit(source, WriteStatusDeclaration(unwrapped, task, guarded: false, lineEnding));
            if (!bare.Applied) return PipelineEditResult.Refused(source, bare.Error!);

            var renamed = Update(bare.Script, id, newId, connection, body);
            return renamed.Applied
                ? renamed
                : PipelineEditResult.Refused(source, renamed.Error ?? "The rename could not be applied.");
        }

        var slice = source[task.StartOffset..task.EndOffset];
        var spans = TokenSpans(slice, task.StartOffset);
        if (spans is null)
            return PipelineEditResult.Refused(source, $"Could not locate the parts of '{task.Id}' in the script.");

        // Applied back to front so an earlier replacement cannot shift a later offset.
        var edits = new List<(int Start, int End, string Text)>();
        if (newId is not null) edits.Add((spans.Value.LabelStart, spans.Value.LabelEnd, newId));
        if (connection is not null) edits.Add((spans.Value.ConnectionStart, spans.Value.ConnectionEnd, connection));
        if (body is not null)
        {
            var lineEnding = DetectLineEnding(source);
            edits.Add((spans.Value.BodyStart, spans.Value.BodyEnd, RenderBody(body, lineEnding)));
        }
        if (edits.Count == 0) return PipelineEditResult.Ok(source);

        var patched = source;
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            patched = Splice(patched, edit.Start, edit.End, edit.Text);

        var committed = Commit(source, patched);
        if (!committed.Applied || newId is null || string.Equals(newId, task.Id, StringComparison.OrdinalIgnoreCase))
            return committed;

        // The edges that named the old label follow it. Dropping them instead would silently undo
        // work the author did on the canvas, and leaving them would strand a gate on a name the
        // script no longer has.
        return Normalize(source, Repoint(committed.Script, task.Id, newId));
    }

    // ── Dependencies ─────────────────────────────────────────────────────────

    /// <summary>The tag a task declares its dependencies with.</summary>
    private const string AfterTag = "-- @after:";

    /// <summary>
    /// Declares that <paramref name="toId"/> runs after <paramref name="fromId"/>, on the given
    /// condition.
    ///
    /// <para>The edge is written into the script as an <c>-- @after:</c> tag above the task's label,
    /// which the lexer reads as a tag and the parser skips between statements, so the declaration
    /// costs nothing at run time and the script stays the source of truth.</para>
    ///
    /// <para>The engine runs a script top to bottom, so a declared dependency that contradicted the
    /// physical order would be a lie the canvas told about the file. Connecting therefore also moves
    /// the dependent task below the one it now waits for, when it was not already.</para>
    ///
    /// <para>A condition other than <see cref="PipelineEdgeCondition.Always"/> is not a note on the
    /// diagram: <see cref="Normalize"/> then writes the <c>BEGIN TRY</c> guard that records the
    /// upstream outcome and the <c>IF</c> that reads it, so running the file does what the canvas
    /// draws.</para>
    /// </summary>
    public PipelineEditResult Connect(
        string? script,
        string fromId,
        string toId,
        PipelineEdgeCondition condition = PipelineEdgeCondition.Always,
        string? expression = null)
    {
        var source = script ?? string.Empty;
        if (!TryParse(source, out var ast, out var parseError))
            return PipelineEditResult.Refused(source, parseError);

        if (condition == PipelineEdgeCondition.Expression && string.IsNullOrWhiteSpace(expression))
            return PipelineEditResult.Refused(source, "A conditional edge needs the expression it runs on.");
        if (condition == PipelineEdgeCondition.Expression && UnusableExpression(expression!) is { } bad)
            return PipelineEditResult.Refused(source, bad);

        var tasks = ReadTasks(source, ast);
        var from = Find(tasks, fromId);
        var to = Find(tasks, toId);
        if (from is null) return PipelineEditResult.Refused(source, $"No task called '{fromId}'.");
        if (to is null) return PipelineEditResult.Refused(source, $"No task called '{toId}'.");
        if (string.Equals(from.Id, to.Id, StringComparison.OrdinalIgnoreCase))
            return PipelineEditResult.Refused(source, "A task cannot depend on itself.");

        var existing = to.DependsOn.FirstOrDefault(dependency =>
            string.Equals(dependency.Id, from.Id, StringComparison.OrdinalIgnoreCase));
        var declaration = new PipelineDependency(from.Id, condition, expression?.Trim());
        if (existing == declaration) return PipelineEditResult.Ok(source);

        // A cycle cannot be executed by a linear script, and drawing one would make the canvas claim
        // something the engine can never do.
        if (existing is null && DependsOnTransitively(tasks, from.Id, to.Id))
            return PipelineEditResult.Refused(source,
                $"'{from.Id}' already waits on '{to.Id}', so this would make a cycle.");

        // Re-declaring an edge with a different condition replaces it in place rather than adding a
        // second prerequisite on the same task, which would read as "waits for all 2" of one thing.
        var declared = existing is null
            ? to.DependsOn.Append(declaration).ToList()
            : to.DependsOn.Select(dependency => dependency == existing ? declaration : dependency).ToList();

        var written = WriteDeclaration(source, to, declared);
        if (written is null) return PipelineEditResult.Refused(source, $"Could not write the dependency onto '{to.Id}'.");

        // Re-read against the rewritten script: the tag line shifts every offset below it.
        var committed = Commit(source, written);
        if (!committed.Applied) return committed;

        if (from.StartOffset > to.StartOffset)
        {
            var reordered = Move(committed.Script, to.Id, from.Id);
            if (reordered.Applied) committed = reordered;
        }

        return Normalize(source, committed.Script);
    }

    /// <summary>Removes one declared dependency, leaving the tasks and their order alone.</summary>
    public PipelineEditResult Disconnect(string? script, string fromId, string toId)
    {
        var source = script ?? string.Empty;
        if (!TryParse(source, out var ast, out var parseError))
            return PipelineEditResult.Refused(source, parseError);

        var to = Find(ReadTasks(source, ast), toId);
        if (to is null) return PipelineEditResult.Refused(source, $"No task called '{toId}'.");
        if (!to.DependsOn.Any(dependency => string.Equals(dependency.Id, fromId, StringComparison.OrdinalIgnoreCase)))
            return PipelineEditResult.Refused(source, $"'{to.Id}' does not wait on '{fromId}'.");

        var declared = to.DependsOn
            .Where(dependency => !string.Equals(dependency.Id, fromId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var written = WriteDeclaration(source, to, declared);
        if (written is null)
            return PipelineEditResult.Refused(source, $"Could not rewrite the dependencies of '{to.Id}'.");

        var committed = Commit(source, written);
        return committed.Applied ? Normalize(source, committed.Script) : committed;
    }

    /// <summary>
    /// Rewrites every declaration naming <paramref name="oldId"/> to name <paramref name="newId"/>.
    ///
    /// <para>One task per pass, reparsing between, because rewriting a tag moves every offset below
    /// it — the same reason <see cref="Normalize"/> works that way.</para>
    /// </summary>
    private string Repoint(string script, string oldId, string newId)
    {
        var current = script;
        for (var pass = 0; pass < NormalizePasses; pass++)
        {
            if (!TryParse(current, out var ast, out _)) return current;

            var stale = ReadTasks(current, ast).FirstOrDefault(task =>
                task.DependsOn.Any(dependency => string.Equals(dependency.Id, oldId, StringComparison.OrdinalIgnoreCase)));
            if (stale is null) return current;

            var declared = stale.DependsOn
                .Select(dependency => string.Equals(dependency.Id, oldId, StringComparison.OrdinalIgnoreCase)
                    ? dependency with { Id = newId }
                    : dependency)
                .ToList();

            // The gate names the old status variable, so it is rewritten with the declaration.
            var written = WriteDeclaration(current, stale with { Gate = null }, declared);
            if (written is null) return current;
            current = written;
        }

        return current;
    }

    /// <summary>True when <paramref name="taskId"/> waits on <paramref name="candidate"/>, directly or through others.</summary>
    private static bool DependsOnTransitively(IReadOnlyList<PipelineTask> tasks, string taskId, string candidate)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([taskId]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var dependency in Find(tasks, current)?.DependsOn ?? [])
                pending.Push(dependency.Id);
        }

        return false;
    }

    /// <summary>
    /// The reason an author's edge expression cannot be written into the script, or null.
    ///
    /// <para>It ends up inside an <c>IF</c> the canvas emits, so a fragment carrying a statement
    /// terminator or a comment opener would not stay inside it. Refusing here says so in the
    /// inspector; letting it through would produce a parse error about syntax nobody typed, or —
    /// worse — a script that parses and means something else.</para>
    /// </summary>
    private static string? UnusableExpression(string expression) =>
        expression.Contains(';', StringComparison.Ordinal) ? "An edge condition cannot contain ';'."
        : expression.Contains("--", StringComparison.Ordinal) ? "An edge condition cannot contain a comment."
        : expression.Contains("/*", StringComparison.Ordinal) ? "An edge condition cannot contain a comment."
        : expression.AsSpan().ContainsAny('\r', '\n') ? "An edge condition has to fit on one line."
        : null;

    /// <summary>
    /// Rewrites a task's declaration and the gate that enforces it, in one edit.
    ///
    /// <para>The two have to move together. A gate carrying the author's own expression is only
    /// recognisable as this service's while the declaration explaining it is still on the line above;
    /// rewrite the tag first and the <c>IF</c> becomes indistinguishable from one the author wrote,
    /// and a later pass would leave it behind, gating a task on an edge the canvas no longer draws.</para>
    ///
    /// <para>The statement is spliced before the tag because it sits below it, so the tag's offsets
    /// survive the first edit.</para>
    /// </summary>
    private static string? WriteDeclaration(string script, PipelineTask task, IReadOnlyList<PipelineDependency> declared)
    {
        var gate = GateCondition(declared);
        if (SameCondition(gate, task.Gate) || task.InnerStart < 0 || task.StatementStart < 0)
            return WriteDependencies(script, task, declared);

        var patched = Splice(
            script,
            task.StatementStart,
            task.EndOffset,
            ComposeStatement(script[task.InnerStart..task.InnerEnd], gate, task.Guarded ? task.Id : null, DetectLineEnding(script)));

        return WriteDependencies(patched, task, declared);
    }

    /// <summary>
    /// Rewrites a task's <c>-- @after:</c> declaration, adding, replacing, or removing it as needed.
    ///
    /// <para>Only those lines are touched. An empty list removes the tag rather than leaving
    /// <c>-- @after:</c> behind, because a tag declaring nothing reads as a dependency the reader
    /// cannot see.</para>
    ///
    /// <para>Plain prerequisites share one line, the way they always have. An edge carrying the
    /// author's own expression gets a line to itself, because a comma inside that expression would
    /// otherwise read as the start of another prerequisite.</para>
    /// </summary>
    private static string? WriteDependencies(string script, PipelineTask task, IReadOnlyList<PipelineDependency> declared)
    {
        var lineEnding = DetectLineEnding(script);
        var indent = LabelIndent(script, task.StartOffset);
        var (tagStart, tagEnd) = DependencyTagSpan(script, task.StartOffset);

        var shared = declared.Where(dependency => dependency.Condition != PipelineEdgeCondition.Expression).ToList();
        var lines = new List<string>();
        if (shared.Count > 0) lines.Add(string.Join(", ", shared.Select(RenderDependency)));
        lines.AddRange(declared
            .Where(dependency => dependency.Condition == PipelineEdgeCondition.Expression)
            .Select(RenderDependency));

        var replacement = string.Concat(lines.Select(line => $"{indent}{AfterTag} {line}{lineEnding}"));
        return Splice(script, tagStart, tagEnd, replacement);
    }

    /// <summary>One prerequisite as the tag spells it.</summary>
    private static string RenderDependency(PipelineDependency dependency) => dependency.Condition switch
    {
        PipelineEdgeCondition.OnSuccess => $"{dependency.Id} on success",
        PipelineEdgeCondition.OnFailure => $"{dependency.Id} on failure",
        PipelineEdgeCondition.OnCompletion => $"{dependency.Id} on completion",
        PipelineEdgeCondition.Expression => $"{dependency.Id} when {dependency.Expression}",
        _ => dependency.Id,
    };

    /// <summary>
    /// The span of the task's existing <c>-- @after:</c> declaration, or an empty span where one
    /// would go.
    ///
    /// <para>Only the run of tag lines immediately above the label counts. A tag further up belongs
    /// to whatever sits between them, and claiming it would attach one task's dependencies to
    /// another.</para>
    /// </summary>
    private static (int Start, int End) DependencyTagSpan(string script, int labelStart)
    {
        var lineStart = StartOfLine(script, labelStart);
        var start = lineStart;
        while (start > 0)
        {
            var previousStart = StartOfLine(script, start - 1);
            if (!script[previousStart..start].TrimStart().StartsWith(AfterTag, StringComparison.OrdinalIgnoreCase))
                break;
            start = previousStart;
        }

        return (start, lineStart);
    }

    /// <summary>
    /// The prerequisites a task declares, in the order the author wrote them.
    ///
    /// <para>A name the grammar cannot use as a label is dropped rather than carried as a dependency
    /// on nothing — the author is mid-edit, and a half-typed name is not an edge.</para>
    /// </summary>
    private static List<PipelineDependency> ReadDependencies(string script, int labelStart)
    {
        var (start, end) = DependencyTagSpan(script, labelStart);
        if (end <= start) return [];

        var declared = new List<PipelineDependency>();
        foreach (var raw in script[start..end].Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(AfterTag, StringComparison.OrdinalIgnoreCase)) continue;
            var body = line[AfterTag.Length..].Trim();
            if (body.Length == 0) continue;

            // An expression owns the rest of its line: splitting it on commas would cut a function
            // call in half and turn the tail into a prerequisite the script never declared.
            var when = body.IndexOf(" when ", StringComparison.OrdinalIgnoreCase);
            if (when >= 0)
            {
                var id = body[..when].Trim();
                var expression = body[(when + " when ".Length)..].Trim();
                if (IsValidTaskId(id) && expression.Length > 0)
                    declared.Add(new PipelineDependency(id, PipelineEdgeCondition.Expression, expression));
                continue;
            }

            foreach (var item in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (id, condition) = SplitCondition(item);
                if (IsValidTaskId(id)) declared.Add(new PipelineDependency(id, condition));
            }
        }

        return declared;
    }

    /// <summary>Splits <c>name on success</c> into its name and its condition.</summary>
    private static (string Id, PipelineEdgeCondition Condition) SplitCondition(string item)
    {
        foreach (var (suffix, condition) in TagSuffixes)
        {
            if (item.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return (item[..^suffix.Length].Trim(), condition);
        }

        return (item, PipelineEdgeCondition.Always);
    }

    private static readonly (string Suffix, PipelineEdgeCondition Condition)[] TagSuffixes =
    [
        (" on success", PipelineEdgeCondition.OnSuccess),
        (" on failure", PipelineEdgeCondition.OnFailure),
        (" on completion", PipelineEdgeCondition.OnCompletion),
    ];

    // ── Guards and gates ─────────────────────────────────────────────────────

    /// <summary>The variable a guarded task records its outcome in: 0 not run, 1 succeeded, -1 threw.</summary>
    private static string StatusName(string id) => $"{id}_status";

    private static string StatusVariable(string id) => $"@{StatusName(id)}";

    private static string StatusDeclaration(string id) => $"DECLARE {StatusVariable(id)} INT = 0;";

    /// <summary>
    /// The <c>IF</c> condition a task's declared edges add up to, or null when it needs no gate.
    ///
    /// <para>Several conditions on one task are a join, exactly as several plain prerequisites are:
    /// every one of them has to hold. <see cref="PipelineEdgeCondition.OnCompletion"/> contributes
    /// no term — it says the dependent runs either way, and what it really asks for is the guard on
    /// the task it waits for, so a failure there stops aborting the run.</para>
    /// </summary>
    private static string? GateCondition(IReadOnlyList<PipelineDependency> dependencies)
    {
        var terms = new List<string>();
        foreach (var dependency in dependencies)
        {
            switch (dependency.Condition)
            {
                case PipelineEdgeCondition.OnSuccess:
                    terms.Add($"{StatusVariable(dependency.Id)} = 1");
                    break;
                case PipelineEdgeCondition.OnFailure:
                    terms.Add($"{StatusVariable(dependency.Id)} = -1");
                    break;
                case PipelineEdgeCondition.Expression when !string.IsNullOrWhiteSpace(dependency.Expression):
                    terms.Add($"({dependency.Expression.Trim()})");
                    break;
                default:
                    break;
            }
        }

        return terms.Count == 0 ? null : string.Join(" AND ", terms);
    }

    /// <summary>True when some other task's edge needs this one to report how it finished.</summary>
    private static bool NeedsGuard(IReadOnlyList<PipelineTask> tasks, string id) =>
        tasks.SelectMany(task => task.DependsOn).Any(dependency =>
            dependency.Condition is PipelineEdgeCondition.OnSuccess
                or PipelineEdgeCondition.OnFailure
                or PipelineEdgeCondition.OnCompletion
            && string.Equals(dependency.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Brings the script's control flow back in line with what its tags declare.
    ///
    /// <para>The tag is the declaration; the <c>BEGIN TRY</c> guard and the <c>IF</c> gate are how
    /// the engine obeys it. Writing them from the declaration — rather than alongside it — is what
    /// keeps the two from drifting: a hand-edited tag produces the control flow it describes on the
    /// next canvas edit, and an edge that was removed takes its wrapper with it.</para>
    ///
    /// <para>One task per pass, reparsing between, because every rewrite moves every offset below
    /// it. Scripts are small and edits are one at a time; a clever incremental version of this would
    /// buy nothing and would be the thing that corrupts a file.</para>
    /// </summary>
    private PipelineEditResult Normalize(string original, string script)
    {
        var current = script;
        for (var pass = 0; pass < NormalizePasses; pass++)
        {
            if (!TryParse(current, out var ast, out var parseError))
                return PipelineEditResult.Refused(original, $"The edit would not parse: {parseError}");

            var tasks = ReadTasks(current, ast);
            var next = NextNormalization(current, tasks);
            if (next is null) return PipelineEditResult.Ok(current);

            current = next;
        }

        return PipelineEditResult.Refused(original, "The pipeline's control flow could not be settled.");
    }

    /// <summary>How many rewrites one normalisation may take before it is treated as not converging.</summary>
    private const int NormalizePasses = 128;

    /// <summary>The script with the first out-of-date wrapper rewritten, or null when none is.</summary>
    private static string? NextNormalization(string script, IReadOnlyList<PipelineTask> tasks)
    {
        var lineEnding = DetectLineEnding(script);
        var known = tasks.Select(task => task.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            // A conditional edge onto a task that is no longer in the script would gate on a
            // variable nothing declares. The declaration goes; a plain prerequisite naming a missing
            // task is left alone, because that one is only ever a stale line, never a broken run.
            var live = task.DependsOn
                .Where(dependency => dependency.Condition == PipelineEdgeCondition.Always || known.Contains(dependency.Id))
                .ToList();
            if (live.Count != task.DependsOn.Count)
                return WriteDependencies(script, task, live);

            var gate = GateCondition(task.DependsOn);
            var guard = NeedsGuard(tasks, task.Id);
            if (guard == task.Guarded && SameCondition(gate, task.Gate)) continue;

            if (task.InnerStart < 0 || task.StatementStart < 0) continue;

            var rewritten = Splice(
                script,
                task.StatementStart,
                task.EndOffset,
                ComposeStatement(script[task.InnerStart..task.InnerEnd], gate, guard ? task.Id : null, lineEnding));

            return WriteStatusDeclaration(rewritten, task, guard, lineEnding);
        }

        return null;
    }

    /// <summary>
    /// The task's statement wrapped in the control flow its edges call for.
    ///
    /// <para>The guard is outside the gate on purpose. A task whose gate is false never ran, so its
    /// status stays at the 0 it was declared with — neither success nor failure — and a downstream
    /// <c>on failure</c> edge does not fire for a task that was skipped.</para>
    /// </summary>
    private static string ComposeStatement(string inner, string? gate, string? guardId, string lineEnding)
    {
        var text = Dedent(inner);

        if (gate is not null)
            text = $"IF {gate}{lineEnding}BEGIN{lineEnding}{Indent(text, lineEnding)}{lineEnding}END;";

        if (guardId is not null)
        {
            text = $"BEGIN TRY{lineEnding}{Indent(text, lineEnding)}{lineEnding}"
                + $"    SET {StatusVariable(guardId)} = 1;{lineEnding}"
                + $"END TRY{lineEnding}BEGIN CATCH{lineEnding}"
                + $"    SET {StatusVariable(guardId)} = -1;{lineEnding}"
                + $"    PRINT 'Task {guardId} failed: ' + ERROR_MESSAGE();{lineEnding}"
                + "END CATCH;";
        }

        return text;
    }

    /// <summary>
    /// Removes the indentation a wrapper added, so wrapping and unwrapping are inverses.
    ///
    /// <para>The statement's own first line starts at its offset and carries no leading whitespace,
    /// so the block it belongs to is measured from the lines under it. Without this, every gate an
    /// author adds and removes walks the body four more spaces to the right.</para>
    /// </summary>
    private static string Dedent(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
        if (lines.Count < 2) return text.Trim();

        var common = lines.Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join(
            "\n",
            lines.Select((line, index) => index == 0 || string.IsNullOrWhiteSpace(line) ? line.Trim() : line[common..].TrimEnd()));
    }

    private static string Indent(string text, string lineEnding) =>
        string.Join(lineEnding, text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : "    " + line.TrimEnd()));

    /// <summary>Adds or removes the <c>DECLARE</c> that gives a guarded task somewhere to report to.</summary>
    private static string WriteStatusDeclaration(string script, PipelineTask task, bool guarded, string lineEnding)
    {
        var (start, end) = StatusDeclarationSpan(script, task);
        if (guarded == end > start) return script;

        var indent = LabelIndent(script, task.StartOffset);
        return Splice(script, start, end, guarded ? $"{indent}{StatusDeclaration(task.Id)}{lineEnding}" : string.Empty);
    }

    /// <summary>
    /// The span of a guarded task's status <c>DECLARE</c>, or an empty span where one would go.
    ///
    /// <para>It sits directly above the task's own declaration, so it travels with the task and is
    /// removed with it, rather than being left behind naming something the script no longer has.</para>
    /// </summary>
    private static (int Start, int End) StatusDeclarationSpan(string script, PipelineTask task)
    {
        var (tagStart, _) = DependencyTagSpan(script, task.StartOffset);
        if (tagStart == 0) return (tagStart, tagStart);

        var previousStart = StartOfLine(script, tagStart - 1);
        return string.Equals(script[previousStart..tagStart].Trim(), StatusDeclaration(task.Id), StringComparison.OrdinalIgnoreCase)
            ? (previousStart, tagStart)
            : (tagStart, tagStart);
    }

    /// <summary>The whitespace a task's label sits behind, so a written tag lines up with it.</summary>
    private static string LabelIndent(string script, int labelStart)
    {
        var lineStart = StartOfLine(script, labelStart);
        return script[lineStart..labelStart];
    }

    private static int StartOfLine(string script, int offset)
    {
        var start = Math.Clamp(offset, 0, script.Length);
        while (start > 0 && script[start - 1] != '\n') start--;
        return start;
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<PipelineTask> ReadTasks(string script, Script ast)
    {
        var tasks = new List<PipelineTask>();
        var statements = ast.Statements;

        for (var i = 0; i < statements.Count - 1; i++)
        {
            if (statements[i] is not SectionLabelStatement label) continue;

            var outer = statements[i + 1];
            var start = label.StartOffset;
            var end = outer.EndOffset;
            if (start < 0 || end <= start || end > script.Length) continue;

            var dependencies = ReadDependencies(script, start);

            // The guard and the gate are wrappers this service writes; the task is what sits inside
            // them. Unwrapping is deliberately conservative — a hand-authored TRY/CATCH or IF that
            // does not carry the bookkeeping the canvas writes is left as an ordinary read-only
            // projection stage rather than quietly adopted and then rewritten by a normalisation.
            var inner = Unwrap(script, label.LabelName, outer, dependencies, out var guarded, out var gate);
            if (KindOf(inner) is not { } kind) continue;
            if (inner.StartOffset < start || inner.EndOffset > end || inner.EndOffset <= inner.StartOffset) continue;

            // Only an execution task has a connection and a block body to read back. The others are
            // reported as a task — addressable, movable, deletable — with their statement text as the
            // body, because the canvas has no field editor for their parts yet, and inventing empty
            // fields would read as "this task has no recipient" rather than "not shown here".
            var slice = script[start..end];
            if (kind == PipelineTaskKind.Execution)
            {
                var spans = TokenSpans(slice, start);
                if (spans is null) continue;

                tasks.Add(new PipelineTask(
                    label.LabelName,
                    kind,
                    script[spans.Value.ConnectionStart..spans.Value.ConnectionEnd],
                    script[spans.Value.BodyStart..spans.Value.BodyEnd].Trim('\r', '\n'),
                    label.Line,
                    start,
                    end,
                    dependencies,
                    guarded,
                    gate,
                    outer.StartOffset,
                    inner.StartOffset,
                    inner.EndOffset));
                continue;
            }

            tasks.Add(new PipelineTask(
                label.LabelName,
                kind,
                string.Empty,
                script[inner.StartOffset..inner.EndOffset].Trim(),
                label.Line,
                start,
                end,
                dependencies,
                guarded,
                gate,
                outer.StartOffset,
                inner.StartOffset,
                inner.EndOffset));
        }

        return tasks;
    }

    /// <summary>
    /// Looks through the wrappers a conditional edge writes and reports the statement inside.
    ///
    /// <para>A <c>BEGIN TRY</c> is only this service's guard when its body records the task's outcome
    /// into <c>@&lt;label&gt;_status</c>. An <c>IF</c> is only this service's gate when it reads
    /// nothing but those status variables, or when it says exactly what the task's own declared
    /// conditions say. Anything else is the author's own control flow: it is left intact, and the
    /// task stays outside the editable set rather than being adopted and then rewritten.</para>
    /// </summary>
    private static Statement Unwrap(
        string script,
        string id,
        Statement outer,
        IReadOnlyList<PipelineDependency> dependencies,
        out bool guarded,
        out string? gate)
    {
        guarded = false;
        gate = null;

        var current = outer;
        if (current is TryCatchStatement candidate
            && RecordsStatus(candidate.TryBody, id)
            && Single(candidate.TryBody) is { } inside)
        {
            guarded = true;
            current = inside;
        }

        if (current is IfStatement conditional
            && conditional.ElseBody is null
            && (conditional.ElseIfClauses?.Count ?? 0) == 0
            && Single(conditional.IfBody) is { } gated
            && GateText(script, outer) is { } text
            && IsCanvasGate(text, dependencies))
        {
            gate = text;
            current = gated;
        }

        return current;
    }

    /// <summary>The one statement a wrapper body holds, ignoring the bookkeeping the canvas writes.</summary>
    private static Statement? Single(Statement body)
    {
        var statements = body is BlockStatement block ? block.Statements : [body];
        var real = statements.Where(statement => statement is not SetVariableStatement and not PrintStatement).ToList();
        return real.Count == 1 ? real[0] : null;
    }

    /// <summary>True when this TRY body records the task's outcome, which is what makes it a guard.</summary>
    private static bool RecordsStatus(Statement body, string id)
    {
        var statements = body is BlockStatement block ? block.Statements : [body];
        return statements.OfType<SetVariableStatement>()
            .Any(set => string.Equals(set.VariableName.TrimStart('@'), StatusName(id), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The condition source text of a task's gate, read back by re-lexing the task's own slice.
    ///
    /// <para>The parser records no offsets for expressions, and the serializer reformats them —
    /// <c>-1</c> comes back as <c>(0 - 1)</c> — so neither can answer whether the gate on disk is
    /// still the one the canvas wrote. The tokens can.</para>
    /// </summary>
    private static string? GateText(string script, Statement outer)
    {
        var start = outer.StartOffset;
        var end = outer.EndOffset;
        if (start < 0 || end <= start || end > script.Length) return null;

        List<Token> tokens;
        try
        {
            tokens = new Lexer(script[start..end]).Tokenize();
        }
        catch
        {
            return null;
        }

        var index = 0;
        if (index + 1 < tokens.Count && tokens[index].Type == TokenType.BEGIN && tokens[index + 1].Type == TokenType.TRY)
            index += 2;
        if (index >= tokens.Count || tokens[index].Type != TokenType.IF) return null;

        var conditionStart = index + 1;
        if (conditionStart >= tokens.Count) return null;

        // An expression never contains a bare BEGIN, so the first one closes the condition.
        var body = tokens.FindIndex(conditionStart, token => token.Type == TokenType.BEGIN);
        if (body < 0) return null;

        var text = script[(start + tokens[conditionStart].Offset)..(start + tokens[body].Offset)].Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// True when this <c>IF</c> is one the canvas wrote, and so is safe to rewrite or remove.
    ///
    /// <para>Every conjunct has to be accounted for: either it reads a task status variable — a name
    /// this service owns — or it is a condition the task still declares. A gate one edge out of date
    /// still qualifies, which is what lets an edge be removed without stranding the <c>IF</c> that
    /// enforced it. A condition with a term belonging to neither is the author's, and is left
    /// alone.</para>
    /// </summary>
    private static bool IsCanvasGate(string condition, IReadOnlyList<PipelineDependency> dependencies)
    {
        var declared = dependencies
            .Where(dependency => dependency.Condition == PipelineEdgeCondition.Expression)
            .Select(dependency => Collapse($"({dependency.Expression})"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var terms = condition.Split(" AND ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length > 0
            && terms.All(term => StatusTerm().IsMatch(term) || declared.Contains(Collapse(term)));
    }

    [GeneratedRegex(@"^@[A-Za-z_][A-Za-z0-9_]*_status\s*=\s*-?1$", RegexOptions.IgnoreCase)]
    private static partial Regex StatusTerm();

    /// <summary>Compares two conditions the way the script means them: whitespace is not content.</summary>
    private static bool SameCondition(string? left, string? right) =>
        string.Equals(Collapse(left), Collapse(right), StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string? text) =>
        new((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>The kind a labelled statement represents, or null when the canvas does not model it.</summary>
    private static PipelineTaskKind? KindOf(Statement statement) => statement switch
    {
        ExecutePushdownStatement => PipelineTaskKind.Execution,
        FileOperationStatement => PipelineTaskKind.FileOperation,
        AssertStatement => PipelineTaskKind.Validation,
        EmailStatement => PipelineTaskKind.Notification,
        _ => null,
    };

    private static PipelineTask? Find(IReadOnlyList<PipelineTask> tasks, string? id) =>
        id is null ? null : tasks.FirstOrDefault(task => string.Equals(task.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every label a <c>GOTO</c> anywhere in the script jumps to.
    ///
    /// <para>Walks into control-flow bodies, not just plain blocks: a jump inside an <c>IF</c> is the
    /// normal way one is written, and a rename that only checked the top level would happily break
    /// it. The parse gate would still refuse the result, but "the edit would not parse" is a much
    /// worse answer than "that label is a GOTO target".</para>
    /// </summary>
    private static HashSet<string> GotoTargets(Script ast)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(ast.Statements);
        return targets;

        void Walk(IEnumerable<Statement> statements)
        {
            foreach (var statement in statements)
            {
                if (statement is GotoStatement jump) targets.Add(jump.LabelName);
                foreach (var body in Bodies(statement))
                    Walk(body);
            }
        }
    }

    private static IEnumerable<IEnumerable<Statement>> Bodies(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block: yield return block.Statements; break;
            case ParallelStatement parallel: yield return parallel.Body.Statements; break;
            case IfStatement conditional:
                yield return [conditional.IfBody];
                foreach (var clause in conditional.ElseIfClauses ?? [])
                    yield return [clause.Body];
                if (conditional.ElseBody is not null) yield return [conditional.ElseBody];
                break;
            case TryCatchStatement tryCatch:
                yield return [tryCatch.TryBody];
                yield return [tryCatch.CatchBody];
                break;
            case WhileStatement loop: yield return [loop.Body]; break;
            case ForStatement loop: yield return [loop.Body]; break;
            case ForeachStatement loop: yield return [loop.Body]; break;
            case ParallelForStatement loop: yield return [loop.Body]; break;
        }
    }

    /// <summary>
    /// The offsets of the three editable runs inside a task's own text: the label name, the
    /// connection alias, and the block body between its matching <c>BEGIN</c> and <c>END</c>.
    ///
    /// <para>Found by re-lexing the task's slice rather than by scanning for keywords, so a body that
    /// contains the word BEGIN inside a string or a comment cannot be mistaken for the block's own,
    /// and nested BEGIN/END pairs in the pushed-down SQL are counted rather than tripped over.</para>
    /// </summary>
    private static TaskSpans? TokenSpans(string slice, int origin)
    {
        List<Token> tokens;
        try
        {
            tokens = new Lexer(slice).Tokenize();
        }
        catch
        {
            return null;
        }

        if (tokens.Count < 2 || tokens[1].Type != TokenType.COLON) return null;
        var label = tokens[0];

        var executeIndex = tokens.FindIndex(2, token => token.Type is TokenType.EXECUTE or TokenType.EXEC);
        if (executeIndex < 0 || executeIndex + 1 >= tokens.Count) return null;
        var connection = tokens[executeIndex + 1];

        var beginIndex = tokens.FindIndex(executeIndex, token => token.Type == TokenType.BEGIN);
        if (beginIndex < 0) return null;

        var depth = 0;
        var endIndex = -1;
        for (var i = beginIndex; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.BEGIN) depth++;
            else if (tokens[i].Type == TokenType.END && --depth == 0) { endIndex = i; break; }
        }
        if (endIndex < 0) return null;

        return new TaskSpans(
            origin + label.Offset, origin + label.EndOffset,
            origin + connection.Offset, origin + connection.EndOffset,
            origin + tokens[beginIndex].EndOffset, origin + tokens[endIndex].Offset);
    }

    private readonly record struct TaskSpans(
        int LabelStart, int LabelEnd,
        int ConnectionStart, int ConnectionEnd,
        int BodyStart, int BodyEnd);

    // ── Writing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The statement a task of this kind becomes.
    ///
    /// <para>Every literal goes through <see cref="Literal"/>, so a path or a message containing a
    /// quote is escaped rather than closing the string early and rewriting the rest of the script as
    /// something else entirely. The emitted forms are covered one per kind by focused parse, lint,
    /// formatter, and reference tests — that gate is why the palette offers them at all.</para>
    /// </summary>
    private static string RenderTask(PipelineTaskDraft draft, string lineEnding) => draft.Kind switch
    {
        PipelineTaskKind.Execution =>
            $"{draft.Id}:{lineEnding}EXECUTE {draft.Connection} BEGIN{RenderBody(draft.Body, lineEnding)}END;",

        PipelineTaskKind.FileOperation =>
            $"{draft.Id}:{lineEnding}COPY FILE {Literal(draft.Source)} TO {Literal(draft.Target)};",

        PipelineTaskKind.Validation =>
            $"{draft.Id}:{lineEnding}ASSERT {draft.Condition},{lineEnding}    {Literal(draft.Message)};",

        // FROM is not optional to the parser, whatever the connector's DEFAULT_FROM says, so the
        // sender is a field the author fills in rather than something this quietly omits.
        PipelineTaskKind.Notification =>
            $"{draft.Id}:{lineEnding}SEND EMAIL{lineEnding}"
            + $"    TO {Literal(draft.Recipient)}{lineEnding}"
            + $"    FROM {Literal(draft.Sender)}{lineEnding}"
            + $"    SUBJECT {Literal(draft.Subject)}{lineEnding}"
            + $"    BODY {Literal(draft.Body)}{lineEnding}"
            + $"    AT {draft.Connection};",

        _ => throw new ArgumentOutOfRangeException(nameof(draft), draft.Kind, "Unknown pipeline task kind."),
    };

    /// <summary>A single-quoted ETL-SQL string literal with embedded quotes doubled.</summary>
    private static string Literal(string? value) =>
        "'" + (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string RenderBody(string? body, string lineEnding)
    {
        var lines = (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .SkipWhile(string.IsNullOrWhiteSpace)
            .ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) lines.Add("SELECT 1;");

        var indented = lines.Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : "    " + line.TrimEnd());
        return lineEnding + string.Join(lineEnding, indented) + lineEnding;
    }

    /// <summary>The span a move or a delete takes with it: the task plus the line it sits on.</summary>
    /// <summary>
    /// The span a move or a delete takes with it: the task, the line it sits on, and the
    /// <c>-- @after:</c> declaration above it.
    ///
    /// <para>The tag has to travel with the task. Left behind, it lands above whatever moves up into
    /// its place, silently handing one task's dependencies to another — and the task that was moved
    /// loses the declaration it was moved to satisfy. The status <c>DECLARE</c> a guarded task
    /// writes to travels for the same reason.</para>
    /// </summary>
    private static (int Start, int End) RemovableSpan(string script, PipelineTask task)
    {
        var (declarationStart, declarationEnd) = StatusDeclarationSpan(script, task);
        var (tagStart, tagEnd) = DependencyTagSpan(script, task.StartOffset);
        var start = declarationEnd > declarationStart ? declarationStart
            : tagEnd > tagStart ? tagStart
            : task.StartOffset;

        while (start > 0 && (script[start - 1] == ' ' || script[start - 1] == '\t')) start--;
        if (start > 0 && script[start - 1] == '\n')
        {
            start--;
            if (start > 0 && script[start - 1] == '\r') start--;
        }

        return (start, EndOfLine(script, task.EndOffset));
    }

    /// <summary>Where the first task should land when it is moved to the head of the flow.</summary>
    private static int FirstTaskAnchor(string script, IReadOnlyList<PipelineTask> tasks)
    {
        var first = tasks.FirstOrDefault();
        if (first is null) return script.Length;

        // Before the first existing task, but after whatever precedes it — CREATE CONNECTION, above
        // all, which every task depends on and which the canvas does not model.
        var (start, _) = RemovableSpan(script, first);
        return Math.Max(start, 0);
    }

    private static int EndOfLine(string script, int offset)
    {
        var end = Math.Clamp(offset, 0, script.Length);
        while (end < script.Length && script[end] != '\n' && script[end] != '\r') end++;
        if (end < script.Length && script[end] == '\r') end++;
        if (end < script.Length && script[end] == '\n') end++;
        return end;
    }

    private static bool NeedsBlankLineBefore(string script, int offset)
    {
        if (offset <= 0) return false;
        var text = script[..offset];
        if (text.TrimEnd().Length == 0) return false;
        return !text.EndsWith("\n\n", StringComparison.Ordinal) && !text.EndsWith("\n\r\n", StringComparison.Ordinal);
    }

    private static string Splice(string script, int start, int end, string text) =>
        script[..Math.Clamp(start, 0, script.Length)]
        + text
        + script[Math.Clamp(end, 0, script.Length)..];

    /// <summary>Collapses the run of blank lines a relocation can leave behind, and nothing else.</summary>
    private static string Tidy(string script, string lineEnding)
    {
        var tripled = lineEnding + lineEnding + lineEnding;
        while (script.Contains(tripled, StringComparison.Ordinal))
            script = script.Replace(tripled, lineEnding + lineEnding, StringComparison.Ordinal);
        return script;
    }

    /// <summary>
    /// The gate every edit passes: the result must parse cleanly, or the original script is returned
    /// with the reason. An edit that produces bytes the engine would reject is not an edit worth
    /// keeping, and a canvas that writes one has corrupted the author's file.
    /// </summary>
    private static PipelineEditResult Commit(string original, string patched)
    {
        if (!TryParse(patched, out _, out var error))
            return PipelineEditResult.Refused(original, $"The edit would not parse: {error}");
        return PipelineEditResult.Ok(patched);
    }

    /// <summary>Parses, treating a recovered-but-broken parse as a failure with its first error.</summary>
    private static bool TryParse(string script, out Script ast, out string error)
    {
        ast = new Script();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(script)) return true;

        try
        {
            ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
            var diagnostic = ast.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (diagnostic is null) return true;
            error = string.IsNullOrWhiteSpace(diagnostic.Message) ? "The script could not be parsed." : diagnostic.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = string.IsNullOrWhiteSpace(ex.Message) ? "The script could not be parsed." : ex.Message;
            return false;
        }
    }

    private static string DetectLineEnding(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
