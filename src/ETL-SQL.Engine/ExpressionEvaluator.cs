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

        public ExpressionEvaluator(IExecutionContext context)
        {
            _context = context;
            _logger = context.Logger;
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

            // Optimization: Pre-calculate the set of all qualified names to avoid O(n^2) search
            var allNames = context.Columns.Keys;
            var qualifiedSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (qualifier != null)
            {
                foreach (var k in allNames)
                {
                    if (k.Contains(".")) qualifiedSuffixes.Add(k);
                }
            }

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
                            bool belongsToAnother = qualifiedSuffixes.Any(other => other.EndsWith(targetSuffix, StringComparison.OrdinalIgnoreCase) && !other.StartsWith(qualifier + ".", StringComparison.OrdinalIgnoreCase));
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
        public async Task<object?> Evaluate(Expression? expr, Row context)
        {
            return await EvaluateInternal(expr, context);
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
        public async Task<object?> EvaluateInternal(Expression? expr, Row context)
        {
            return expr switch
            {
                null => null,
                VariableExpression v => EvaluateVariable(v),
                MemberAccessExpression ma => await EvaluateMemberAccess(ma, context),
                LiteralExpression lit => lit.Value,
                IdentifierExpression id => EvaluateIdentifier(id, context),
                BinaryExpression bin => await EvaluateBinary(bin, context),
                LikeExpression like => await EvaluateLikeExpr(like, context),
                IsNullExpression isNull => await EvaluateIsNull(isNull, context),
                CaseExpression c => await EvaluateCase(c, context),
                InExpression inExp => await EvaluateIn(inExp, context),
                ExistsExpression ex => await EvaluateExists(ex, context),
                ListExpression listExpr => await EvaluateList(listExpr, context),
                AtTimeZoneExpression atTz => await EvaluateAtTimeZone(atTz, context),
                SubstringExpression sub => await EvaluateSubstring(sub, context),
                PositionExpression pos => await EvaluatePosition(pos, context),
                ExtractExpression ext => await EvaluateExtract(ext, context),
                OverlayExpression ovl => await EvaluateOverlay(ovl, context),
                TrimExpression trim => await EvaluateTrim(trim, context),
                FunctionCallExpression f => await EvaluateFunction(f, context),
                SubqueryExpression subq => await EvaluateSubquery(subq, context),
                _ => null
            };
        }

        /// <summary>Evaluates an IN expression (list or subquery).</summary>
        private async Task<object?> EvaluateIn(InExpression inExp, Row context)
        {
            var l = await EvaluateInternal(inExp.Left, context);
            bool found = false;
            var inSubq = inExp.Subquery ?? (inExp.Right as SubqueryExpression)?.Query;
            if (inSubq != null || inExp.Right is SubqueryExpression)
            {
                await foreach (var row in EvaluateStream(inExp.Right, context))
                {
                if (row.Schema?.ColumnCount > 0)
                {
                    var rowVal = row[0];
                    if (!l.IsNull() && !rowVal.IsNull())
                    {
                        if (IsSoftEqual(l, rowVal)) { found = true; break; }
                    }
                }
                }
            }
            else if (inExp.Right is ListExpression list)
            {
                foreach (var item in list.Items)
                {
                    var itemVal = await EvaluateInternal(item, context);
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
            bool found = false;
            await foreach (var row in EvaluateStream(new SubqueryExpression(ex.Subquery), context))
            {
                found = true;
                break;
            }
            return ex.IsNot ? !found : found;
        }

        /// <summary>Evaluates a list of expressions.</summary>
        private async Task<object?> EvaluateList(ListExpression listExpr, Row context)
        {
            var result = new List<object?>();
            foreach (var item in listExpr.Items) result.Add(await EvaluateInternal(item, context));
            return result;
        }

        /// <summary>Evaluates a scalar or aggregate function call.</summary>
        public async Task<object?> EvaluateFunction(FunctionCallExpression f, Row context)
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
                var val = await EvaluateInternal(f.Arguments.FirstOrDefault(), context ?? new Row());
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
                if (i == 0 && (fn == "DATEPART" || fn == "DATEDIFF" || fn == "DATENAME") && arg is IdentifierExpression idArg)
                {
                    args.Add(idArg.Name);
                }
                else
                {
                    args.Add(await EvaluateInternal(arg, context ?? new Row()));
                }
            }

            if (_context.FunctionRegistry.IsRegistered(fn))
            {
                return await _context.FunctionRegistry.ExecuteAsync(fn, args, _context);
            }

            return await _context.EvaluateUserDefinedFunction(f, args, context ?? new Row());
        }

        /// <summary>Evaluates the AT TIME ZONE expression.</summary>
        private async Task<object?> EvaluateAtTimeZone(AtTimeZoneExpression atTz, Row context)
        {
            var val = await EvaluateInternal(atTz.Left, context);
            var zone = await EvaluateInternal(atTz.TimeZone, context);
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
            if (_context.SubqueryCache.TryGetValue(subq.Query, out var cached))
            {
                _context.SubqueryCacheHits++;
                return cached;
            }
            _context.SubqueryCacheMisses++;
            _context.OuterRowStack.Push(context);
            await foreach (var batch in _context.ExecuteQuery(subq.Query))
            {
                if (batch.Rows.Count > 0 && batch.ColumnNames.Count > 0)
                {
                    result = batch.Rows[0][0];
                }
                break;
            }
            _context.OuterRowStack.Pop();
            if (result != null)
                _context.SubqueryCache.Set(subq.Query, result);
            return result;
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
        private object? EvaluateVariable(VariableExpression v)
        {
            if (v.Name.Equals("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)) return _context.TranCount;
            if (v.Name.Equals("@@RESULTSETS", StringComparison.OrdinalIgnoreCase)) return _context.LastResultSets;
            if (v.Name.Equals("@@VERSION", StringComparison.OrdinalIgnoreCase)) return LanguageMetadata.GetFullVersionString();
            if (v.Name.Equals("@@ROWCOUNT", StringComparison.OrdinalIgnoreCase)) return _context.RowsProcessed;
            if (v.Name.Equals("@@ERROR", StringComparison.OrdinalIgnoreCase)) return _context.PreviousErrorNumber;
            if (v.Name.Equals("@@TOTAL_SPILLED_BYTES", StringComparison.OrdinalIgnoreCase)) return _context.TotalSpilledBytes;
            if (v.Name.Equals("@@PARTITIONS_COUNT", StringComparison.OrdinalIgnoreCase)) return _context.PartitionsCount;
            if (v.Name.Equals("@@AGGREGATE_GROUPS_COUNT", StringComparison.OrdinalIgnoreCase)) return _context.AggregateGroupsCount;
            if (v.Name.Equals("@@AGGREGATE_EXPANSION_RATIO", StringComparison.OrdinalIgnoreCase)) return _context.AggregateExpansionRatio;
            if (v.Name.Equals("@@LAST_EXEC_MS", StringComparison.OrdinalIgnoreCase)) return _context.LastExecutionTimeMs;
            if (v.Name.Equals("@@PEAK_MEMORY_MB", StringComparison.OrdinalIgnoreCase)) return Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
            if (v.Name.Equals("@@SUBQUERY_CACHE_HITS", StringComparison.OrdinalIgnoreCase)) return _context.SubqueryCacheHits;
            if (v.Name.Equals("@@SORT_SPILLS", StringComparison.OrdinalIgnoreCase)) return (long)_context.SortSpillCount;


            
            if (!_context.ContainsVariable(v.Name))
                throw new ExecutionException($"Undeclared: {v.Name}");
                
            return _context.GetVariable(v.Name);
        }

        /// <summary>Evaluates a member access expression (e.g., row.Member or object.Property).</summary>
        private async Task<object?> EvaluateMemberAccess(MemberAccessExpression ma, Row context)
        {
            var val = await EvaluateInternal(ma.Expression, context);
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
        private object? EvaluateIdentifier(IdentifierExpression id, Row context)
        {
            var val = ResolveIdentifier(id.Name, context);
            if (val != null || (context != null && context.Columns.ContainsKey(id.Name))) return val;

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

            // Special identifiers that act as functions/literals
            if (id.Name.Equals("*", StringComparison.OrdinalIgnoreCase)) return "*";
            if (id.Name.Equals("SYSDATE", StringComparison.OrdinalIgnoreCase) || 
                id.Name.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now;
            if (id.Name.Equals("CURRENT_DATE", StringComparison.OrdinalIgnoreCase))
                return DateTime.Today;
            if (id.Name.Equals("CURRENT_TIME", StringComparison.OrdinalIgnoreCase))
                return DateTime.Now.TimeOfDay;

            // For date parts (year, month, etc.) and others, return name if Row is null
            if (context == null) return id.Name;

            return null;
        }

        /// <summary>Evaluates a binary operation (arithmetic or logical).</summary>
        private async Task<object?> EvaluateBinary(BinaryExpression bin, Row context)
        {
            if (bin.Operator == TokenType.AND)
            {
                var lVal = await EvaluateInternal(bin.Left, context);
                // IF L is FALSE, result is FALSE (Short-circuit)
                if (!lVal.IsNull() && !Convert.ToBoolean(lVal)) return false;

                var rVal = await EvaluateInternal(bin.Right, context);
                // IF R is FALSE, result is FALSE
                if (!rVal.IsNull() && !Convert.ToBoolean(rVal)) return false;

                // IF either is NULL, result is NULL (UNKNOWN)
                if (lVal.IsNull() || rVal.IsNull()) return null;

                // Both must be TRUE
                return true;
            }
            if (bin.Operator == TokenType.OR)
            {
                var lVal = await EvaluateInternal(bin.Left, context);
                // IF L is TRUE, result is TRUE (Short-circuit)
                if (!lVal.IsNull() && Convert.ToBoolean(lVal)) return true;

                var rVal = await EvaluateInternal(bin.Right, context);
                // IF R is TRUE, result is TRUE
                if (!rVal.IsNull() && Convert.ToBoolean(rVal)) return true;

                // IF either is NULL, result is NULL (UNKNOWN)
                if (lVal.IsNull() || rVal.IsNull()) return null;

                // Both must be FALSE
                return false;
            }

            var leftVal = await EvaluateInternal(bin.Left, context);
            var rightVal = await EvaluateInternal(bin.Right, context);

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
        private async Task<object?> EvaluateLikeExpr(LikeExpression like, Row context)
        {
            var l = await EvaluateInternal(like.Left, context);
            var r = await EvaluateInternal(like.Pattern, context);
            string? escapeStr = null;
            if (like.EscapeChar != null)
            {
                var escVal = await EvaluateInternal(like.EscapeChar, context);
                escapeStr = escVal?.ToString();
            }
            bool res = EvaluateLike(l, r, escapeStr);
            return like.IsNot ? !res : res;
        }

        /// <summary>Evaluates an IS NULL or IS NOT NULL expression.</summary>
        private async Task<object?> EvaluateIsNull(IsNullExpression isNull, Row context)
        {
            var val = await EvaluateInternal(isNull.Expression, context);
            bool res = val == null || val == DBNull.Value || (val is string s && string.IsNullOrEmpty(s) && _context.GetVariable("NULL_AS_EMPTY")?.ToString() == "TRUE");
            return isNull.Not ? !res : res;
        }

        /// <summary>Evaluates a CASE expression.</summary>
        private async Task<object?> EvaluateCase(CaseExpression c, Row context)
        {
            foreach (var clause in c.WhenClauses)
            {
                var cond = await EvaluateInternal(clause.Condition, context);
                if (cond != null && Convert.ToBoolean(cond)) return await EvaluateInternal(clause.Result, context);
            }
            return await EvaluateInternal(c.ElseResult, context);
        }

        private async Task<object?> EvaluateSubstring(SubstringExpression sub, Row context)
        {
            var val = await EvaluateInternal(sub.String, context);
            if (val == null) return null;
            var s = val.ToString() ?? "";
            
            var startVal = await EvaluateInternal(sub.Start, context);
            if (startVal == null) return null;
            int start = Convert.ToInt32(startVal);
            
            int? len = null;
            if (sub.Length != null)
            {
                var lenVal = await EvaluateInternal(sub.Length, context);
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

        private async Task<object?> EvaluatePosition(PositionExpression pos, Row context)
        {
            var substrVal = await EvaluateInternal(pos.Substring, context);
            var strVal = await EvaluateInternal(pos.String, context);
            if (substrVal == null || strVal == null) return 0;
            
            var substr = substrVal.ToString() ?? "";
            var str = strVal.ToString() ?? "";
            
            // SQL POSITION returns 1-based index, or 0 if not found
            int index = str.IndexOf(substr, StringComparison.OrdinalIgnoreCase);
            return index + 1;
        }

        private async Task<object?> EvaluateExtract(ExtractExpression ext, Row context)
        {
            var val = await EvaluateInternal(ext.Source, context);
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

        private async Task<object?> EvaluateOverlay(OverlayExpression ovl, Row context)
        {
            var strVal = await EvaluateInternal(ovl.String, context);
            var ovlVal = await EvaluateInternal(ovl.Overlay, context);
            var startVal = await EvaluateInternal(ovl.Start, context);
            
            if (strVal == null || ovlVal == null || startVal == null) return null;
            
            var s = strVal.ToString() ?? "";
            var o = ovlVal.ToString() ?? "";
            int start = Convert.ToInt32(startVal);
            
            if (start < 1) start = 1;
            int dotNetStart = start - 1;
            
            int len = o.Length;
            if (ovl.Length != null)
            {
                var lenVal = await EvaluateInternal(ovl.Length, context);
                if (lenVal != null) len = Convert.ToInt32(lenVal);
            }
            
            if (dotNetStart > s.Length) return s + o;
            
            var prefix = s.Substring(0, dotNetStart);
            var replacedLen = Math.Min(len, s.Length - dotNetStart);
            var suffix = (dotNetStart + replacedLen < s.Length) ? s.Substring(dotNetStart + replacedLen) : "";
            return prefix + o + suffix;
        }

        private async Task<object?> EvaluateTrim(TrimExpression trim, Row context)
        {
            var val = await EvaluateInternal(trim.String, context);
            if (val == null) return null;
            var s = val.ToString() ?? "";
            
            char[]? chars = null;
            if (trim.Characters != null)
            {
                var cVal = await EvaluateInternal(trim.Characters, context);
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
