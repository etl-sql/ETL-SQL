using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// The text mechanics every script-shaped authoring surface needs: parse, walk, locate a position,
/// splice a span.
///
/// <para>Shared because the alternative is each surface carrying its own line/offset conversion, and
/// an off-by-one there is not a visible bug — it is a comment spliced one character into the token
/// beside it, in a file the author did not read before saving.</para>
/// </summary>
internal static class ScriptTextEditing
{
    /// <summary>
    /// Parses, treating any error diagnostic as a refusal. Every surface built on this edits the
    /// author's own bytes, and there is no safe edit to make to a file whose shape is not yet known.
    /// </summary>
    public static bool TryParse(string source, out Script ast, out string error)
    {
        ast = new Script();
        error = string.Empty;
        try
        {
            var lexer = new Lexer(source);
            var parser = new CoreParser(lexer.Tokenize(), source);
            ast = parser.Parse();
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        var failure = ast.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (failure is not null)
        {
            error = $"Line {failure.Line}: {failure.Message}";
            return false;
        }
        return true;
    }

    /// <summary>
    /// The character offset of a 1-based line/column pair, or -1 when the position is not in this
    /// text. Several AST nodes carry positions rather than offsets, and an edit is made at an
    /// offset, so the conversion has to happen somewhere; doing it here keeps it in one place.
    /// </summary>
    public static int Offset(string source, int line, int column)
    {
        if (line <= 0 || column <= 0) return -1;

        var offset = 0;
        for (var remaining = line - 1; remaining > 0; remaining--)
        {
            var next = source.IndexOf('\n', offset);
            if (next < 0) return -1;
            offset = next + 1;
        }

        var result = offset + column - 1;
        return result > source.Length ? -1 : result;
    }

    public static string Splice(string script, int start, int end, string text) =>
        script[..start] + text + script[end..];

    public static string DetectLineEnding(string source) => source.Contains("\r\n") ? "\r\n" : "\n";

    public static int EndOfLine(string source, int offset)
    {
        var index = Math.Clamp(offset, 0, source.Length);
        while (index < source.Length && source[index] != '\n') index++;
        return index < source.Length ? index + 1 : index;
    }

    public static int StartOfLine(string source, int offset)
    {
        var index = Math.Clamp(offset, 0, source.Length);
        while (index > 0 && source[index - 1] != '\n') index--;
        return index;
    }

    public static bool NeedsBlankLineBefore(string source, int offset)
    {
        if (offset <= 0 || offset > source.Length) return false;
        var preceding = source[..offset].TrimEnd('\r', '\n');
        if (preceding.Length == 0) return false;
        var newlines = source[preceding.Length..offset].Count(character => character == '\n');
        return newlines < 2;
    }

    /// <summary>Every statement in the script, containers walked into, in written order.</summary>
    public static IEnumerable<Statement> Flatten(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;

            IEnumerable<Statement> children = statement switch
            {
                BlockStatement block => block.Statements,
                IfStatement conditional => new[] { conditional.IfBody }
                    .Concat(conditional.ElseIfClauses?.Select(clause => clause.Body) ?? [])
                    .Concat(conditional.ElseBody is null ? [] : [conditional.ElseBody]),
                WhileStatement loop => [loop.Body],
                ForStatement loop => [loop.Body],
                ForeachStatement loop => [loop.Body],
                TryCatchStatement guard => [guard.TryBody, guard.CatchBody],
                CreateProcedureStatement procedure => [procedure.Body],
                CreateFunctionStatement function => [function.Body],
                ParallelStatement parallel => [parallel.Body],
                ParallelForStatement parallel => [parallel.Body],
                ExecuteRemoteBlockStatement remote => [remote.Body],
                _ => [],
            };

            foreach (var child in Flatten(children))
                yield return child;
        }
    }

    /// <summary>Yields every query statement nested inside <paramref name="expr"/>.</summary>
    public static IEnumerable<Statement> EnumerateSubqueries(Expression? expr)
    {
        switch (expr)
        {
            case SubqueryExpression subq when subq.Query != null:
                yield return subq.Query;
                break;
            case ExistsExpression exists when exists.Subquery != null:
                yield return exists.Subquery;
                break;
            case InExpression inExpr:
                foreach (var q in EnumerateSubqueries(inExpr.Left)) yield return q;
                foreach (var q in EnumerateSubqueries(inExpr.Right)) yield return q;
                break;
            case BinaryExpression bin:
                foreach (var q in EnumerateSubqueries(bin.Left)) yield return q;
                foreach (var q in EnumerateSubqueries(bin.Right)) yield return q;
                break;
            case UnaryExpression un:
                foreach (var q in EnumerateSubqueries(un.Expression)) yield return q;
                break;
            case FunctionCallExpression func:
                foreach (var arg in func.Arguments)
                    foreach (var q in EnumerateSubqueries(arg)) yield return q;
                foreach (var q in EnumerateSubqueries(func.Filter)) yield return q;
                break;
            case CaseExpression c:
                foreach (var q in EnumerateSubqueries(c.InputExpression)) yield return q;
                foreach (var w in c.WhenClauses)
                {
                    foreach (var q in EnumerateSubqueries(w.Condition)) yield return q;
                    foreach (var q in EnumerateSubqueries(w.Result)) yield return q;
                }
                foreach (var q in EnumerateSubqueries(c.ElseResult)) yield return q;
                break;
            case BetweenExpression b:
                foreach (var q in EnumerateSubqueries(b.Left)) yield return q;
                foreach (var q in EnumerateSubqueries(b.Start)) yield return q;
                foreach (var q in EnumerateSubqueries(b.End)) yield return q;
                break;
            case LikeExpression like:
                foreach (var q in EnumerateSubqueries(like.Left)) yield return q;
                foreach (var q in EnumerateSubqueries(like.Pattern)) yield return q;
                foreach (var q in EnumerateSubqueries(like.EscapeChar)) yield return q;
                break;
            case IsNullExpression isNull:
                foreach (var q in EnumerateSubqueries(isNull.Expression)) yield return q;
                break;
            case IsDistinctFromExpression idf:
                foreach (var q in EnumerateSubqueries(idf.Left)) yield return q;
                foreach (var q in EnumerateSubqueries(idf.Right)) yield return q;
                break;
            case ListExpression list:
                foreach (var item in list.Items)
                    foreach (var q in EnumerateSubqueries(item)) yield return q;
                break;
            case AtTimeZoneExpression atz:
                foreach (var q in EnumerateSubqueries(atz.Left)) yield return q;
                foreach (var q in EnumerateSubqueries(atz.TimeZone)) yield return q;
                break;
        }
    }

    /// <summary>Yields every query nested inside a <see cref="SelectStatement"/>'s expressions and clauses.</summary>
    public static IEnumerable<Statement> EnumerateNestedSelectQueries(SelectStatement select)
    {
        foreach (var cte in select.Ctes ?? [])
            if (cte.Query != null) yield return cte.Query;
        if (select.FromTable?.Subquery != null) yield return select.FromTable.Subquery;
        foreach (var join in select.Joins ?? [])
        {
            if (join.Table?.Subquery != null) yield return join.Table.Subquery;
            foreach (var q in EnumerateSubqueries(join.Condition)) yield return q;
        }
        foreach (var col in select.Columns ?? [])
            foreach (var q in EnumerateSubqueries(col.Expression)) yield return q;
        foreach (var q in EnumerateSubqueries(select.WhereClause)) yield return q;
        foreach (var q in EnumerateSubqueries(select.HavingClause)) yield return q;
        foreach (var q in EnumerateSubqueries(select.QualifyClause)) yield return q;
    }
}
