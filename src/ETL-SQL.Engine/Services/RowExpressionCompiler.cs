using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

internal static class RowExpressionCompiler
{
    public delegate object? RowValue(Row row);
    public delegate bool RowPredicate(Row row);

    public static bool TryCompileValue(IExecutionContext context, Expression? expression, out RowValue compiled)
    {
        if (context.OuterRowStack.Count > 0)
        {
            compiled = static _ => null;
            return false;
        }

        if (expression == null)
        {
            compiled = static _ => null;
            return true;
        }

        return TryCompile(context, expression, out compiled);
    }

    public static bool TryCompilePredicate(IExecutionContext context, Expression? expression, out RowPredicate compiled)
    {
        if (context.OuterRowStack.Count > 0)
        {
            compiled = static _ => false;
            return false;
        }

        if (expression == null)
        {
            compiled = static _ => true;
            return true;
        }

        if (!TryCompile(context, expression, out var value))
        {
            compiled = static _ => false;
            return false;
        }

        compiled = row => ToPredicate(value(row));
        return true;
    }

    private static bool TryCompile(IExecutionContext context, Expression expression, out RowValue compiled)
    {
        switch (expression)
        {
            case LiteralExpression lit:
                if (lit.Value is string s && s.StartsWith("__HEX_BLOB__", StringComparison.Ordinal))
                {
                    compiled = static _ => null;
                    return false;
                }

                var constant = lit.Value;
                compiled = _ => constant;
                return true;

            case IdentifierExpression id:
                if (id.Name.StartsWith("@", StringComparison.Ordinal))
                {
                    compiled = static _ => null;
                    return false;
                }
                if (id.Name.Equals("*", StringComparison.OrdinalIgnoreCase)
                    || id.Name.EndsWith(".CONNECTION_STRING", StringComparison.OrdinalIgnoreCase)
                    || context.Connections.ContainsKey(id.Name)
                    || context.FunctionRegistry.IsRegistered(id.Name))
                {
                    compiled = static _ => null;
                    return false;
                }

                var name = id.Name;
                compiled = row => ResolveIdentifier(name, row);
                return true;

            case UnaryExpression unary:
                return TryCompileUnary(context, unary, out compiled);

            case BinaryExpression binary:
                return TryCompileBinary(context, binary, out compiled);

            case IsNullExpression isNull:
                if (!TryCompile(context, isNull.Expression, out var inner))
                {
                    compiled = static _ => null;
                    return false;
                }

                compiled = row =>
                {
                    var value = inner(row);
                    var result = value == null
                        || value == DBNull.Value
                        || (value is string s
                            && string.IsNullOrEmpty(s)
                            && context.VarContext.GetVariable("NULL_AS_EMPTY")?.ToString() == "TRUE");
                    return isNull.Not ? !result : result;
                };
                return true;

            default:
                compiled = static _ => null;
                return false;
        }
    }

    private static bool TryCompileUnary(IExecutionContext context, UnaryExpression unary, out RowValue compiled)
    {
        if (!TryCompile(context, unary.Expression, out var inner))
        {
            compiled = static _ => null;
            return false;
        }

        compiled = unary.Operator switch
        {
            TokenType.NOT => row =>
            {
                var value = inner(row);
                if (value == null || value == DBNull.Value) return null;
                try { return !Convert.ToBoolean(value); } catch { return null; }
            },
            TokenType.MINUS => row =>
            {
                var value = inner(row);
                if (value == null || value == DBNull.Value) return null;
                try { return -Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return null; }
            },
            TokenType.PLUS => row =>
            {
                var value = inner(row);
                if (value == null || value == DBNull.Value) return null;
                try { return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture); } catch { return value; }
            },
            _ => static _ => null
        };

