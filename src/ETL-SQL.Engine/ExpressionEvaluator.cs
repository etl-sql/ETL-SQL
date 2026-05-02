using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Services;
using System.Collections.Concurrent;
using System.Reflection;

namespace ETL_SQL.Engine
{
    /// <summary>
    /// Responsible for evaluating SQL expressions (literals, identifiers, binary ops, functions) against a row context.
    /// </summary>
    public class ExpressionEvaluator
    {
        private static readonly TableSchema _scalarSchema = new TableSchema(new[] { "Value" });
        private readonly IExecutionContext _context;
        private readonly ILogger _logger;
        private static readonly ConcurrentDictionary<(Type, string), MemberInfo?> _reflectionCache = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Statement, List<string>> _outerRefCache = new();

        public ExpressionEvaluator(IExecutionContext context)
        {
            _context = context;
            _logger = context.Logger;
        }

        private bool IsResolvableInOuterScope(string name, Row? context = null)
        {
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

        private object? ResolveIdentifier(string name, Row? context)
        {
            // 1. Check immediate context (with ambiguity check)
            if (context != null)
            {
                var fb = ResolveIdentifierFallback(name, context);
                if (fb != null) return fb;
            }

            // 2. Check outer scopes (exact match)
            foreach (var outer in _context.OuterRowStack)
            {
                if (outer != null)
                {
                    var outerVal = outer[name];
                    if (outerVal != null || outer.HasColumn(name)) return outerVal;
                }
            }

            // 3. Fallback: search for column in outer scopes
            foreach (var outer in _context.OuterRowStack)
            {
                var fb = ResolveIdentifierFallback(name, outer);
                if (fb != null) return fb;
            }

            return null;
        }

        /// <summary>
        /// Provides fallback resolution for identifiers (e.g., matching 'ID' if 'T.ID' exists in the row).
        /// </summary>
        private object? ResolveIdentifierFallback(string name, Row context)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (context.HasColumn(name)) return context[name];

            var parts = name.Split('.');
            var baseName = parts.Last();
            var qualifier = name.Contains(".") ? name.Substring(0, name.LastIndexOf('.')) : null;
            var suffix = "." + baseName;

            var strongMatches = new List<string>();
            var weakMatches = new List<string>();

            // BUG FIX: Must include dynamic columns (like qualified names from joins) when checking for overlaps.
            // Using GetColumnNames() to avoid creating a full dictionary copy for every resolution.
            var allNames = context.GetColumnNames();
            
            // Pre-scan for qualified suffixes only if we have a qualifier
            var qualifiedSuffixes = qualifier != null 
                ? allNames.Where(k => k.Contains(".")).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var k in allNames)
            {
                // Case 1: Partial match on baseName or suffix
                if (k.Equals(baseName, StringComparison.OrdinalIgnoreCase) || k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (qualifier != null)
                    {
                        // User specified a qualifier (#A.ID)
                        if (k.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase))
                            strongMatches.Add(k);
                        else if (!k.Contains("."))
                        {
                            // If the row contains a strongly qualified version of this unqualified column for a DIFFERENT qualifier,
                            // then this unqualified column actually belongs to that other block.
                            var targetSuffix = "." + k;
                            bool belongsToAnother = qualifiedSuffixes!.Any(other => other.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase) && !other.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase));
                            if (!belongsToAnother)
                            {
                                weakMatches.Add(k);
                            }
                        }
                    }
                    else
                    {
                        // User did NOT specify a qualifier (ID)
                        if (!k.Contains("."))
                            strongMatches.Add(k); // Strong match: exact unqualified match
                        else
                            weakMatches.Add(k); // Weak match: ID matches #A.ID
                    }
                }
            }

            var finalMatches = strongMatches.Count > 0 ? strongMatches : weakMatches;

            if (finalMatches.Count > 1)
                throw new ExecutionException($"Ambiguous identifier '{name}'. Matches: {string.Join(", ", finalMatches)}");

            if (finalMatches.Count == 1) return context[finalMatches[0]];

            return null;
        }

        /// <summary>Evaluates an expression against a row context.</summary>
        public async Task<object?> Evaluate(Expression? expr, Row context, bool decryptSensitive = false)
        {
            return await EvaluateInternal(expr, context, decryptSensitive);
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
        public async Task<object?> EvaluateInternal(Expression? expr, Row context, bool decryptSensitive = false)
        {
            return expr switch
            {
                null => null,
                VariableExpression v => EvaluateVariable(v, decryptSensitive),
                MemberAccessExpression ma => await EvaluateMemberAccess(ma, context, decryptSensitive),
                LiteralExpression lit => (decryptSensitive && lit.Value is string s && s.StartsWith("ENC:")) ? _context.DecryptValue(s) : lit.Value,
                IdentifierExpression id => await EvaluateIdentifier(id, context),
                BinaryExpression bin => await EvaluateBinary(bin, context, decryptSensitive),
                LikeExpression like => await EvaluateLikeExpr(like, context, decryptSensitive),
                IsNullExpression isNull => await EvaluateIsNull(isNull, context, decryptSensitive),
                CaseExpression c => await EvaluateCase(c, context, decryptSensitive),
                InExpression inExp => await EvaluateIn(inExp, context, decryptSensitive),
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
                ParameterExpression p => EvaluateParameter(p),
                _ => null
            };
        }

        /// <summary>Evaluates an IN expression (list or subquery).</summary>
        private async Task<object?> EvaluateIn(InExpression inExp, Row context, bool decryptSensitive = false)
        {
            var l = await EvaluateInternal(inExp.Left, context, decryptSensitive);
            bool found = false;
            
            if (inExp.Right is SubqueryExpression subq)
            {
                // Use cached stream evaluation for subqueries in IN clauses
                await foreach (var rowVal in EvaluateStreamSubquery(subq, context))
                {
                    if (!l.IsNull() && !rowVal.IsNull())
                    {
                        if (IsSoftEqual(l, rowVal)) { found = true; break; }
                    }
                }
            }
            else if (inExp.Right is ListExpression list)
            {
                foreach (var item in list.Items)
                {
                    var itemVal = await EvaluateInternal(item, context, decryptSensitive);
                    if (!l.IsNull() && !itemVal.IsNull())
                    {
                        if (IsSoftEqual(l, itemVal)) { found = true; break; }
                    }
                }
            }
            return inExp.IsNot ? !found : found;
        }
    

        /// <summary>Evaluates an EXISTS clause.</summary>
        private async Task<object?> EvaluateExists(ExistsExpression ex, Row context)
        {
            if (ex.Subquery is SelectStatement select)
            {
                bool found = await EvaluateExistsSubquery(select, context);
                return ex.IsNot ? !found : found;
            }
            throw new ExecutionException("EXISTS subquery must be a SELECT statement.");
        }

        /// <summary>Evaluates a list of expressions.</summary>
        private async Task<object?> EvaluateList(ListExpression listExpr, Row context, bool decryptSensitive = false)
        {
            var result = new List<object?>();
            foreach (var item in listExpr.Items) result.Add(await EvaluateInternal(item, context, decryptSensitive));
            return result;
        }

        /// <summary>Evaluates a scalar or aggregate function call.</summary>
        public async Task<object?> EvaluateFunction(FunctionCallExpression f, Row context, bool decryptSensitive = false)
        {
            if (f.Window != null) 
            {
                var winKey = $"WINDOW_{f.ToSql().ToUpperInvariant()}";
                return context.Columns.ContainsKey(winKey) ? context[winKey] : null;
            }
            
            // Check for pre-calculated aggregate results (used in HAVING clause)
            var aggKey = $"AGG_{f.ToSql().ToUpperInvariant()}";
            if (context != null && context.Columns.TryGetValue(aggKey, out var aggVal)) return aggVal;

            var fn = f.FunctionName.ToUpperInvariant();
            
            // ANSI String length aliases
            if (fn == "CHARACTER_LENGTH" || fn == "CHAR_LENGTH" || fn == "OCTET_LENGTH")
            {
                var val = await EvaluateInternal(f.Arguments.FirstOrDefault(), context ?? new Row(), decryptSensitive);
                if (val == null) return null;
                var s = val.ToString() ?? "";
                return fn == "OCTET_LENGTH" ? System.Text.Encoding.UTF8.GetByteCount(s) : s.Length;
            }

            if (fn == "SYSDATE" || fn == "GETDATE" || fn == "CURRENT_TIMESTAMP") return DateTime.Now;
            if (fn == "CURRENT_DATE") return DateTime.Today;
            if (fn == "CURRENT_TIME") return DateTime.Now.TimeOfDay;

            var args = new List<object?>();
            for (int i = 0; i < f.Arguments.Count; i++)
            {
                var arg = f.Arguments[i];
                if (i == 0 && (fn == "DATEPART" || fn == "DATEDIFF" || fn == "DATENAME" || fn == "DATEADD") && arg is IdentifierExpression idArg)
                {
                    args.Add(idArg.Name);
                }
                else
                {
                    args.Add(await EvaluateInternal(arg, context ?? new Row(), decryptSensitive));
                }
            }

            if (_context.FunctionRegistry.IsRegistered(fn))
            {
                return await _context.FunctionRegistry.ExecuteAsync(fn, args, _context);
            }

            return await _context.EvaluateUserDefinedFunction(f, args, context ?? new Row());
        }

        /// <summary>Evaluates the AT TIME ZONE expression.</summary>
        private async Task<object?> EvaluateAtTimeZone(AtTimeZoneExpression atTz, Row context, bool decryptSensitive = false)
        {
            var val = await EvaluateInternal(atTz.Left, context, decryptSensitive);
            var zone = await EvaluateInternal(atTz.TimeZone, context, decryptSensitive);
            if (val == null || zone == null) return val;
            
            DateTime dt = DateTime.Parse(val.ToString() ?? "");
            if (dt.Kind == DateTimeKind.Unspecified) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            
            try {
                var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(zone.ToString() ?? "UTC");
                return TimeZoneInfo.ConvertTime(dt, tzInfo);
            } catch {
                return dt;
            }
        }

        /// <summary>Evaluates a scalar subquery.</summary>
        private async Task<object?> EvaluateSubquery(SubqueryExpression subq, Row context)
        {
            object? result = null;
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

            _context.SubqueryCache.Set(cacheKey, new SubqueryResult(result));
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
            
            // Materialize or Spill fully BEFORE yielding to ensure cache is populated
            long rowCount = 0;
            var inSet = new HashSet<object?>(CanonicalEqualityComparer.Instance);
            InMemoryDataSource? spillStore = null;
            
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
                            await spillStore.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { batch })); 
                        }
                        else if (rowCount > _context.SubquerySpillThresholdRows)
                        {
                            spillStore = new InMemoryDataSource();
                            spillStore.SetSchema(batch.Schema.ColumnNames.Select(c => new ColumnDefinition(c, "VARIANT", false)).ToList());
                            
                            var dt = new DataTable { Schema = new TableSchema(new[] { "Value" }) };
                            foreach(var existing in inSet!) await dt.AddRowAsync(new Row(dt.Schema, new[] { existing }));
                            await spillStore.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { dt }));
                            
                            var currentBatch = new DataTable { Schema = batch.Schema };
                            await currentBatch.AddRowAsync(row);
                            await spillStore.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { currentBatch }));
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
                finalResult = new SubqueryResult(spillStore);
                _context.SubqueryCache.Set(cacheKey, finalResult);
                await foreach (var batch in spillStore.ReadBatches())
                {
                    foreach (var row in batch.Rows) yield return row[0];
                }
            }
            else
            {
                finalResult = new SubqueryResult(inSet ?? new HashSet<object?>());
                _context.SubqueryCache.Set(cacheKey, finalResult);
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

            _context.SubqueryCache.Set(cacheKey, new SubqueryResult(found));
            return found;
        }

        /// <summary>Checks for soft equality between two objects.</summary>
        public bool IsSoftEqual(object? a, object? b) => EvaluationUtils.IsSoftEqual(a, b, _logger);
        
        /// <summary>Compares two values for ordering.</summary>
        public int CompareConstants(object? a, object? b) => EvaluationUtils.CompareConstants(a, b);
        
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


            
            if (!_context.VarContext.ContainsVariable(v.Name))
                throw new ExecutionException($"Undeclared: {v.Name}");
            
            var val = _context.VarContext.GetVariable(v.Name);

            if (val is string reldateExpr &&
                _context.VarContext.VariableMetadata.TryGetValue(v.Name, out var relMeta) &&
                "RELDATE".Equals(relMeta.DataType, StringComparison.OrdinalIgnoreCase))
            {
                return RelDateResolver.Resolve(reldateExpr, _context.WeekStartDay);
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
        private async Task<object?> EvaluateMemberAccess(MemberAccessExpression ma, Row context, bool decryptSensitive = false)
        {
            var val = await EvaluateInternal(ma.Expression, context, decryptSensitive);
            if (val == null) return null;
            
            // Handle Row or IDictionary
            if (val is Row row) 
            {
                // Try direct indexer (best for schema-backed columns)
                var r = row[ma.MemberName];
                if (r != null) return r;
                
                // Fallback: Check if it's explicitly null in the Columns dictionary or a dynamic column
                if (row.Columns.TryGetValue(ma.MemberName, out var dynamicVal)) return dynamicVal;
                
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
        private async Task<object?> EvaluateIdentifier(IdentifierExpression id, Row context)
        {
            var val = ResolveIdentifier(id.Name, context);
            if (val != null || (context != null && context.HasColumn(id.Name))) return val;

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
        private async Task<object?> EvaluateBinary(BinaryExpression bin, Row context, bool decryptSensitive = false)
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
                bin.Operator == TokenType.MODULO) return null;

            return bin.Operator switch
            {
                TokenType.EQUALS => (!leftVal.IsNull() && !rightVal.IsNull()) && IsSoftEqual(leftVal, rightVal),
                TokenType.NOT_EQUALS => (!leftVal.IsNull() && !rightVal.IsNull()) && !IsSoftEqual(leftVal, rightVal),
                TokenType.GREATER_THAN => (!leftVal.IsNull() && !rightVal.IsNull()) && CompareConstants(leftVal, rightVal) > 0,
                TokenType.LESS_THAN => (!leftVal.IsNull() && !rightVal.IsNull()) && CompareConstants(leftVal, rightVal) < 0,
                TokenType.GREATER_EQUALS => (!leftVal.IsNull() && !rightVal.IsNull()) && CompareConstants(leftVal, rightVal) >= 0,
                TokenType.LESS_EQUALS => (!leftVal.IsNull() && !rightVal.IsNull()) && CompareConstants(leftVal, rightVal) <= 0,
                TokenType.LIKE => EvaluateLike(leftVal, rightVal),
                _ => IsSoftEqual(leftVal, rightVal)
            };
        }

        /// <summary>Evaluates a LIKE expression.</summary>
        private async Task<object?> EvaluateLikeExpr(LikeExpression like, Row context, bool decryptSensitive = false)
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
        private async Task<object?> EvaluateIsNull(IsNullExpression isNull, Row context, bool decryptSensitive = false)
        {
            var val = await EvaluateInternal(isNull.Expression, context, decryptSensitive);
            bool res = val == null || val == DBNull.Value || (val is string s && string.IsNullOrEmpty(s) && _context.VarContext.GetVariable("NULL_AS_EMPTY")?.ToString() == "TRUE");
            return isNull.Not ? !res : res;
        }

        /// <summary>Evaluates a CASE expression.</summary>
        private async Task<object?> EvaluateCase(CaseExpression c, Row context, bool decryptSensitive = false)
        {
            foreach (var clause in c.WhenClauses)
            {
                var cond = await EvaluateInternal(clause.Condition, context, decryptSensitive);
                if (cond != null && Convert.ToBoolean(cond)) return await EvaluateInternal(clause.Result, context, decryptSensitive);
            }
            return await EvaluateInternal(c.ElseResult, context, decryptSensitive);
        }

        private async Task<object?> EvaluateSubstring(SubstringExpression sub, Row context, bool decryptSensitive = false)
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

        private async Task<object?> EvaluatePosition(PositionExpression pos, Row context, bool decryptSensitive = false)
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

        private async Task<object?> EvaluateExtract(ExtractExpression ext, Row context, bool decryptSensitive = false)
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
                _ => null
            };
        }

        private async Task<object?> EvaluateOverlay(OverlayExpression ovl, Row context, bool decryptSensitive = false)
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

        private async Task<object?> EvaluateTrim(TrimExpression trim, Row context, bool decryptSensitive = false)
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
}

