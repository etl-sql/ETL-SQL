using System.Text;
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
    string Connection,
    string Body,
    int Line,
    int StartOffset,
    int EndOffset);

/// <summary>A task to add: its label, the connection it runs against, and the SQL in the block.</summary>
/// <param name="After">Id of the task it follows, or null to append at the end of the script.</param>
public sealed record PipelineTaskDraft(string Id, string Connection, string Body, string? After = null);

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
/// <para>Deliberately narrow: only a top-level label followed directly by an execute block is a task.
/// A label inside a block is scoped to that block, and an execute block with no label has no stable
/// identity to edit against, so both are left alone and are visible on the canvas as ordinary
/// read-only projection stages.</para>
/// </summary>
public sealed class PipelineTaskAuthoringService
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
        if (!IsValidTaskId(draft.Connection))
            return PipelineEditResult.Refused(source, $"'{draft.Connection}' is not a usable connection alias.");

        if (!TryParse(source, out var ast, out var parseError))
            return PipelineEditResult.Refused(source, parseError);

        var tasks = ReadTasks(source, ast);
        if (tasks.Any(task => string.Equals(task.Id, draft.Id, StringComparison.OrdinalIgnoreCase)))
            return PipelineEditResult.Refused(source, $"This script already has a task called '{draft.Id}'.");

        var lineEnding = DetectLineEnding(source);
        var text = RenderTask(draft.Id, draft.Connection, draft.Body, lineEnding);

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
        return Commit(source, Splice(source, start, end, string.Empty));
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
        var moved = source[task.StartOffset..task.EndOffset];

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

        return Commit(source, patched);
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<PipelineTask> ReadTasks(string script, Script ast)
    {
        var tasks = new List<PipelineTask>();
        var statements = ast.Statements;

        for (var i = 0; i < statements.Count - 1; i++)
        {
            if (statements[i] is not SectionLabelStatement label) continue;
            if (statements[i + 1] is not ExecutePushdownStatement execute) continue;

            var start = label.StartOffset;
            var end = execute.EndOffset;
            if (start < 0 || end <= start || end > script.Length) continue;

            var spans = TokenSpans(script[start..end], start);
            if (spans is null) continue;

            tasks.Add(new PipelineTask(
                label.LabelName,
                script[spans.Value.ConnectionStart..spans.Value.ConnectionEnd],
                script[spans.Value.BodyStart..spans.Value.BodyEnd].Trim('\r', '\n'),
                label.Line,
                start,
                end));
        }

        return tasks;
    }

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

    private static string RenderTask(string id, string connection, string? body, string lineEnding) =>
        $"{id}:{lineEnding}EXECUTE {connection} BEGIN{RenderBody(body, lineEnding)}END;";

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
    private static (int Start, int End) RemovableSpan(string script, PipelineTask task)
    {
        var start = task.StartOffset;
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