        return unary.Operator is TokenType.NOT or TokenType.MINUS or TokenType.PLUS;
    }

    private static bool TryCompileBinary(IExecutionContext context, BinaryExpression binary, out RowValue compiled)
    {
        if (!TryCompile(context, binary.Left, out var left) || !TryCompile(context, binary.Right, out var right))
        {
            compiled = static _ => null;
            return false;
        }

        compiled = binary.Operator switch
        {
            TokenType.AND => row =>
            {
                var leftValue = left(row);
                if (!IsNull(leftValue) && !Convert.ToBoolean(leftValue)) return false;

                var rightValue = right(row);
                if (!IsNull(rightValue) && !Convert.ToBoolean(rightValue)) return false;
                if (IsNull(leftValue) || IsNull(rightValue)) return null;
                return true;
            },
            TokenType.OR => row =>
            {
                var leftValue = left(row);
                if (!IsNull(leftValue) && Convert.ToBoolean(leftValue)) return true;

                var rightValue = right(row);
                if (!IsNull(rightValue) && Convert.ToBoolean(rightValue)) return true;
                if (IsNull(leftValue) || IsNull(rightValue)) return null;
                return false;
            },
            TokenType.PLUS or TokenType.MINUS or TokenType.STAR or TokenType.SLASH
                or TokenType.MODULO or TokenType.LSHIFT or TokenType.RSHIFT => row =>
            {
                var result = BinaryOperatorFactory.Execute(binary.Operator, left(row), right(row));
                return result;
            },
            TokenType.EQUALS => row =>
            {
                var leftValue = left(row);
                var rightValue = right(row);
                return IsNull(leftValue) || IsNull(rightValue) ? null : context.IsSoftEqual(leftValue, rightValue);
            },
            TokenType.NOT_EQUALS => row =>
            {
                var leftValue = left(row);
                var rightValue = right(row);
                return IsNull(leftValue) || IsNull(rightValue) ? null : !context.IsSoftEqual(leftValue, rightValue);
            },
            TokenType.GREATER_THAN => row => Compare(context, left(row), right(row), static c => c > 0),
            TokenType.LESS_THAN => row => Compare(context, left(row), right(row), static c => c < 0),
            TokenType.GREATER_EQUALS => row => Compare(context, left(row), right(row), static c => c >= 0),
            TokenType.LESS_EQUALS => row => Compare(context, left(row), right(row), static c => c <= 0),
            _ => static _ => null
        };

        return binary.Operator is TokenType.AND or TokenType.OR
            or TokenType.PLUS or TokenType.MINUS or TokenType.STAR or TokenType.SLASH or TokenType.MODULO or TokenType.LSHIFT or TokenType.RSHIFT
            or TokenType.EQUALS or TokenType.NOT_EQUALS or TokenType.GREATER_THAN or TokenType.LESS_THAN or TokenType.GREATER_EQUALS or TokenType.LESS_EQUALS;
    }

    private static object? Compare(IExecutionContext context, object? left, object? right, Func<int, bool> predicate)
        => IsNull(left) || IsNull(right) ? null : predicate(context.CompareConstants(left, right));

    private static bool ToPredicate(object? value)
    {
        if (value == null || value == DBNull.Value) return false;
        if (value is bool b) return b;
        try { return Convert.ToBoolean(value); } catch { return false; }
    }

    private static bool IsNull(object? value) => value == null || value == DBNull.Value;

    private static object? ResolveIdentifier(string name, Row row)
    {
        if (row.TryGetValue(name, out var value)) return value;

        var names = row.GetColumnNames();
        var nameList = names as IReadOnlyList<string> ?? new List<string>(names);
        var match = FindMatch(name, nameList);
        if (match.IsAmbiguous)
            throw new ExecutionException($"Ambiguous identifier '{name}'. Matches: {string.Join(", ", match.Candidates)}");
        return match.ResolvedKey != null ? row[match.ResolvedKey] : null;
    }

    private readonly struct MatchResult
    {
        public string? ResolvedKey { get; init; }
        public bool IsAmbiguous { get; init; }
        public IReadOnlyList<string> Candidates { get; init; }

        public static MatchResult NoMatch => new() { Candidates = Array.Empty<string>() };
        public static MatchResult Ambiguous(IReadOnlyList<string> candidates)
            => new() { IsAmbiguous = true, Candidates = candidates };
        public static MatchResult Resolved(string key)
            => new() { ResolvedKey = key, Candidates = Array.Empty<string>() };
    }

    private static MatchResult FindMatch(string name, IReadOnlyList<string> allNames)
    {
        if (string.IsNullOrEmpty(name)) return MatchResult.NoMatch;

        var baseName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        var qualifier = name.Contains('.') ? name[..name.LastIndexOf('.')] : null;
        var suffix = "." + baseName;
        var strongMatches = new List<string>();
        var weakMatches = new List<string>();

        Dictionary<string, List<string>>? qualifiedByBase = null;
        if (qualifier != null)
        {
            qualifiedByBase = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in allNames)
            {
                if (!key.Contains('.')) continue;
                var keyBase = key[(key.LastIndexOf('.') + 1)..];
                if (!qualifiedByBase.TryGetValue(keyBase, out var list))
                    qualifiedByBase[keyBase] = list = new List<string>();
                list.Add(key);
            }
        }

        foreach (var key in allNames)
        {
            if (!key.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                && !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (qualifier != null)
            {
                if (key.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase))
                {
                    strongMatches.Add(key);
                }
                else if (key.Contains('.') && qualifier.Contains('.'))
                {
                    var keyQualifier = key[..key.LastIndexOf('.')];
                    if (qualifier.EndsWith("." + keyQualifier, StringComparison.OrdinalIgnoreCase))
                        strongMatches.Add(key);
                }
                else if (!key.Contains('.'))
                {
                    var belongsToAnother = false;
                    if (qualifiedByBase!.TryGetValue(key, out var qualifiedKeys))
                    {
                        foreach (var qualifiedKey in qualifiedKeys)
                        {
                            if (!qualifiedKey.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase))
                            {
                                belongsToAnother = true;
                                break;
                            }
                        }
                    }
                    if (!belongsToAnother)
                        weakMatches.Add(key);
                }
            }
            else
            {
                if (!key.Contains('.')) strongMatches.Add(key);
                else weakMatches.Add(key);
            }
        }

        var finalMatches = strongMatches.Count > 0 ? strongMatches : weakMatches;
        return finalMatches.Count switch
        {
            > 1 => MatchResult.Ambiguous(finalMatches),
            1 => MatchResult.Resolved(finalMatches[0]),
            _ => MatchResult.NoMatch
        };
    }
}
