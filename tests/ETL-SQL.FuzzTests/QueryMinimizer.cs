using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.FuzzTests
{
    public class QueryMinimizer
    {
        public static string Minimize(string originalSql, Func<string, bool> testFunction)
        {
            // First verify if the original SQL actually triggers the failure
            if (!testFunction(originalSql))
            {
                return originalSql;
            }

            string currentSql = originalSql;
            bool progress = true;

            while (progress)
            {
                progress = false;

                // 1. Try parsing current SQL to AST
                Script? script = null;
                try
                {
                    var tokens = new Lexer(currentSql).Tokenize();
                    script = new Parser(tokens, currentSql).Parse();
                }
                catch
                {
                    // If parsing fails, we cannot minimize via AST. Fallback to token-level reduction
                }

                if (script != null && script.Statements.Count > 0)
                {
                    // A. Try statement-level pruning (removing statements one by one)
                    if (script.Statements.Count > 1)
                    {
                        for (int i = 0; i < script.Statements.Count; i++)
                        {
                            var statementsCopy = script.Statements.ToList();
                            statementsCopy.RemoveAt(i);
                            var reducedScript = new Script { Statements = statementsCopy };
                            var candidateSql = reducedScript.ToSql();
                            if (testFunction(candidateSql))
                            {
                                currentSql = candidateSql;
                                progress = true;
                                break;
                            }
                        }
                        if (progress) continue;
                    }

                    // B. Try statement-internal clause pruning
                    for (int sIdx = 0; sIdx < script.Statements.Count; sIdx++)
                    {
                        var stmt = script.Statements[sIdx];
                        if (stmt is SelectStatement selectStmt)
                        {
                            // Try removing WhereClause
                            if (selectStmt.WhereClause != null)
                            {
                                var candidateStmt = selectStmt with { WhereClause = null };
                                var candidateSql = ReplaceStatement(script, sIdx, candidateStmt).ToSql();
                                if (testFunction(candidateSql)) { currentSql = candidateSql; progress = true; break; }
                            }

                            // Try removing HavingClause
                            if (selectStmt.HavingClause != null)
                            {
                                var candidateStmt = selectStmt with { HavingClause = null };
                                var candidateSql = ReplaceStatement(script, sIdx, candidateStmt).ToSql();
                                if (testFunction(candidateSql)) { currentSql = candidateSql; progress = true; break; }
                            }

                            // Try removing GroupBy
                            if (selectStmt.GroupBy != null && selectStmt.GroupBy.Count > 0)
                            {
                                var candidateStmt = selectStmt with { GroupBy = null };
                                var candidateSql = ReplaceStatement(script, sIdx, candidateStmt).ToSql();
                                if (testFunction(candidateSql)) { currentSql = candidateSql; progress = true; break; }
                            }

                            // Try removing OrderBy
                            if (selectStmt.OrderBy != null && selectStmt.OrderBy.Count > 0)
                            {
                                var candidateStmt = selectStmt with { OrderBy = null };
                                var candidateSql = ReplaceStatement(script, sIdx, candidateStmt).ToSql();
                                if (testFunction(candidateSql)) { currentSql = candidateSql; progress = true; break; }
                            }

                            // Try pruning Joins one by one
                            if (selectStmt.Joins != null && selectStmt.Joins.Count > 0)
                            {
                                for (int j = 0; j < selectStmt.Joins.Count; j++)
                                {
                                    var joinsCopy = selectStmt.Joins.ToList();
                                    joinsCopy.RemoveAt(j);
                                    var candidateStmt = selectStmt with { Joins = joinsCopy };
                                    var candidateSql = ReplaceStatement(script, sIdx, candidateStmt).ToSql();
                                    if (testFunction(candidateSql)) { currentSql = candidateSql; progress = true; break; }
                                }
                                if (progress) break;
                            }

                            // Try pruning Columns one by one (keeping at least one column)
                            if (selectStmt.Columns != null && selectStmt.Columns.Count > 1)
                            {
                                for (int c = 0; c < selectStmt.Columns.Count; c++)
                                {
                                    var columnsCopy = selectStmt.Columns.ToList();
                                    columnsCopy.RemoveAt(c);
                                    var candidateStmt = selectStmt with { Columns = columnsCopy };
                                    var candidateSql = ReplaceStatement(script, sIdx, candidateStmt).ToSql();
                                    if (testFunction(candidateSql)) { currentSql = candidateSql; progress = true; break; }
                                }
                                if (progress) break;
                            }
                        }
                    }
                    if (progress) continue;

                    // C. Try expression-level pruning (sub-expression reduction)
                    for (int sIdx = 0; sIdx < script.Statements.Count; sIdx++)
                    {
                        var stmt = script.Statements[sIdx];
                        if (stmt is SelectStatement selectStmt)
                        {
                            var exprList = new List<Expression>();
                            if (selectStmt.WhereClause != null) exprList.Add(selectStmt.WhereClause);
                            if (selectStmt.HavingClause != null) exprList.Add(selectStmt.HavingClause);
                            foreach (var col in selectStmt.Columns)
                            {
                                if (col.Expression != null) exprList.Add(col.Expression);
                            }

                            bool exprPruned = false;
                            foreach (var rootExpr in exprList)
                            {
                                var subExprs = GetSubExpressions(rootExpr).ToList();
                                foreach (var sub in subExprs)
                                {
                                    var replacement = new LiteralExpression(1, TokenType.NUMBER);
                                    var candidateRoot = ReplaceNode(rootExpr, sub, replacement);

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
                                        var candidateSql = ReplaceStatement(script, sIdx, candidateStmt).ToSql();
                                        if (testFunction(candidateSql))
                                        {
                                            currentSql = candidateSql;
                                            progress = true;
                                            exprPruned = true;
                                            break;
                                        }
                                    }
                                }
                                if (exprPruned) break;
                            }
                            if (exprPruned) break;
                        }
                    }
                    if (progress) continue;
                }

                // 2. Token-level delta-debugging fallback
                List<Token> tokenList;
                try
                {
                    tokenList = new Lexer(currentSql).Tokenize().Where(t => t.Type != TokenType.EOF).ToList();
                }
                catch
                {
                    break;
                }

                if (tokenList.Count > 3)
                {
                    for (int i = 0; i < tokenList.Count; i++)
                    {
                        var tokensCopy = tokenList.ToList();
                        tokensCopy.RemoveAt(i);
                        var candidateSql = string.Join(" ", tokensCopy.Select(t => t.Value));
                        if (testFunction(candidateSql))
                        {
                            currentSql = candidateSql;
                            progress = true;
                            break;
                        }
                    }
                    if (progress) continue;
                }
            }

            return currentSql;
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

            return current;
        }
    }
}
