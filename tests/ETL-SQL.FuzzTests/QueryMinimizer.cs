using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.FuzzTests
{
    /// <summary>
    /// Delta-debugging minimizer that operates on the original <see cref="Token"/> stream rather than
    /// re-lexing a space-joined string. Re-lexing was lossy: synthetic single-token identifiers the
    /// generator emits (e.g. an IDENTIFIER whose value is <c>src.Users</c>) re-lex into
    /// <c>IDENTIFIER DOT IDENTIFIER</c>, so a string round-trip could fail to reproduce the crash.
    /// AST-level pruning still round-trips through <c>ToSql()</c> (which is well-formed and lexes
    /// faithfully); only the initial reproduction check and the token-level fallback need the raw
    /// tokens.
    /// </summary>
    public class QueryMinimizer
    {
        public static List<Token> Minimize(List<Token> originalTokens, Func<List<Token>, bool> testFunction)
        {
            // Verify the original tokens actually trigger the failure before trying to shrink them.
            if (!testFunction(originalTokens))
            {
                return originalTokens;
            }

            var current = originalTokens;
            bool progress = true;

            while (progress)
            {
                progress = false;

                // 1. AST-level pruning (statement / clause / sub-expression) via faithful ToSql round-trip.
                var script = TryParse(current);
                if (script != null && script.Statements.Count > 0)
                {
                    foreach (var candidate in EnumerateReducedScripts(script))
                    {
                        var candidateTokens = Lex(candidate.ToSql());
                        if (testFunction(candidateTokens))
                        {
                            current = candidateTokens;
                            progress = true;
                            break;
                        }
                    }
                    if (progress) continue;
                }

                // 2. Token-level delta-debugging fallback on the real token stream.
                var body = current.Where(t => t.Type != TokenType.EOF).ToList();
                if (body.Count > 3)
                {
                    for (int i = 0; i < body.Count; i++)
                    {
                        var reduced = new List<Token>(body);
                        reduced.RemoveAt(i);
                        var reducedTokens = WithEof(reduced);
                        if (testFunction(reducedTokens))
                        {
                            current = reducedTokens;
                            progress = true;
                            break;
                        }
                    }
                    if (progress) continue;
                }
            }

            return current;
        }

        /// <summary>Human-readable rendering of a minimized token stream for the reproducer file.</summary>
        public static string Render(IEnumerable<Token> tokens) =>
            string.Join(" ", tokens.Where(t => t.Type != TokenType.EOF).Select(t => t.Value));

        private static Script? TryParse(List<Token> tokens)
        {
            try
            {
                return new Parser(tokens, Render(tokens)).Parse();
            }
            catch
            {
                return null;
            }
        }

        private static List<Token> Lex(string sql) => new Lexer(sql).Tokenize();

        private static List<Token> WithEof(List<Token> body)
        {
            var last = body.Count > 0 ? body[^1] : new Token(TokenType.EOF, string.Empty, 1, 1, 1, 1);
            var result = new List<Token>(body)
            {
                new Token(TokenType.EOF, string.Empty, last.EndLine, last.EndColumn, last.EndLine, last.EndColumn)
            };
            return result;
        }

        /// <summary>
        /// Yields progressively-reduced copies of <paramref name="script"/>: dropping whole statements,
        /// then clauses/joins/columns, then sub-expressions. Callers test each candidate and adopt the
        /// first that still reproduces the failure.
        /// </summary>
        private static IEnumerable<Script> EnumerateReducedScripts(Script script)
        {
            // A. Statement-level pruning.
            if (script.Statements.Count > 1)
            {
                for (int i = 0; i < script.Statements.Count; i++)
                {
                    var copy = script.Statements.ToList();
                    copy.RemoveAt(i);
                    yield return new Script { Statements = copy };
                }
            }

            // B. Statement-internal clause / join / column pruning.
            for (int sIdx = 0; sIdx < script.Statements.Count; sIdx++)
            {
                if (script.Statements[sIdx] is not SelectStatement selectStmt) continue;

                if (selectStmt.WhereClause != null)
                    yield return ReplaceStatement(script, sIdx, selectStmt with { WhereClause = null });

                if (selectStmt.HavingClause != null)
                    yield return ReplaceStatement(script, sIdx, selectStmt with { HavingClause = null });

                if (selectStmt.GroupBy != null && selectStmt.GroupBy.Count > 0)
                    yield return ReplaceStatement(script, sIdx, selectStmt with { GroupBy = null });

                if (selectStmt.OrderBy != null && selectStmt.OrderBy.Count > 0)
                    yield return ReplaceStatement(script, sIdx, selectStmt with { OrderBy = null });

                if (selectStmt.Joins != null && selectStmt.Joins.Count > 0)
                {
                    for (int j = 0; j < selectStmt.Joins.Count; j++)
                    {
                        var joinsCopy = selectStmt.Joins.ToList();
                        joinsCopy.RemoveAt(j);
                        yield return ReplaceStatement(script, sIdx, selectStmt with { Joins = joinsCopy });
                    }
                }

                if (selectStmt.Columns != null && selectStmt.Columns.Count > 1)
                {
                    for (int c = 0; c < selectStmt.Columns.Count; c++)
                    {
                        var columnsCopy = selectStmt.Columns.ToList();
                        columnsCopy.RemoveAt(c);
                        yield return ReplaceStatement(script, sIdx, selectStmt with { Columns = columnsCopy });
                    }
                }
            }

            // C. Sub-expression pruning (replace a sub-expression with a literal).
            for (int sIdx = 0; sIdx < script.Statements.Count; sIdx++)
            {
                if (script.Statements[sIdx] is not SelectStatement selectStmt) continue;

                var roots = new List<Expression>();
                if (selectStmt.WhereClause != null) roots.Add(selectStmt.WhereClause);
                if (selectStmt.HavingClause != null) roots.Add(selectStmt.HavingClause);
                foreach (var col in selectStmt.Columns)
                {
                    if (col.Expression != null) roots.Add(col.Expression);
                }

                foreach (var rootExpr in roots)
                {
                    foreach (var sub in GetSubExpressions(rootExpr).ToList())
                    {
                        var replacement = new LiteralExpression(1, TokenType.NUMBER);
                        var candidateRoot = ReplaceNode(rootExpr, sub, replacement);
                        if (candidateRoot == rootExpr) continue;

                        SelectStatement? candidateStmt = null;
                        if (rootExpr == selectStmt.WhereClause) candidateStmt = selectStmt with { WhereClause = candidateRoot };
                        else if (rootExpr == selectStmt.HavingClause) candidateStmt = selectStmt with { HavingClause = candidateRoot };
                        else
                        {
                            var colsCopy = selectStmt.Columns.ToList();
                            int colIdx = colsCopy.FindIndex(c => c.Expression == rootExpr);
                            if (colIdx >= 0)
                            {
                                colsCopy[colIdx] = new SelectColumn(candidateRoot, colsCopy[colIdx].Alias, colsCopy[colIdx].Metadata);
                                candidateStmt = selectStmt with { Columns = colsCopy };
                            }
                        }

                        if (candidateStmt != null)
                        {
                            yield return ReplaceStatement(script, sIdx, candidateStmt);
                        }
                    }
                }
            }
        }

        private static Script ReplaceStatement(Script script, int index, Statement newStatement)
        {
            var statementsCopy = script.Statements.ToList();
            statementsCopy[index] = newStatement;
            return new Script { Statements = statementsCopy };
        }

        private static IEnumerable<Expression> GetSubExpressions(Expression expr)
        {
            if (expr is BinaryExpression binary)
            {
                yield return binary.Left;
                yield return binary.Right;
                foreach (var sub in GetSubExpressions(binary.Left)) yield return sub;
                foreach (var sub in GetSubExpressions(binary.Right)) yield return sub;
            }
            else if (expr is FunctionCallExpression func)
            {
                foreach (var arg in func.Arguments)
                {
                    yield return arg;
                    foreach (var sub in GetSubExpressions(arg)) yield return sub;
                }
            }
            else if (expr is UnaryExpression unary)
            {
                yield return unary.Expression;
                foreach (var sub in GetSubExpressions(unary.Expression)) yield return sub;
            }
            else if (expr is CaseExpression caseExpr)
            {
                if (caseExpr.InputExpression != null)
                {
                    yield return caseExpr.InputExpression;
                    foreach (var sub in GetSubExpressions(caseExpr.InputExpression)) yield return sub;
                }
                foreach (var (cond, res) in caseExpr.WhenClauses)
                {
                    yield return cond;
                    foreach (var sub in GetSubExpressions(cond)) yield return sub;
                    yield return res;
                    foreach (var sub in GetSubExpressions(res)) yield return sub;
                }
                if (caseExpr.ElseResult != null)
                {
                    yield return caseExpr.ElseResult;
                    foreach (var sub in GetSubExpressions(caseExpr.ElseResult)) yield return sub;
                }
            }
        }

        private static Expression ReplaceNode(Expression current, Expression target, Expression replacement)
        {
            if (current == target)
            {
                return replacement;
            }

            if (current is BinaryExpression binary)
            {
                var newLeft = ReplaceNode(binary.Left, target, replacement);
                var newRight = ReplaceNode(binary.Right, target, replacement);
                if (newLeft != binary.Left || newRight != binary.Right)
                {
                    return new BinaryExpression(newLeft, binary.Operator, newRight)
                    {
                        Line = binary.Line,
                        Column = binary.Column,
                        EndLine = binary.EndLine,
                        EndColumn = binary.EndColumn
                    };
                }
            }
            else if (current is FunctionCallExpression func)
            {
                bool changed = false;
                var newArgs = new List<Expression>();
                foreach (var arg in func.Arguments)
                {
                    var newArg = ReplaceNode(arg, target, replacement);
                    if (newArg != arg) changed = true;
                    newArgs.Add(newArg);
                }
                if (changed)
                {
                    return new FunctionCallExpression(func.FunctionName, newArgs)
                    {
                        Line = func.Line,
                        Column = func.Column,
                        EndLine = func.EndLine,
                        EndColumn = func.EndColumn
                    };
                }
            }
            else if (current is UnaryExpression unary)
            {
                var newInner = ReplaceNode(unary.Expression, target, replacement);
                if (newInner != unary.Expression)
                {
                    return new UnaryExpression(unary.Operator, newInner)
                    {
                        Line = unary.Line,
                        Column = unary.Column,
                        EndLine = unary.EndLine,
                        EndColumn = unary.EndColumn
                    };
                }
            }
            else if (current is CaseExpression caseExpr)
            {
                bool changed = false;
                var newInput = caseExpr.InputExpression;
                if (caseExpr.InputExpression != null)
                {
                    newInput = ReplaceNode(caseExpr.InputExpression, target, replacement);
                    if (newInput != caseExpr.InputExpression) changed = true;
                }

                var newWhens = new List<(Expression, Expression)>();
                foreach (var (cond, res) in caseExpr.WhenClauses)
                {
                    var nc = ReplaceNode(cond, target, replacement);
                    var nr = ReplaceNode(res, target, replacement);
                    if (nc != cond || nr != res) changed = true;
                    newWhens.Add((nc, nr));
                }

                var newElse = caseExpr.ElseResult;
                if (caseExpr.ElseResult != null)
                {
                    newElse = ReplaceNode(caseExpr.ElseResult, target, replacement);
                    if (newElse != caseExpr.ElseResult) changed = true;
                }

                if (changed)
                {
                    return new CaseExpression(newWhens, newElse, newInput)
                    {
                        Line = caseExpr.Line,
                        Column = caseExpr.Column,
                        EndLine = caseExpr.EndLine,
                        EndColumn = caseExpr.EndColumn
                    };
                }
            }

            return current;
        }
    }
}
