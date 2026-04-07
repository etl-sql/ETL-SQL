using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine
{
    /// <summary>
    /// Responsible for evaluating SQL expressions (literals, identifiers, binary ops, functions) against a row context.
    /// </summary>
    public class ExpressionEvaluator
    {
        private readonly IExecutionContext _context;

        public ExpressionEvaluator(IExecutionContext context)
        {
            _context = context;
        }

        private object? ResolveIdentifier(string name, Row? context)
        {
            // 1. Check immediate context (exact match)
            if (context != null)
            {
                var val = context[name];
                if (val != null || context.HasColumn(name)) return val;
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

            // 3. Fallback: search for column that ends with "." + name or matches name (immediate context)
            if (context != null)
            {
                var fb = ResolveIdentifierFallback(name, context);
                if (fb != null) return fb;
            }

            // 4. Fallback: search for column in outer scopes
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

            // Case 1: name is unqualified ('date'), search for context key ending with '.date'
            if (!name.Contains("."))
            {
                var suffix = "." + name;
                foreach (var k in context.Columns.Keys) // Still need to iterate keys for fallback, but this is less frequent
                {
                    if (k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return context[k];
                }
            }
            
            // Case 2: name is qualified ('s.date'), but we only have 'date'
            // We only do this if it's the ONLY possible match for the basename
            var parts = name.Split('.');
            if (parts.Length > 1)
            {
                var baseName = parts.Last();
                // Check if 'baseName' exists unqualified
                var val = context[baseName];
                if (val != null || context.HasColumn(baseName))
                {
                    // Only return if no other qualified version of this basename exists in the row
                    // (to avoid matching rj1.ID to rj2.ID when row has ID and rj2.ID)
                    var suffix = "." + baseName;
                    bool hasOther = false;
                    foreach (var k in context.Columns.Keys)
                    {
                        if (k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && !k.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            hasOther = true;
                            break;
                        }
                    }
                    if (!hasOther) return val;
                }
            }

            return null;
        }

        /// <summary>Evaluates an expression against a row context.</summary>
        public async Task<object?> Evaluate(Expression? expr, Row context)
        {
            return await EvaluateInternal(expr, context);
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
            if (inSubq != null)
            {
                _context.OuterRowStack.Push(context);
                await foreach (var batch in _context.ExecuteQuery(inSubq))
                {
                    foreach (var row in batch.Rows)
                    {
                        if (batch.ColumnNames.Count > 0)
                        {
                            var val = row[0];
                            if (IsSoftEqual(l, val)) { found = true; break; }
                        }
                    }
                    if (found) break;
                }
                _context.OuterRowStack.Pop();
            }
            else if (inExp.Right is ListExpression list)
            {
                foreach (var item in list.Items)
                {
                    if (IsSoftEqual(l, await EvaluateInternal(item, context))) { found = true; break; }
                }
            }
            return inExp.IsNot ? !found : found;
        }

        /// <summary>Evaluates an EXISTS clause.</summary>
        private async Task<object?> EvaluateExists(ExistsExpression ex, Row context)
        {
            bool found = false;
            _context.OuterRowStack.Push(context);
            await foreach (var batch in _context.ExecuteQuery(ex.Subquery))
            {
                if (batch.Rows.Count > 0) { found = true; break; }
            }
            _context.OuterRowStack.Pop();
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
            if (f.Window != null) return context.Columns.ContainsKey($"WINDOW_{f.ToSql().ToUpperInvariant()}") ? context[$"WINDOW_{f.ToSql().ToUpperInvariant()}"] : null;
            
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
            if (_context.SubqueryCache.TryGetValue(subq.Query, out var cached)) return cached;
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
            // Limit cache size to prevent unbounded growth in long-running sessions.
            if (result != null && _context.SubqueryCache.Count < 1000)
                _context.SubqueryCache[subq.Query] = result;
            return result;
        }

        /// <summary>Checks for soft equality between two objects.</summary>
        public bool IsSoftEqual(object? a, object? b) => EvaluationUtils.IsSoftEqual(a, b);
        
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
            if (v.Name.Equals("@@TRANCOUNT", StringComparison.OrdinalIgnoreCase)) return _context.GetVariable("@@TRANCOUNT");
            if (v.Name.Equals("@@RESULTSETS", StringComparison.OrdinalIgnoreCase)) return _context.LastResultSets;
            
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
            if (val is Row row && row.Columns.TryGetValue(ma.MemberName, out var rVal)) return rVal;
            if (val is IDictionary<string, object?> dict && dict.TryGetValue(ma.MemberName, out var dVal)) return dVal;
            
            // Handle reflection for properties/fields
            var prop = val.GetType().GetProperty(ma.MemberName);
            if (prop != null) return prop.GetValue(val);
            
            var field = val.GetType().GetField(ma.MemberName);
            if (field != null) return field.GetValue(val);

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
                var l = await EvaluateInternal(bin.Left, context);
                if (l == null || l == DBNull.Value || !Convert.ToBoolean(l)) return false;
                var r = await EvaluateInternal(bin.Right, context);
                return r != null && r != DBNull.Value && Convert.ToBoolean(r);
            }
            if (bin.Operator == TokenType.OR)
            {
                var l = await EvaluateInternal(bin.Left, context);
                if (l != null && l != DBNull.Value && Convert.ToBoolean(l)) return true;
                var r = await EvaluateInternal(bin.Right, context);
                return r != null && r != DBNull.Value && Convert.ToBoolean(r);
            }

            var lVal = await EvaluateInternal(bin.Left, context);
            var rVal = await EvaluateInternal(bin.Right, context);

            // Use the registry for arithmetic and simple logical operators
            var result = BinaryOperatorFactory.Execute(bin.Operator, lVal, rVal);
            if (result != null) return result;

            return bin.Operator switch
            {
                TokenType.EQUALS => (lVal != null && lVal != DBNull.Value && rVal != null && rVal != DBNull.Value) && IsSoftEqual(lVal, rVal),
                TokenType.NOT_EQUALS => (lVal != null && lVal != DBNull.Value && rVal != null && rVal != DBNull.Value) && !IsSoftEqual(lVal, rVal),
                TokenType.GREATER_THAN => (lVal != null && rVal != null) && CompareConstants(lVal, rVal) > 0,
                TokenType.LESS_THAN => (lVal != null && rVal != null) && CompareConstants(lVal, rVal) < 0,
                TokenType.GREATER_EQUALS => (lVal != null && rVal != null) && CompareConstants(lVal, rVal) >= 0,
                TokenType.LESS_EQUALS => (lVal != null && rVal != null) && CompareConstants(lVal, rVal) <= 0,
                TokenType.LIKE => EvaluateLike(lVal, rVal),
                _ => IsSoftEqual(lVal, rVal)
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
            
            // SQL SUBSTRING is 1-indexed
            if (start < 1) start = 1;
            int dotNetStart = start - 1;
            if (dotNetStart >= s.Length) return "";
            
            if (sub.Length != null)
            {
                var lenVal = await EvaluateInternal(sub.Length, context);
                if (lenVal == null) return null;
                int len = Convert.ToInt32(lenVal);
                if (len <= 0) return "";
                if (dotNetStart + len > s.Length) len = s.Length - dotNetStart;
                return s.Substring(dotNetStart, len);
            }
            return s.Substring(dotNetStart);
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
