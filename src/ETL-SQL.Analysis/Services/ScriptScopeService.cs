using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Analysis.Services;

/// <summary>One variable a task can read, and where it got its value.</summary>
/// <param name="Origin">declared | assigned | loop</param>
public sealed record ScopeVariable(
    string Name,
    string? Type,
    string? Value,
    int Line,
    string Origin);

/// <summary>One <c>#temp</c> table a task can read.</summary>
/// <param name="Origin">The statement kind that produced it, in words the author will recognise.</param>
public sealed record ScopeTempTable(
    string Name,
    IReadOnlyList<ScopeColumn> Columns,
    int Line,
    string Origin);

public sealed record ScopeColumn(string Name, string? Type);

/// <summary>
/// What is in scope where a task sits.
/// </summary>
/// <param name="Resolved">
/// False when the script does not parse, or when it holds no task by that name. The caller says so
/// rather than rendering an empty scope, which would read as "nothing is in scope here".
/// </param>
public sealed record ScriptScope(
    bool Resolved,
    string? Error,
    IReadOnlyList<ScopeVariable> Variables,
    IReadOnlyList<ScopeTempTable> TempTables)
{
    public static ScriptScope Failed(string error) => new(false, error, [], []);
}

public interface IScriptScopeProjection
{
    ScriptScope At(string? scriptText, string? taskId);
}

/// <summary>
/// Reports what a pipeline task can see from where it sits in the script.
///
/// <para>Positional, not script-wide. ETL-SQL runs a script top to bottom, so a variable declared
/// below a task is not something that task can read, and a <c>#temp</c> table created below it does
/// not exist yet. A panel that listed every name in the file would be telling the author they can
/// use things that are not there — the most expensive kind of wrong, because it is only wrong at run
/// time.</para>
///
/// <para>Enclosing blocks count, and their loop variables come with them: a task inside
/// <c>FOREACH @row IN #orders</c> can read <c>@row</c>, and one outside that loop cannot. What a
/// sibling branch of the same block produced does not count either, which is exactly the distinction
/// a flat list would lose.</para>
/// </summary>
public sealed class ScriptScopeService : IScriptScopeProjection
{
    public ScriptScope At(string? scriptText, string? taskId)
    {
        if (string.IsNullOrWhiteSpace(scriptText)) return ScriptScope.Failed("There is no script to read.");
        if (string.IsNullOrWhiteSpace(taskId)) return ScriptScope.Failed("No task is selected.");

        Script ast;
        try
        {
            ast = new CoreParser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
        }
        catch (Exception ex)
        {
            return ScriptScope.Failed($"Could not read the script: {ex.Message}");
        }

        if (ast.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } diagnostic)
            return ScriptScope.Failed($"Could not read the script: {diagnostic.Message}");

