using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine;
/// <summary>
/// Responsible for evaluating SQL expressions (literals, identifiers, binary ops, functions) against a row context.
/// </summary>
public class ExpressionEvaluator
{
    private static readonly TableSchema _scalarSchema = new TableSchema(new[] { "Value" });
    private static readonly Regex _interpolationRegex = new(@"\$\{(@?[a-zA-Z_][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);
    private readonly IExecutionContext _context;
    private readonly ILogger _logger;
    private static readonly ConcurrentDictionary<(Type, string), MemberInfo?> _reflectionCache = new();
    private readonly ConcurrentDictionary<Statement, List<string>> _outerRefCache = new();
    private readonly ConcurrentDictionary<(TableSchema?, string), string?> _identifierCache = new();
    private readonly ConcurrentDictionary<FunctionCallExpression, FunctionLookupKeys> _functionKeyCache = new();

    private readonly record struct FunctionLookupKeys(string WindowKey, string AggregateKey);

    public void ClearCaches()
    {
        _outerRefCache.Clear();
        _identifierCache.Clear();
        _functionKeyCache.Clear();
    }

    public ExpressionEvaluator(IExecutionContext context)
    {
        _context = context;
        _logger = context.Logger;
    }

    private bool IsResolvableInOuterScope(string name, Row? context = null)
    {
        if (name.StartsWith("@")) return _context.VarContext.ContainsVariable(name);

        if (context != null)
        {
            if (context.HasColumn(name)) return true;
            if (ResolveIdentifierFallback(name, context) != null) return true;
        }

        foreach (var outer in _context.OuterRowStack)
        {
            if (outer != null)
            {
                if (outer.HasColumn(name)) return true;
                if (ResolveIdentifierFallback(name, outer) != null) return true;
            }
        }
        return false;
    }

    private bool TryResolveIdentifier(string name, Row? context, out object? value)
    {
        if (name.StartsWith("@"))
        {
            if (_context.VarContext.ContainsVariable(name))
            {
                value = _context.VarContext.GetVariable(name);
                return true;
            }
            value = null;
            return false;
        }

        // 1. Check immediate context (with ambiguity check)
        if (context != null)
        {
            if (context.TryGetValue(name, out value)) return true;
            var fb = ResolveIdentifierFallback(name, context);
            if (fb != null)
            {
                value = fb;
                return true;
            }
        }

        // 2. Check outer scopes (exact match)
        foreach (var outer in _context.OuterRowStack)
        {
            if (outer != null && outer.TryGetValue(name, out value))
            {
                return true;
            }
        }

        // 3. Fallback: search for column in outer scopes
        foreach (var outer in _context.OuterRowStack)
        {
            if (outer != null)
            {
                var fb = ResolveIdentifierFallback(name, outer);
                if (fb != null)
                {
                    value = fb;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private object? ResolveIdentifier(string name, Row? context)
    {
        TryResolveIdentifier(name, context, out var value);
        return value;
    }

    /// <summary>
    /// Provides fallback resolution for identifiers (e.g., matching 'ID' if 'T.ID' exists in the row).
    /// </summary>
    private object? ResolveIdentifierFallback(string name, Row context)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (context.HasColumn(name)) return context[name];

        var schema = context.Schema;

        // Schema-less rows have no stable identity — skip the cache to prevent stale cross-step
        // hits where (null, "col") resolves a key from a previous dynamic row shape.
        if (schema == null)
        {
            var colNames = context.GetColumnNames();
            var allNamesUncached = colNames as IReadOnlyList<string> ?? colNames.ToList();
            var matchUncached = ColumnMatcher.FindMatch(name, allNamesUncached);
            if (matchUncached.IsAmbiguous)
                throw new ExecutionException($"Ambiguous identifier '{name}'. Matches: {string.Join(", ", matchUncached.Candidates)}");
            return matchUncached.ResolvedKey != null ? context[matchUncached.ResolvedKey] : null;
        }

        var cacheKey = (schema, name);
        if (_identifierCache.TryGetValue(cacheKey, out var resolvedKey))
        {
            return resolvedKey != null ? context[resolvedKey] : null;
        }

        var allNames = context.GetColumnNames();
        var nameList = allNames as IReadOnlyList<string> ?? allNames.ToList();
        var match = ColumnMatcher.FindMatch(name, nameList);

        if (match.IsAmbiguous)
            throw new ExecutionException($"Ambiguous identifier '{name}'. Matches: {string.Join(", ", match.Candidates)}");

        _identifierCache[cacheKey] = match.ResolvedKey;
        return match.ResolvedKey != null ? context[match.ResolvedKey] : null;
    }

    /// <summary>
    /// Resolves qualified and unqualified column name references against a set of row column names.
    /// Classifies candidates as strong (exact qualifier match) or weak (unqualified fallback),
    /// and detects cross-qualifier ambiguity.
    /// </summary>
    private static class ColumnMatcher
    {
        public readonly struct MatchResult
        {
            public string? ResolvedKey { get; init; }
            public bool IsAmbiguous { get; init; }
            public IReadOnlyList<string> Candidates { get; init; }

            public static MatchResult NoMatch => new() { Candidates = Array.Empty<string>() };
            public static MatchResult Ambiguous(IReadOnlyList<string> c)
                => new() { IsAmbiguous = true, Candidates = c };
            public static MatchResult Resolved(string key)
                => new() { ResolvedKey = key, Candidates = Array.Empty<string>() };
        }

        public static MatchResult FindMatch(string name, IReadOnlyList<string> allNames)
        {
            var baseName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
            var qualifier = name.Contains('.') ? name[..name.LastIndexOf('.')] : null;
            var suffix = "." + baseName;

            var strongMatches = new List<string>();
            var weakMatches = new List<string>();

            // When a qualifier is present, build an index from baseName → qualified keys so the
            // "belongs to another qualifier" check is O(1) per candidate instead of O(N).
            Dictionary<string, List<string>>? qualifiedByBase = null;
            if (qualifier != null)
            {
                qualifiedByBase = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in allNames)
                {
                    if (!k.Contains('.')) continue;
                    var kBase = k[(k.LastIndexOf('.') + 1)..];
                    if (!qualifiedByBase.TryGetValue(kBase, out var list))
                        qualifiedByBase[kBase] = list = new List<string>();
                    list.Add(k);
                }
            }

            foreach (var k in allNames)
            {
                if (!k.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                    && !k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (qualifier != null)
                {
                    if (k.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        strongMatches.Add(k);
                    }
                    else if (k.Contains('.') && qualifier.Contains('.'))
                    {
                        // Three-part name: m.FILE.id → FILE.id — the column qualifier ("FILE") is a
                        // trailing segment of the requested qualifier ("m.FILE"). Match as strong.
                        var kQualifier = k[..k.LastIndexOf('.')];
                        if (qualifier.EndsWith("." + kQualifier, StringComparison.OrdinalIgnoreCase))
                            strongMatches.Add(k);
                    }
                    else if (!k.Contains('.'))
                    {
                        // Include an unqualified column only if no other qualifier owns it.
                        bool belongsToAnother = false;
                        if (qualifiedByBase!.TryGetValue(k, out var qualifiedKeysForK))
                        {
                            foreach (var qk in qualifiedKeysForK)
                            {
                                if (!qk.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase))
                                {
                                    belongsToAnother = true;
                                    break;
                                }
                            }
                        }
                        if (!belongsToAnother)
                            weakMatches.Add(k);
                    }
                }
                else
                {
                    if (!k.Contains('.'))
                        strongMatches.Add(k); // Exact unqualified match
                    else
                        weakMatches.Add(k);   // Weak: bare "ID" matches qualified "#A.ID"
                }
            }

            var finalMatches = strongMatches.Count > 0 ? strongMatches : weakMatches;

            if (finalMatches.Count > 1) return MatchResult.Ambiguous(finalMatches);
            if (finalMatches.Count == 1) return MatchResult.Resolved(finalMatches[0]);
            return MatchResult.NoMatch;
        }
    }

    /// <summary>Evaluates an expression against a row context.</summary>
    public ValueTask<object?> Evaluate(Expression? expr, Row context, bool decryptSensitive = false)
    {
        return EvaluateInternal(expr, context, decryptSensitive);
    }

    /// <summary>Evaluates an expression as an asynchronous stream of rows.</summary>
    public async IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context)
    {
        if (expr == null) yield break;

        if (expr is SubqueryExpression subq)
        {
            _context.OuterRowStack.Push(context);
            await foreach (var batch in _context.ExecuteQuery(subq.Query))
            {
                foreach (var row in batch.Rows) yield return row;
            }
            _context.OuterRowStack.Pop();
            yield break;
        }

        if (expr is VariableExpression v)
        {
            var val = EvaluateVariable(v);
            if (val is DataTable dt)
            {
                foreach (var row in dt.Rows) yield return row;
            }
            else if (val is System.Collections.IEnumerable list && val is not string)
            {
                foreach (var item in list)
                {
                    if (item is Row r) yield return r;
                    else if (item is DataTable dtItem) foreach (var dtr in dtItem.Rows) yield return dtr;
                    else yield return new Row(_scalarSchema, new[] { item });
                }
            }
            else if (val != null)
            {
                yield return new Row(_scalarSchema, new[] { val });
            }
            yield break;
        }

        if (expr is IdentifierExpression id)
        {
            if (_context.Connections.ContainsKey(id.Name))
            {
                var sql = new SelectStatement(
                    new List<SelectColumn> { new SelectColumn(new IdentifierExpression("*")) },
                    null,
                    new TableReference(id.Name),
                    new List<JoinClause>(),
                    null
                );

                await foreach (var batch in _context.ExecuteQuery(sql))
                {
                    foreach (var row in batch.Rows) yield return row;
                }
                yield break;
            }
        }

        var singleVal = await EvaluateInternal(expr, context);
        if (singleVal is DataTable dt2)
        {
            foreach (var row in dt2.Rows) yield return row;
        }
        else if (singleVal is System.Collections.IEnumerable list2 && singleVal is not string)
        {
            foreach (var item in list2)
            {
                if (item is Row r) yield return r;
                else if (item is DataTable dtItem) foreach (var dtr in dtItem.Rows) yield return dtr;
                else yield return new Row(_scalarSchema, new[] { item });
            }
        }
        else if (singleVal != null)
        {
            yield return new Row(_scalarSchema, new[] { singleVal });
        }
    }

    /// <summary>Internal recursive entry point for expression evaluation.</summary>
    public ValueTask<object?> EvaluateInternal(Expression? expr, Row context, bool decryptSensitive = false)
    {
        if (expr == null) return default;

        // Fast paths to avoid async state machine for primitives
        if (expr is LiteralExpression lit)
        {
            if (lit.Value is string s)
            {
                if (s.StartsWith("__HEX_BLOB__", StringComparison.Ordinal))
                {
                    return new ValueTask<object?>(ParseHex(s.Substring(12)));
                }
                var val = (decryptSensitive && s.StartsWith("ENC:")) ? _context.DecryptValue(s) : s;
                if (val is string strVal && strVal.Contains("${"))
                {
                    val = InterpolateStringVariables(strVal);
                }
                return new ValueTask<object?>(val);
            }
            return new ValueTask<object?>(lit.Value);
        }

        if (expr is IdentifierExpression id)
            return EvaluateIdentifier(id, context);

        if (expr is VariableExpression v)
            return new ValueTask<object?>(EvaluateVariable(v, decryptSensitive));

        if (expr is ParameterExpression p)
            return new ValueTask<object?>(EvaluateParameter(p));

        return EvaluateInternalAsync(expr, context, decryptSensitive);
    }

    private async ValueTask<object?> EvaluateInternalAsync(Expression expr, Row context, bool decryptSensitive)
    {
        return expr switch
        {
            MemberAccessExpression ma => await EvaluateMemberAccess(ma, context, decryptSensitive),
            BinaryExpression bin => await EvaluateBinary(bin, context, decryptSensitive),
            LikeExpression like => await EvaluateLikeExpr(like, context, decryptSensitive),
            IsNullExpression isNull => await EvaluateIsNull(isNull, context, decryptSensitive),
            IsDistinctFromExpression idf => await EvaluateIsDistinctFrom(idf, context, decryptSensitive),
            CaseExpression c => await EvaluateCase(c, context, decryptSensitive),
            InExpression inExp => await EvaluateIn(inExp, context, decryptSensitive),
            BetweenExpression bet => await EvaluateBetween(bet, context, decryptSensitive),
            ExistsExpression ex => await EvaluateExists(ex, context),
            ListExpression listExpr => await EvaluateList(listExpr, context, decryptSensitive),
            AtTimeZoneExpression atTz => await EvaluateAtTimeZone(atTz, context, decryptSensitive),
            SubstringExpression sub => await EvaluateSubstring(sub, context, decryptSensitive),
            PositionExpression pos => await EvaluatePosition(pos, context, decryptSensitive),
            ExtractExpression ext => await EvaluateExtract(ext, context, decryptSensitive),
            OverlayExpression ovl => await EvaluateOverlay(ovl, context, decryptSensitive),
            TrimExpression trim => await EvaluateTrim(trim, context, decryptSensitive),
            FunctionCallExpression f => await EvaluateFunction(f, context, decryptSensitive),
            SubqueryExpression subq => await EvaluateSubquery(subq, context),
            UnaryExpression un => await EvaluateUnary(un, context, decryptSensitive),
            _ => null
        };
    }

    private async ValueTask<object?> EvaluateUnary(UnaryExpression un, Row context, bool decryptSensitive)
    {
        if (un.Operator == TokenType.NOT)
        {
            var inner = await EvaluateInternal(un.Expression, context, decryptSensitive);
            if (inner == null || inner == DBNull.Value) return null;
            if (inner is bool b) return (object?)!b;
            try { return (object?)!Convert.ToBoolean(inner); } catch { return null; }
        }
        if (un.Operator == TokenType.MINUS)
        {
            var inner = await EvaluateInternal(un.Expression, context, decryptSensitive);
            if (inner == null || inner == DBNull.Value) return null;
            try
            {
                decimal d = Convert.ToDecimal(inner, System.Globalization.CultureInfo.InvariantCulture);
                return -d;
            }
            catch
            {
                return null;
            }
        }
        if (un.Operator == TokenType.PLUS)
        {
            var inner = await EvaluateInternal(un.Expression, context, decryptSensitive);
            if (inner == null || inner == DBNull.Value) return null;
            try
            {
                return Convert.ToDecimal(inner, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return inner;
            }
        }
        return null;
    }

    /// <summary>Evaluates an IN expression (list or subquery).</summary>
    private async ValueTask<object?> EvaluateIn(InExpression inExp, Row context, bool decryptSensitive = false)
    {
        var l = await EvaluateInternal(inExp.Left, context, decryptSensitive);
        bool hasElements = false;
        bool found = false;
        bool hasNullRight = false;

        if (inExp.Right is SubqueryExpression subq)
        {
            // Use cached stream evaluation for subqueries in IN clauses
            await foreach (var rowVal in EvaluateStreamSubquery(subq, context))
            {
                hasElements = true;
                if (rowVal.IsNull())
                {
                    hasNullRight = true;
                }
                else if (!l.IsNull())
                {
                    if (IsSoftEqual(l, rowVal)) { found = true; break; }
                }
            }
        }
        else if (inExp.Right is ListExpression list)
        {
            foreach (var item in list.Items)
            {
                hasElements = true;
                var itemVal = await EvaluateInternal(item, context, decryptSensitive);
                if (itemVal.IsNull())
                {
                    hasNullRight = true;
                }
                else if (!l.IsNull())
                {
                    if (IsSoftEqual(l, itemVal)) { found = true; break; }
                }
            }
        }
        else
        {
            var rightVal = await EvaluateInternal(inExp.Right, context, decryptSensitive);
            if (rightVal is System.Collections.IEnumerable listVal && !(rightVal is string))
            {
                foreach (var item in listVal)
                {
                    hasElements = true;
                    if (item.IsNull())
                    {
                        hasNullRight = true;
                    }
                    else if (!l.IsNull())
                    {
                        if (IsSoftEqual(l, item)) { found = true; break; }
                    }
                }
            }
            else
            {
                hasElements = true;
                if (rightVal.IsNull())
                {
                    hasNullRight = true;
                }
                else if (!l.IsNull())
                {
                    found = IsSoftEqual(l, rightVal);
                }
            }
        }

        if (!hasElements)
        {
            return inExp.IsNot ? true : false;
        }

        if (found)
        {
            return inExp.IsNot ? false : true;
        }

        if (l.IsNull() || hasNullRight)
        {
            return null;
        }

        return inExp.IsNot ? true : false;
    }

    private async ValueTask<object?> EvaluateBetween(BetweenExpression bet, Row context, bool decryptSensitive = false)
    {
        var val = await EvaluateInternal(bet.Left, context, decryptSensitive);
        if (val.IsNull()) return null;

        var start = await EvaluateInternal(bet.Start, context, decryptSensitive);
        var end = await EvaluateInternal(bet.End, context, decryptSensitive);

        // Three-valued logic for: val >= start AND val <= end
        bool? leftCond = null;
        if (!start.IsNull())
        {
            leftCond = CompareConstants(val, start) >= 0;
        }

        bool? rightCond = null;
        if (!end.IsNull())
        {
            rightCond = CompareConstants(val, end) <= 0;
        }

        // Evaluate leftCond AND rightCond in three-valued logic
        bool? isBetween;
        if (leftCond == false || rightCond == false)
        {
            isBetween = false;
        }
        else if (leftCond == null || rightCond == null)
        {
            isBetween = null;
        }
        else
        {
            isBetween = true;
        }

        if (isBetween == null) return null;

        bool finalResult = isBetween.Value;
        return bet.IsNot ? !finalResult : finalResult;
    }


    /// <summary>Evaluates an EXISTS clause.</summary>
    private async ValueTask<object?> EvaluateExists(ExistsExpression ex, Row context)
    {
        if (ex.Subquery is SelectStatement select)
        {
            bool found = await EvaluateExistsSubquery(select, context);
            return ex.IsNot ? !found : found;
        }
        throw new ExecutionException("EXISTS subquery must be a SELECT statement.");
    }

    /// <summary>Evaluates a list of expressions.</summary>
    private async ValueTask<object?> EvaluateList(ListExpression listExpr, Row context, bool decryptSensitive = false)
    {
        var result = new List<object?>();
        foreach (var item in listExpr.Items) result.Add(await EvaluateInternal(item, context, decryptSensitive));
        return result;
    }

    /// <summary>Evaluates a scalar or aggregate function call.</summary>
    public async ValueTask<object?> EvaluateFunction(FunctionCallExpression f, Row context, bool decryptSensitive = false)
    {
        if (f.Window != null)
        {
            var keys = GetFunctionLookupKeys(f);
            return context.HasColumn(keys.WindowKey) ? context[keys.WindowKey] : null;
        }

        // Check for pre-calculated aggregate results (used in HAVING clause)
        var aggregateKey = GetFunctionLookupKeys(f).AggregateKey;
        if (context != null && context.TryGetValue(aggregateKey, out var aggVal)) return aggVal;

        var fn = f.FunctionName.ToUpperInvariant();

        if (fn == "PREV")
        {
            if (context is MatchRecognizeRow mrRow)
            {
                int offset = 1;
                if (f.Arguments.Count > 1)
                {
                    var offsetVal = await EvaluateInternal(f.Arguments[1], context, decryptSensitive);
                    offset = Convert.ToInt32(offsetVal);
                }
                int prevIndex = mrRow.Index - offset;
                if (prevIndex >= 0 && prevIndex < mrRow.Rows.Count)
                {
                    var prefixedPrevRow = new MatchRecognizeRow(mrRow.Rows, prevIndex, mrRow.Variable);
                    return await EvaluateInternal(f.Arguments[0], prefixedPrevRow, decryptSensitive);
                }
                return null;
            }
            return null;
        }

        // ANSI String length aliases
        if (fn == "CHARACTER_LENGTH" || fn == "CHAR_LENGTH" || fn == "OCTET_LENGTH")
        {
            var val = await EvaluateInternal(f.Arguments.FirstOrDefault(), context ?? Row.Empty, decryptSensitive);
            if (val == null) return null;
            var s = val.ToString() ?? "";
            return fn == "OCTET_LENGTH" ? System.Text.Encoding.UTF8.GetByteCount(s) : s.Length;
        }

        if (fn == "SYSDATE" || fn == "GETDATE" || fn == "CURRENT_TIMESTAMP") return DateTime.Now;
        if (fn == "CURRENT_DATE") return DateTime.Today;
        if (fn == "CURRENT_TIME") return DateTime.Now.TimeOfDay;

        var args = new List<object?>(f.Arguments.Count);
        for (int i = 0; i < f.Arguments.Count; i++)
        {
            var arg = f.Arguments[i];
            if (i == 0 && (fn == "DATEPART" || fn == "DATEDIFF" || fn == "DATENAME" || fn == "DATEADD" || fn == "DATE_TRUNC" || fn == "DATE_PART") && arg is IdentifierExpression idArg)
            {
                args.Add(idArg.Name);
            }
            else
            {
                args.Add(await EvaluateInternal(arg, context ?? Row.Empty, decryptSensitive));
            }
        }

        if (_context.FunctionRegistry.IsRegistered(fn))
        {
            return await _context.FunctionRegistry.ExecuteAsync(fn, args, _context);
        }

        // Safeguard: Do not route recognized aggregates to ProcedureExecutor
        var aggregateEngine = new Engines.AggregateEngine(_context, _context.Logger);
        if (aggregateEngine.IsAggregate(f))
        {
            throw new ExecutionException($"Aggregate function '{fn}' could not be resolved. Ensure it is used in a SELECT clause with grouping, or check if the query should have been pushed down.");
        }

        return await _context.EvaluateUserDefinedFunction(f, args, context ?? Row.Empty);
    }

    private FunctionLookupKeys GetFunctionLookupKeys(FunctionCallExpression f)
    {
        return _functionKeyCache.GetOrAdd(f, static expr =>
        {
            var normalized = expr.ToSql().ToUpperInvariant();
            return new FunctionLookupKeys($"WINDOW_{normalized}", $"AGG_{normalized}");
        });
    }

    private async ValueTask<object?> EvaluateAtTimeZone(AtTimeZoneExpression atTz, Row context, bool decryptSensitive = false)
    {
        var val = await EvaluateInternal(atTz.Left, context, decryptSensitive);
        var zone = await EvaluateInternal(atTz.TimeZone, context, decryptSensitive);
        if (val == null || zone == null) return val;

        DateTimeOffset dto;
        if (val is DateTimeOffset valDto)
        {
            dto = valDto;
        }
        else if (val is DateTime valDt)
        {
            var dt = valDt;
            if (dt.Kind == DateTimeKind.Unspecified)
            {
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            dto = new DateTimeOffset(dt);
        }
        else if (val is string text && DateTime.TryParse(
                     text,
                     System.Globalization.CultureInfo.InvariantCulture,
                     System.Globalization.DateTimeStyles.AssumeUniversal
                     | System.Globalization.DateTimeStyles.AdjustToUniversal,
                     out var parsedUtc))
        {
            dto = new DateTimeOffset(DateTime.SpecifyKind(parsedUtc, DateTimeKind.Utc));
        }
        else if (EvaluationUtils.TryToDateTimeOffset(val, out var parsedDto))
        {
            dto = parsedDto;
        }
        else
        {
            return val;
        }

        try
        {
            var tzInfo = RelDateResolver.FindTimeZone(zone.ToString() ?? "UTC");
            return TimeZoneInfo.ConvertTime(dto, tzInfo);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ExecutionException($"Unknown time zone '{zone}'.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ExecutionException($"Invalid time zone configuration for '{zone}'.", ex);
        }
    }

    /// <summary>Evaluates a scalar subquery.</summary>
    private async ValueTask<object?> EvaluateSubquery(SubqueryExpression subq, Row context)
    {
        object? result = null;
        var query = (SelectStatement)subq.Query;

        if (!_outerRefCache.TryGetValue(query, out var outerRefs))
        {
            var analyzer = new SubqueryAnalyzer();
            outerRefs = analyzer.GetOuterReferences(query);
            _outerRefCache[query] = outerRefs;
        }

        var captureValues = new List<object?>(outerRefs.Count);
        foreach (var or in outerRefs)
        {
            // Only capture if it's actually an outer reference (resolvable in outer scope)
            if (IsResolvableInOuterScope(or, context))
            {
                captureValues.Add(ResolveIdentifier(or, context));
            }
        }

        var cacheKey = new SubqueryCacheKey(query, new CompoundKey(captureValues.ToArray()), SubqueryResultType.Scalar);

        if (_context.SubqueryCache.TryGetValue(cacheKey, out var cachedResult))
        {
            _context.Telemetry.SubqueryCacheHits++;
            return cachedResult!.ScalarValue;
        }

        _context.Telemetry.SubqueryCacheMisses++;

        _context.OuterRowStack.Push(context);
        try
        {
            await foreach (var batch in _context.ExecuteQuery(subq.Query))
            {
                if (batch.Rows.Count > 0 && batch.ColumnNames.Count > 0)
                {
                    result = batch.Rows[0][0];
                }
                break;
            }
        }
        finally
        {
            _context.OuterRowStack.Pop();
        }

        await _context.SubqueryCache.SetAsync(cacheKey, new SubqueryResult(result));
        return result;
    }

    private async IAsyncEnumerable<object?> EvaluateStreamSubquery(SubqueryExpression subq, Row context)
    {
        var query = (SelectStatement)subq.Query;
        if (!_outerRefCache.TryGetValue(query, out var outerRefs))
        {
            var analyzer = new SubqueryAnalyzer();
            outerRefs = analyzer.GetOuterReferences(query);
            _outerRefCache[query] = outerRefs;
        }

        var captureValues = new List<object?>();
        foreach (var or in outerRefs)
        {
            if (IsResolvableInOuterScope(or, context))
            {
                captureValues.Add(ResolveIdentifier(or, context));
            }
        }

        var cacheKey = new SubqueryCacheKey(query, new CompoundKey(captureValues.ToArray()), SubqueryResultType.Stream);

        if (_context.SubqueryCache.TryGetValue(cacheKey, out var cachedResult))
        {
            _context.Telemetry.SubqueryCacheHits++;
            if (cachedResult!.InSet != null)
            {
                foreach (var val in cachedResult.InSet) yield return val;
            }
            else if (cachedResult.StreamData != null)
            {
                await foreach (var batch in cachedResult.StreamData.ReadBatches())
                {
                    foreach (var row in batch.Rows) yield return row[0];
                }
            }
            yield break;
        }

        _context.Telemetry.SubqueryCacheMisses++;

        // Materialize or spill fully before yielding so the cache is populated.
        // IN only needs the first projected value, so spilled cache rows use a
        // compact single-column schema instead of copying the full subquery row.
        long rowCount = 0;
        var inSet = new HashSet<object?>(CanonicalEqualityComparer.Instance);
        InMemoryDataSource? spillStore = null;
        DataTable? spillBatch = null;
        var spillSchema = new TableSchema(new[] { "Value" });

        async Task AddSpillValueAsync(object? value)
        {
            if (spillStore == null)
                throw new InvalidOperationException("Subquery spill store has not been initialized.");

            spillBatch ??= new DataTable { Schema = spillSchema };
            await spillBatch.AddRowAsync(new Row(spillSchema, new[] { value }));

            if (spillBatch.Rows.Count >= _context.EffectiveBatchSize)
            {
                await spillStore.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { spillBatch }), append: true);
                spillBatch = null;
            }
        }

        async Task FlushSpillBatchAsync()
        {
            if (spillStore != null && spillBatch != null && spillBatch.Rows.Count > 0)
            {
                await spillStore.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { spillBatch }), append: true);
                spillBatch = null;
            }
        }

        _context.OuterRowStack.Push(context);
        try
        {
            await foreach (var batch in _context.ExecuteQuery(subq.Query))
            {
                foreach (var row in batch.Rows)
                {
                    rowCount++;
                    var val = row.Schema?.ColumnCount > 0 ? row[0] : null;

                    if (spillStore != null)
                    {
                        await AddSpillValueAsync(val);
                    }
                    else if (rowCount > _context.SubquerySpillThresholdRows)
                    {
                        spillStore = new InMemoryDataSource();
                        spillStore.SetSchema(new[] { new ColumnDefinition("Value", "VARIANT", false) });

                        foreach (var existing in inSet!) await AddSpillValueAsync(existing);
                        await AddSpillValueAsync(val);
                        inSet = null;
                    }
                    else
                    {
                        inSet!.Add(val);
                    }
                }
            }
        }
        finally
        {
            _context.OuterRowStack.Pop();
        }

        // Cache is now guaranteed to be populated
        SubqueryResult finalResult;
        if (spillStore != null)
        {
            await FlushSpillBatchAsync();
            finalResult = new SubqueryResult(spillStore);
            await _context.SubqueryCache.SetAsync(cacheKey, finalResult);
            await foreach (var batch in spillStore.ReadBatches())
            {
                foreach (var row in batch.Rows) yield return row[0];
            }
        }
        else
        {
            finalResult = new SubqueryResult(inSet ?? new HashSet<object?>());
            await _context.SubqueryCache.SetAsync(cacheKey, finalResult);
            foreach (var val in finalResult.InSet!) yield return val;
        }
    }

    private async Task<bool> EvaluateExistsSubquery(SelectStatement query, Row context)
    {
        if (!_outerRefCache.TryGetValue(query, out var outerRefs))
        {
            var analyzer = new SubqueryAnalyzer();
            outerRefs = analyzer.GetOuterReferences(query);
            _outerRefCache[query] = outerRefs;
        }

        var captureValues = new List<object?>();
        foreach (var or in outerRefs)
        {
            if (IsResolvableInOuterScope(or, context))
            {
                captureValues.Add(ResolveIdentifier(or, context));
            }
        }

        var cacheKey = new SubqueryCacheKey(query, new CompoundKey(captureValues.ToArray()), SubqueryResultType.Exists);

        if (_context.SubqueryCache.TryGetValue(cacheKey, out var cachedResult))
        {
            _context.Telemetry.SubqueryCacheHits++;
            return (cachedResult!.ScalarValue is bool b && b);
        }

        _context.Telemetry.SubqueryCacheMisses++;
        bool found = false;

        _context.OuterRowStack.Push(context);
        try
        {
            await foreach (var batch in _context.ExecuteQuery(query))
            {
                if (batch.Rows.Count > 0)
                {
                    found = true;
                    break;
                }
            }
        }
        finally
        {
            _context.OuterRowStack.Pop();
        }

        await _context.SubqueryCache.SetAsync(cacheKey, new SubqueryResult(found));
        return found;
    }

    /// <summary>Checks for soft equality between two objects.</summary>
    public bool IsSoftEqual(object? a, object? b) => EvaluationUtils.IsSoftEqual(a, b, _logger, _context.CaseSensitiveComparison);

    /// <summary>Compares two values for ordering.</summary>
    public int CompareConstants(object? a, object? b) => EvaluationUtils.CompareConstants(a, b, _context.CaseSensitiveComparison);

    /// <summary>Performs mathematical operations between two objects.</summary>
    public object? MathOp(object? a, object? b, TokenType op) => EvaluationUtils.MathOp(a, b, op switch { TokenType.PLUS => "+", TokenType.MINUS => "-", TokenType.STAR => "*", TokenType.SLASH => "/", TokenType.MODULO => "%", _ => "" });

    /// <summary>Evaluates a LIKE pattern match.</summary>
    public bool EvaluateLike(object? input, object? pattern, string? escapeChar = null) => EvaluationUtils.EvaluateLike(input, pattern, escapeChar);

    /// <summary>Casts a value to a specific data type.</summary>
    public object? CastToType(object? value, string type) => EvaluationUtils.CastToType(value, type);

    /// <summary>Evaluates a variable reference (@var or #temp).</summary>
    private object? EvaluateVariable(VariableExpression v, bool decryptSensitive = false)
    {
        if (v.Name.Equals("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)) return _context.TranCount;
        if (v.Name.Equals("@@RESULTSETS", StringComparison.OrdinalIgnoreCase)) return _context.LastResultSets;
        if (v.Name.Equals("@@VERSION", StringComparison.OrdinalIgnoreCase)) return LanguageMetadata.GetFullVersionString();
        if (v.Name.Equals("@@ROWCOUNT", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.LastStatementRowsProcessed;
        if (v.Name.Equals("@@ERROR", StringComparison.OrdinalIgnoreCase)) return _context.PreviousErrorNumber;
        if (v.Name.Equals("@@TOTAL_SPILLED_BYTES", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.TotalSpilledBytes;
        if (v.Name.Equals("@@PARTITIONS_COUNT", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.PartitionsCount;
        if (v.Name.Equals("@@AGGREGATE_GROUPS_COUNT", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.AggregateGroupsCount;
        if (v.Name.Equals("@@AGGREGATE_EXPANSION_RATIO", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.AggregateExpansionRatio;
        if (v.Name.Equals("@@LAST_EXEC_MS", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.LastExecutionTimeMs;
        if (v.Name.Equals("@@PEAK_MEMORY_MB", StringComparison.OrdinalIgnoreCase)) return Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
        if (v.Name.Equals("@@SUBQUERY_CACHE_HITS", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.SubqueryCacheHits;
        if (v.Name.Equals("@@SUBQUERY_CACHE_MISSES", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.SubqueryCacheMisses;
        if (v.Name.Equals("@@SORT_SPILLS", StringComparison.OrdinalIgnoreCase)) return (long)_context.Telemetry.SortSpillCount;
        if (v.Name.Equals("@@FETCH_STATUS", StringComparison.OrdinalIgnoreCase)) return _context.Telemetry.FetchStatus;

        // Identity variables (row-level security). Null when no identity is injected — a valid
        // fail-closed value, so these must resolve here and never fall through to "Undeclared".
        if (v.Name.Equals("@@CURRENT_USER", StringComparison.OrdinalIgnoreCase)) return _context.ExecutionIdentity?.EffectiveUser;
        if (v.Name.Equals("@@CURRENT_USER_ID", StringComparison.OrdinalIgnoreCase)) return _context.ExecutionIdentity?.EffectiveUserId;
        if (v.Name.Equals("@@REAL_USER", StringComparison.OrdinalIgnoreCase)) return _context.ExecutionIdentity?.RealUser;
        if (v.Name.Equals("@@IS_ADMIN", StringComparison.OrdinalIgnoreCase)) return _context.ExecutionIdentity?.IsAdmin ?? false;

        if (!_context.VarContext.ContainsVariable(v.Name))
            throw new ExecutionException($"Undeclared: {v.Name}");

        var val = _context.VarContext.GetVariable(v.Name);

        if (val is string reldateExpr &&
            _context.VarContext.VariableMetadata.TryGetValue(v.Name, out var relMeta) &&
            "RELDATE".Equals(relMeta.DataType, StringComparison.OrdinalIgnoreCase))
        {
            return RelDateResolver.ResolveValue(reldateExpr, _context.WeekStartDay);
        }

        if (decryptSensitive && val is string s && s.StartsWith("ENC:"))
        {
            if (_context.VarContext.VariableMetadata.TryGetValue(v.Name, out var meta) && meta.IsSensitive)
            {
                return _context.DecryptValue(s);
            }
        }

        return val;
    }

    /// <summary>Performs inline variable interpolation for string literals containing ${@var} or ${var}.</summary>
    private string InterpolateStringVariables(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("${")) return input;

        return _interpolationRegex.Replace(input, match =>
        {
            var varName = match.Groups[1].Value;
            if (!varName.StartsWith("@", StringComparison.Ordinal))
            {
                varName = "@" + varName;
            }

            if (_context.VarContext.ContainsVariable(varName))
            {
                var val = _context.VarContext.GetVariable(varName);
                if (val != null)
                {
                    if (_context.VarContext.VariableMetadata.TryGetValue(varName, out var meta) && meta.IsSensitive)
                    {
                        if (val is string enc && enc.StartsWith("ENC:", StringComparison.Ordinal))
                        {
                            return _context.DecryptValue(enc) ?? string.Empty;
                        }
                    }
                    return val.ToString() ?? string.Empty;
                }
                return string.Empty;
            }
            return match.Value;
        });
    }

    /// <summary>Evaluates a parameter reference (? or ?n).</summary>
    private object? EvaluateParameter(ParameterExpression p)
    {
        // First check if the index exists in the context's parameters
        if (p.Index.HasValue)
        {
            if (_context.Parameters != null && p.Index.Value <= _context.Parameters.Count)
            {
                return _context.Parameters[p.Index.Value - 1];
            }
            throw new ExecutionException($"Parameter index {p.Index} is out of range. Provided: {_context.Parameters?.Count ?? 0}");
        }

        // For standard '?', it's usually handled by the provider (e.g. MSSQL/Postgres)
        // But if evaluating in engine context, we might want to pop from a queue?
        // For now, we'll throw as it's ambiguous without a specific binding context
        throw new ExecutionException($"Positional parameter '?' is not supported in this evaluation context. Use indexed parameters (?1, ?2) instead.");
    }

    /// <summary>Evaluates a member access expression (e.g., row.Member or object.Property).</summary>
    private async ValueTask<object?> EvaluateMemberAccess(MemberAccessExpression ma, Row context, bool decryptSensitive = false)
    {
        var val = await EvaluateInternal(ma.Expression, context, decryptSensitive);
        if (val == null) return null;

        // Handle Row or IDictionary
        if (val is Row row)
        {
            // Try direct indexer (best for schema-backed columns)
            var r = row[ma.MemberName];
            if (r != null) return r;

            // Fallback: Check if it's explicitly null (HasColumn distinguishes null-stored vs missing)
            if (row.TryGetValue(ma.MemberName, out var dynamicVal)) return dynamicVal;

            return null;
        }
        if (val is IDictionary<string, object?> dict && dict.TryGetValue(ma.MemberName, out var dVal)) return dVal;

        if (val is MinMaxValue mm)
        {
            if (ma.MemberName.Equals("MIN", StringComparison.OrdinalIgnoreCase)) return mm.Min;
            if (ma.MemberName.Equals("MAX", StringComparison.OrdinalIgnoreCase)) return mm.Max;
        }

        // Handle reflection for properties/fields with caching
        var type = val.GetType();
        var member = _reflectionCache.GetOrAdd((type, ma.MemberName), key =>
        {
            var p = key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p != null) return p;
            return (MemberInfo?)key.Item1.GetField(key.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        });

        if (member is PropertyInfo prop) return prop.GetValue(val);
        if (member is FieldInfo field) return field.GetValue(val);

        return null;
    }

    /// <summary>Evaluates an identifier (column name or special variable).</summary>
    private async ValueTask<object?> EvaluateIdentifier(IdentifierExpression id, Row context)
    {
        if (TryResolveIdentifier(id.Name, context, out var val)) return val;

        // Docker Connection Strings
        if (id.Name.Contains(".CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
        {
            var alias = id.Name.Replace(".CONNECTION_STRING", "", StringComparison.OrdinalIgnoreCase);
            var connStr = _context.DockerManager.GetConnectionString(alias);
            if (connStr != null) return connStr;

            // Direct fallback for plain DOCKER.CONNECTION_STRING
            if (alias.Equals("DOCKER", StringComparison.OrdinalIgnoreCase))
                return _context.DockerManager.LastConnectionString;
        }

        // Existing connections
        if (_context.Connections.ContainsKey(id.Name)) return id.Name;

        // Special identifiers
        if (id.Name.Equals("*", StringComparison.OrdinalIgnoreCase)) return "*";

        // Check function registry for no-arg functions (e.g. SYSDATE, NOW, CURRENT_DATE)
        if (_context.FunctionRegistry.IsRegistered(id.Name))
        {
            return await _context.FunctionRegistry.ExecuteAsync(id.Name, new List<object?>(), _context);
        }

        // For date parts (year, month, etc.) and others, return name if Row is null
        if (context == null) return id.Name;

        return null;
    }

    /// <summary>Evaluates a binary operation (arithmetic or logical).</summary>
    private async ValueTask<object?> EvaluateBinary(BinaryExpression bin, Row context, bool decryptSensitive = false)
    {
        if (bin.Operator == TokenType.AND)
        {
            var lVal = await EvaluateInternal(bin.Left, context, decryptSensitive);
            // IF L is FALSE, result is FALSE (Short-circuit)
            if (!lVal.IsNull() && !Convert.ToBoolean(lVal)) return false;

            var rVal = await EvaluateInternal(bin.Right, context, decryptSensitive);
            // IF R is FALSE, result is FALSE
            if (!rVal.IsNull() && !Convert.ToBoolean(rVal)) return false;

            // IF either is NULL, result is NULL (UNKNOWN)
            if (lVal.IsNull() || rVal.IsNull()) return null;

            // Both must be TRUE
            return true;
        }
        if (bin.Operator == TokenType.OR)
        {
            var lVal = await EvaluateInternal(bin.Left, context, decryptSensitive);
            // IF L is TRUE, result is TRUE (Short-circuit)
            if (!lVal.IsNull() && Convert.ToBoolean(lVal)) return true;

            var rVal = await EvaluateInternal(bin.Right, context, decryptSensitive);
            // IF R is TRUE, result is TRUE
            if (!rVal.IsNull() && Convert.ToBoolean(rVal)) return true;

            // IF either is NULL, result is NULL (UNKNOWN)
            if (lVal.IsNull() || rVal.IsNull()) return null;

            // Both must be FALSE
            return false;
        }

        var leftVal = await EvaluateInternal(bin.Left, context, decryptSensitive);
        var rightVal = await EvaluateInternal(bin.Right, context, decryptSensitive);

        // Use the registry for arithmetic and simple logical operators
        var result = BinaryOperatorFactory.Execute(bin.Operator, leftVal, rightVal);
        if (result != null) return result;

        // Arithmetic operators don't fall back to soft equality if null
        if (bin.Operator == TokenType.PLUS || bin.Operator == TokenType.MINUS ||
            bin.Operator == TokenType.STAR || bin.Operator == TokenType.SLASH ||
            bin.Operator == TokenType.MODULO ||
            bin.Operator == TokenType.LSHIFT || bin.Operator == TokenType.RSHIFT) return null;

        return bin.Operator switch
        {
            // SQL 3VL: comparison with NULL operand yields NULL (UNKNOWN), not false.
            TokenType.EQUALS => (leftVal.IsNull() || rightVal.IsNull()) ? null : (object?)(bool)IsSoftEqual(leftVal, rightVal),
            TokenType.NOT_EQUALS => (leftVal.IsNull() || rightVal.IsNull()) ? null : (object?)(bool)!IsSoftEqual(leftVal, rightVal),
            TokenType.GREATER_THAN => (leftVal.IsNull() || rightVal.IsNull()) ? null : (object?)(bool)(CompareConstants(leftVal, rightVal) > 0),
            TokenType.LESS_THAN => (leftVal.IsNull() || rightVal.IsNull()) ? null : (object?)(bool)(CompareConstants(leftVal, rightVal) < 0),
            TokenType.GREATER_EQUALS => (leftVal.IsNull() || rightVal.IsNull()) ? null : (object?)(bool)(CompareConstants(leftVal, rightVal) >= 0),
            TokenType.LESS_EQUALS => (leftVal.IsNull() || rightVal.IsNull()) ? null : (object?)(bool)(CompareConstants(leftVal, rightVal) <= 0),
            TokenType.LIKE => EvaluateLike(leftVal, rightVal),
            TokenType.REGEX_MATCH => EvaluateRegexMatch(leftVal, rightVal, RegexOptions.None),
            TokenType.REGEX_IMATCH => EvaluateRegexMatch(leftVal, rightVal, RegexOptions.IgnoreCase),
            _ => IsSoftEqual(leftVal, rightVal)
        };
    }

    private static object? EvaluateRegexMatch(object? input, object? pattern, RegexOptions options)
    {
        if (input.IsNull() || pattern.IsNull()) return null;
        return Regex.IsMatch(input?.ToString() ?? "", pattern?.ToString() ?? "", options);
    }

    private static byte[] ParseHex(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((GetHexVal(hex[i * 2]) << 4) + GetHexVal(hex[i * 2 + 1]));
        }
        return bytes;
    }

    private static int GetHexVal(char hex)
    {
        int val = (int)hex;
        return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
    }

    /// <summary>Evaluates a LIKE expression.</summary>
    private async ValueTask<object?> EvaluateLikeExpr(LikeExpression like, Row context, bool decryptSensitive = false)
    {
        var l = await EvaluateInternal(like.Left, context, decryptSensitive);
        var r = await EvaluateInternal(like.Pattern, context, decryptSensitive);
        string? escapeStr = null;
        if (like.EscapeChar != null)
        {
            var escVal = await EvaluateInternal(like.EscapeChar, context, decryptSensitive);
            escapeStr = escVal?.ToString();
        }
        bool res = EvaluateLike(l, r, escapeStr);
        return like.IsNot ? !res : res;
    }

    /// <summary>Evaluates an IS NULL or IS NOT NULL expression.</summary>
    private async ValueTask<object?> EvaluateIsNull(IsNullExpression isNull, Row context, bool decryptSensitive = false)
    {
        var val = await EvaluateInternal(isNull.Expression, context, decryptSensitive);
        bool res = val == null || val == DBNull.Value || (val is string s && string.IsNullOrEmpty(s) && _context.VarContext.GetVariable("NULL_AS_EMPTY")?.ToString() == "TRUE");
        return isNull.Not ? !res : res;
    }

    /// <summary>
    /// Evaluates a null-safe comparison. <c>IS DISTINCT FROM</c> returns true when the operands differ
    /// (treating NULL as a value: both NULL = not distinct, exactly one NULL = distinct); <c>IS NOT
    /// DISTINCT FROM</c> returns its negation (null-safe equality). Never yields NULL.
    /// </summary>
    private async ValueTask<object?> EvaluateIsDistinctFrom(IsDistinctFromExpression expr, Row context, bool decryptSensitive = false)
    {
        var left = await EvaluateInternal(expr.Left, context, decryptSensitive);
        var right = await EvaluateInternal(expr.Right, context, decryptSensitive);
        bool leftNull = left.IsNull();
        bool rightNull = right.IsNull();
        bool distinct = (leftNull || rightNull)
            ? leftNull != rightNull          // exactly one NULL ⇒ distinct; both NULL ⇒ not distinct
            : !IsSoftEqual(left, right);
        // Not == true ⇒ IS NOT DISTINCT FROM ⇒ null-safe equality.
        return expr.Not ? !distinct : distinct;
    }

    /// <summary>Evaluates a CASE expression.</summary>
    private async ValueTask<object?> EvaluateCase(CaseExpression c, Row context, bool decryptSensitive = false)
    {
        object? inputVal = null;
        bool hasInput = c.InputExpression != null;
        if (hasInput)
        {
            inputVal = await EvaluateInternal(c.InputExpression, context, decryptSensitive);
            // SQL standard: if simple CASE operand is NULL, no WHEN can match — go straight to ELSE
            if (inputVal.IsNull())
                return await EvaluateInternal(c.ElseResult, context, decryptSensitive);
        }

        foreach (var clause in c.WhenClauses)
        {
            if (hasInput)
            {
                var whenVal = await EvaluateInternal(clause.Condition, context, decryptSensitive);
                if (IsSoftEqual(inputVal, whenVal)) return await EvaluateInternal(clause.Result, context, decryptSensitive);
            }
            else
            {
                var cond = await EvaluateInternal(clause.Condition, context, decryptSensitive);
                if (cond != null && Convert.ToBoolean(cond)) return await EvaluateInternal(clause.Result, context, decryptSensitive);
            }
        }
        return await EvaluateInternal(c.ElseResult, context, decryptSensitive);
    }

    private async ValueTask<object?> EvaluateSubstring(SubstringExpression sub, Row context, bool decryptSensitive = false)
    {
        var val = await EvaluateInternal(sub.String, context, decryptSensitive);
        if (val == null) return null;
        var s = val.ToString() ?? "";

        var startVal = await EvaluateInternal(sub.Start, context, decryptSensitive);
        if (startVal == null) return null;
        int start = Convert.ToInt32(startVal);

        int? len = null;
        if (sub.Length != null)
        {
            var lenVal = await EvaluateInternal(sub.Length, context, decryptSensitive);
            if (lenVal == null) return null;
            len = Convert.ToInt32(lenVal);
            if (len <= 0) return "";
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            int pos = i + 1;
            if (pos >= start && (len == null || pos < start + len))
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }

    private async ValueTask<object?> EvaluatePosition(PositionExpression pos, Row context, bool decryptSensitive = false)
    {
        var substrVal = await EvaluateInternal(pos.Substring, context, decryptSensitive);
        var strVal = await EvaluateInternal(pos.String, context, decryptSensitive);
        if (substrVal == null || strVal == null) return 0;

        var substr = substrVal.ToString() ?? "";
        var str = strVal.ToString() ?? "";

        // SQL POSITION returns 1-based index, or 0 if not found
        int index = str.IndexOf(substr, StringComparison.OrdinalIgnoreCase);
        return index + 1;
    }

    private async ValueTask<object?> EvaluateExtract(ExtractExpression ext, Row context, bool decryptSensitive = false)
    {
        var val = await EvaluateInternal(ext.Source, context, decryptSensitive);
        if (val == null) return null;

        DateTime dt;
        if (val is DateTime dateTime) dt = dateTime;
        else if (!DateTime.TryParse(val.ToString(), out dt)) return null;

        return ext.Field.ToUpperInvariant() switch
        {
            "YEAR" => dt.Year,
            "MONTH" => dt.Month,
            "DAY" => dt.Day,
            "HOUR" => dt.Hour,
            "MINUTE" => dt.Minute,
            "SECOND" => dt.Second,
            "MILLISECOND" => dt.Millisecond,
            "DOW" => (int)dt.DayOfWeek,
            "DOY" => dt.DayOfYear,
            "EPOCH" => (decimal)(new DateTime(dt.Ticks, dt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : dt.Kind).ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
            "QUARTER" => (dt.Month - 1) / 3 + 1,
            "WEEK" => System.Globalization.ISOWeek.GetWeekOfYear(dt),
            "ISODOW" => dt.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dt.DayOfWeek,
            "DECADE" => (int)Math.Floor(dt.Year / 10.0),
            "CENTURY" => (int)Math.Ceiling(dt.Year / 100.0),
            "MILLENNIUM" => (int)Math.Ceiling(dt.Year / 1000.0),
            _ => null
        };
    }

    private async ValueTask<object?> EvaluateOverlay(OverlayExpression ovl, Row context, bool decryptSensitive = false)
    {
        var strVal = await EvaluateInternal(ovl.String, context, decryptSensitive);
        var ovlVal = await EvaluateInternal(ovl.Overlay, context, decryptSensitive);
        var startVal = await EvaluateInternal(ovl.Start, context, decryptSensitive);

        if (strVal == null || ovlVal == null || startVal == null) return null;

        var s = strVal.ToString() ?? "";
        var o = ovlVal.ToString() ?? "";
        int start = Convert.ToInt32(startVal);

        if (start < 1) start = 1;
        int dotNetStart = start - 1;

        int len = o.Length;
        if (ovl.Length != null)
        {
            var lenVal = await EvaluateInternal(ovl.Length, context, decryptSensitive);
            if (lenVal != null) len = Convert.ToInt32(lenVal);
        }

        if (dotNetStart > s.Length) return s + o;

        var prefix = s.Substring(0, dotNetStart);
        var replacedLen = Math.Min(len, s.Length - dotNetStart);
        var suffix = (dotNetStart + replacedLen < s.Length) ? s.Substring(dotNetStart + replacedLen) : "";
        return prefix + o + suffix;
    }

    private async ValueTask<object?> EvaluateTrim(TrimExpression trim, Row context, bool decryptSensitive = false)
    {
        var val = await EvaluateInternal(trim.String, context, decryptSensitive);
        if (val == null) return null;
        var s = val.ToString() ?? "";

        char[]? chars = null;
        if (trim.Characters != null)
        {
            var cVal = await EvaluateInternal(trim.Characters, context, decryptSensitive);
            if (cVal != null) chars = cVal.ToString()?.ToCharArray();
        }

        return trim.Type switch
        {
            TrimType.LEADING => chars != null ? s.TrimStart(chars) : s.TrimStart(),
            TrimType.TRAILING => chars != null ? s.TrimEnd(chars) : s.TrimEnd(),
            _ => chars != null ? s.Trim(chars) : s.Trim()
        };
    }
}

