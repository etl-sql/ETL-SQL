using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Extracts cataloged <c>SHARED:&lt;alias&gt;</c> connection aliases referenced by an ETL-SQL/Report-SQL script.
/// Unparseable scripts report <see cref="IsResolved"/> = <c>false</c> so callers can fail closed.
/// </summary>
public static class SharedConnectionReferenceScan
{
    private const string SharedPrefix = "SHARED:";

    public sealed record ScanResult(bool IsResolved, IReadOnlyList<string> Aliases);

    public static ScanResult ExtractAliases(string? scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            return new ScanResult(true, []);

        Script ast;
        try
        {
            ast = new CoreParser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
        }
        catch (Exception)
        {
            return new ScanResult(false, []);
        }

        if (ast.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new ScanResult(false, []);

        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sawUnresolvedDynamicConnection = false;

        foreach (var statement in ast.Statements)
        {
            VisitStatement(statement, aliases, ref sawUnresolvedDynamicConnection);
        }

        // COMPAT_BREAK: 0.23 - known aliases cannot authorize unresolved dependencies.
        if (sawUnresolvedDynamicConnection)
            return new ScanResult(false, []);

        return new ScanResult(true, aliases.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void VisitStatement(
        Statement? statement,
        HashSet<string> aliases,
        ref bool sawUnresolvedDynamicConnection)
    {
        if (statement is null) return;

        switch (statement)
        {
            case RunScriptStatement:
                // The called script is outside this AST. Its connection dependencies are unknown.
                sawUnresolvedDynamicConnection = true;
                break;

            case CreateProcedureStatement procedure:
                VisitStatement(procedure.Body, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case CreateFunctionStatement function:
                VisitStatement(function.Body, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case CreateConnectionStatement conn:
                VisitExpression(conn.TargetExpression, aliases);
                if (conn.Options is not null)
                {
                    foreach (var expr in conn.Options.Values)
                        VisitExpression(expr, aliases);
                }
                if (conn.TargetExpression is not null and not LiteralExpression)
                    sawUnresolvedDynamicConnection = true;
                break;

            case AlterConnectionStatement alterConn:
                VisitExpression(alterConn.TargetExpression, aliases);
                if (alterConn.Options is not null)
                {
                    foreach (var expr in alterConn.Options.Values)
                        VisitExpression(expr, aliases);
                }
                if (alterConn.TargetExpression is not null and not LiteralExpression)
                    sawUnresolvedDynamicConnection = true;
                break;

            case DeclareStatement dec:
                VisitExpression(dec.InitialValue, aliases);
                break;

            case SetVariableStatement setVar:
                VisitExpression(setVar.Value, aliases);
                break;

            case BlockStatement block:
                foreach (var child in block.Statements)
                    VisitStatement(child, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case ParallelStatement parallel:
                foreach (var child in parallel.Body.Statements)
                    VisitStatement(child, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case IfStatement ifStmt:
                VisitStatement(ifStmt.IfBody, aliases, ref sawUnresolvedDynamicConnection);
                if (ifStmt.ElseIfClauses is not null)
                {
                    foreach (var clause in ifStmt.ElseIfClauses)
                        VisitStatement(clause.Body, aliases, ref sawUnresolvedDynamicConnection);
                }
                VisitStatement(ifStmt.ElseBody, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case WhileStatement whileStmt:
                VisitStatement(whileStmt.Body, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case ForStatement forStmt:
                VisitStatement(forStmt.Body, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case ForeachStatement foreachStmt:
                VisitStatement(foreachStmt.Body, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case ParallelForStatement parallelFor:
                VisitStatement(parallelFor.Body, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case TryCatchStatement tryCatch:
                VisitStatement(tryCatch.TryBody, aliases, ref sawUnresolvedDynamicConnection);
                VisitStatement(tryCatch.CatchBody, aliases, ref sawUnresolvedDynamicConnection);
                break;

            case ExecuteRemoteBlockStatement remote:
                VisitStatement(remote.Body, aliases, ref sawUnresolvedDynamicConnection);
                break;
        }
    }

    private static void VisitExpression(Expression? expression, HashSet<string> aliases)
    {
        if (expression is null) return;

        switch (expression)
        {
            case LiteralExpression { Value: string raw }:
                TryAddAlias(raw, aliases);
                break;
            case BinaryExpression bin:
                VisitExpression(bin.Left, aliases);
                VisitExpression(bin.Right, aliases);
                break;
            case UnaryExpression un:
                VisitExpression(un.Expression, aliases);
                break;
            case FunctionCallExpression func:
                foreach (var arg in func.Arguments)
                    VisitExpression(arg, aliases);
                break;
            case ListExpression list:
                foreach (var item in list.Items)
                    VisitExpression(item, aliases);
                break;
            case CaseExpression c:
                VisitExpression(c.InputExpression, aliases);
                foreach (var w in c.WhenClauses)
                {
                    VisitExpression(w.Condition, aliases);
                    VisitExpression(w.Result, aliases);
                }
                VisitExpression(c.ElseResult, aliases);
                break;
        }
    }

    private static void TryAddAlias(string raw, HashSet<string> aliases)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith(SharedPrefix, StringComparison.OrdinalIgnoreCase))
            return;

        var alias = trimmed[SharedPrefix.Length..].Trim();
        if (alias.Length > 0)
            aliases.Add(alias);
    }
}