        var collector = new Collector(scriptText, taskId);
        return collector.Walk(ast.Statements)
            ? new ScriptScope(true, null, collector.Variables, collector.TempTables)
            : ScriptScope.Failed($"'{taskId}' is not a task in this script.");
    }

    /// <summary>
    /// Walks the script in execution order and stops at the task, keeping what came before it.
    /// </summary>
    private sealed class Collector(string script, string taskId)
    {
        private readonly Dictionary<string, ScopeVariable> _variables = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ScopeTempTable> _tempTables = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>In the order they were declared, because that is the order the author wrote them.</summary>
        private readonly List<string> _variableOrder = [];
        private readonly List<string> _tempOrder = [];

        public IReadOnlyList<ScopeVariable> Variables => [.. _variableOrder.Select(name => _variables[name])];
        public IReadOnlyList<ScopeTempTable> TempTables => [.. _tempOrder.Select(name => _tempTables[name])];

        /// <summary>True when the task was found in this list or under it.</summary>
        public bool Walk(IReadOnlyList<Statement> statements)
        {
            for (var i = 0; i < statements.Count; i++)
            {
                if (statements[i] is SectionLabelStatement label
                    && string.Equals(label.LabelName, taskId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var bodies = Bodies(statements[i]).ToList();
                if (bodies.Count > 0 && Holds(statements[i]))
                {
                    // The task is inside this one, so what the container itself binds comes into
                    // scope — and nothing after it does.
                    Bind(statements[i]);
                    foreach (var body in bodies)
                    {
                        if (Walk(body)) return true;
                    }

                    return true;
                }

                Record(statements[i]);
            }

            return false;
        }

        /// <summary>True when this statement contains the task, at any depth.</summary>
        private bool Holds(Statement statement) =>
            Bodies(statement).Any(body => body.Any(nested =>
                (nested is SectionLabelStatement label
                    && string.Equals(label.LabelName, taskId, StringComparison.OrdinalIgnoreCase))
                || Holds(nested)));

        /// <summary>What a container gives the tasks inside it.</summary>
        private void Bind(Statement container)
        {
            switch (container)
            {
                case ForeachStatement loop:
                    AddVariable(loop.VariableName, null, Text(loop.ListExpression), loop.Line, "loop");
                    break;
                case ForStatement loop:
                    AddVariable(loop.VariableName, "INT", Text(loop.StartValue), loop.Line, "loop");
                    break;
                case ParallelForStatement loop:
                    AddVariable(loop.VariableName, "INT", Text(loop.StartValue), loop.Line, "loop");
                    break;
                default:
                    break;
            }
        }

        /// <summary>What a statement leaves behind for whatever runs after it.</summary>
        private void Record(Statement statement)
        {
            switch (statement)
            {
                case DeclareStatement declare:
                    AddVariable(declare.VariableName, declare.DataType, Text(declare.InitialValue), declare.Line, "declared");
                    break;

                case SetVariableStatement set:
                    AddVariable(set.VariableName, null, Text(set.Value), set.Line, "assigned");
                    break;

                case SelectStatement select when IsTemp(select.IntoTable):
                    AddTemp(select.IntoTable!.TableName, SelectColumns(select), select.Line, "SELECT INTO");
                    break;

                case CreateTableStatement create when IsTemp(create.TargetTable):
                    AddTemp(
                        create.TargetTable.TableName,
                        [.. create.Columns.Select(column => new ScopeColumn(column.ColumnName, column.DataType))],
                        create.Line,
                        "CREATE TABLE");
                    break;

                case TransformStatement transform when IsTemp(transform.TargetTable):
                    AddTemp(transform.TargetTable.TableName, [], transform.Line, "TRANSFORM");
                    break;

                case BulkInsertStatement bulk when IsTemp(bulk.TargetTable):
                    AddTemp(bulk.TargetTable.TableName, [], bulk.Line, "BULK INSERT");
                    break;

                case CreateDatasetStatement dataset:
                    AddTemp(dataset.TempTableName, [], dataset.Line, "CREATE DATASET");
                    break;

                default:
                    break;
            }
        }

        private static bool IsTemp(TableReference? table) =>
            table?.TableName?.StartsWith('#') == true;

        /// <summary>
        /// The column names a <c>SELECT … INTO</c> produces, as far as the text says.
        ///
        /// <para>An alias, or a bare column, names itself. Anything else — an expression with no
        /// alias, a <c>*</c> — is left out rather than guessed at: an invented name in this panel is
        /// one the author would type and the engine would reject.</para>
        /// </summary>
        private static IReadOnlyList<ScopeColumn> SelectColumns(SelectStatement select) =>
        [
            .. select.Columns
                .Select(column => column.Alias
                    ?? (column.Expression is IdentifierExpression identifier ? identifier.Name : null)
                    ?? (column.Expression is MemberAccessExpression member ? member.MemberName : null))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => new ScopeColumn(name!, null)),
        ];

        private void AddVariable(string name, string? type, string? value, int line, string origin)
        {
            var key = name.TrimStart('@');
            if (key.Length == 0) return;

            // A re-assignment updates the value the task will actually see, and keeps the type and
            // the line of the declaration that introduced it.
            if (_variables.TryGetValue(key, out var existing))
            {
                _variables[key] = existing with { Value = value ?? existing.Value, Origin = origin == "loop" ? "loop" : existing.Origin };
                return;
            }

            _variables[key] = new ScopeVariable("@" + key, type, value, line, origin);
            _variableOrder.Add(key);
        }

        private void AddTemp(string name, IReadOnlyList<ScopeColumn> columns, int line, string origin)
        {
            var key = name.StartsWith('#') ? name : "#" + name;
            if (_tempTables.TryGetValue(key, out var existing))
            {
                // Written to again: keep where it was created, take whatever columns are now known.
                if (columns.Count > 0) _tempTables[key] = existing with { Columns = columns };
                return;
            }

            _tempTables[key] = new ScopeTempTable(key, columns, line, origin);
            _tempOrder.Add(key);
        }

        /// <summary>
        /// An expression as the author wrote it.
        ///
        /// <para>From the source where the offsets allow it, because the serializer reformats — it
        /// turns <c>-1</c> into <c>(0 - 1)</c> — and this panel is meant to show the author their own
        /// script back.</para>
        /// </summary>
        private string? Text(Expression? expression)
        {
            if (expression is null) return null;
            if (expression.StartOffset >= 0
                && expression.EndOffset > expression.StartOffset
                && expression.EndOffset <= script.Length)
            {
                return script[expression.StartOffset..expression.EndOffset].Trim();
            }

            try
            {
                return expression.ToSql();
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<IReadOnlyList<Statement>> Bodies(Statement statement)
        {
            switch (statement)
            {
                case BlockStatement block: yield return block.Statements; break;
                case ParallelStatement parallel: yield return parallel.Body.Statements; break;
                case IfStatement conditional:
                    yield return Body(conditional.IfBody);
                    foreach (var clause in conditional.ElseIfClauses ?? [])
                        yield return Body(clause.Body);
                    if (conditional.ElseBody is not null) yield return Body(conditional.ElseBody);
                    break;
                case TryCatchStatement tryCatch:
                    yield return Body(tryCatch.TryBody);
                    yield return Body(tryCatch.CatchBody);
                    break;
                case WhileStatement loop: yield return Body(loop.Body); break;
                case ForStatement loop: yield return Body(loop.Body); break;
                case ForeachStatement loop: yield return Body(loop.Body); break;
                case ParallelForStatement loop: yield return Body(loop.Body); break;
                default: break;
            }
        }

        private static IReadOnlyList<Statement> Body(Statement body) =>
            body is BlockStatement block ? block.Statements : [body];
    }
}
