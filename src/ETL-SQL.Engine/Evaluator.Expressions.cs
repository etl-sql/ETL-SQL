using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine
{
    public partial class Evaluator
    {
        public async ValueTask<object?> ExecuteValue(string expression, Row? context = null, bool decryptSensitive = false)
        {
            var lexer = new ETL_SQL.Core.Parser.Lexer(expression);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, expression);
            var expr = parser.ParseExpression();
            return await EvaluateValue(expr, context ?? new Row(), decryptSensitive);
        }

        public ValueTask<object?> EvaluateValue(Expression? expr, Row context, bool decryptSensitive = false)
            => _expressionEvaluator.Evaluate(expr, context, decryptSensitive);

        public IAsyncEnumerable<Row> EvaluateStream(Expression? expr, Row context)
            => _expressionEvaluator.EvaluateStream(expr, context);

        public CompiledSql CompileExpression(Expression e, string d = "MSSQL")
            => _queryCompiler.CompileExpression(e, d);

        public CompiledSql CompileQuery(Statement s, string d = "MSSQL")
            => _queryCompiler.CompileQuery(s, d);

        public string GetSqlTableName(TableReference t, string dialect = "MSSQL")
        {
            var parts = new List<string>();
            if (t.DatabaseName != null) parts.Add(t.DatabaseName);
            if (t.SchemaName != null) parts.Add(t.SchemaName);

            if (t.TableName.Contains(".") && t.SchemaName == null)
            {
                parts.AddRange(t.TableName.Split('.'));
            }
            else
            {
                parts.Add(t.TableName);
            }

            Func<string, string> quote = dialect.ToUpperInvariant() switch
            {
                "MSSQL" => QuoteIdentifierMssql,
                "ORACLE" => s => QuoteIdentifierStandard(s.ToUpperInvariant()),
                _ => QuoteIdentifierStandard
            };

            return string.Join(".", parts.Select(quote));
        }

        private static string QuoteIdentifierMssql(string s) =>
            s.StartsWith("[") ? s : $"[{s.Replace("]", "]]")}]";

        private static string QuoteIdentifierStandard(string s)
        {
            if (s.StartsWith("\"")) return s;
            bool needsQuoting = s.Any(c => !char.IsLetterOrDigit(c) && c != '_');
            return needsQuoting ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }

        public bool IsSoftEqual(object? l, object? r) => _expressionEvaluator.IsSoftEqual(l, r);
        public int CompareConstants(object? l, object? r) => _expressionEvaluator.CompareConstants(l, r);
        public object? MathOp(object? l, object? r, TokenType op) => _expressionEvaluator.MathOp(l, r, op);
        public bool EvaluateLike(object? left, object? right) => _expressionEvaluator.EvaluateLike(left, right);

        public ValueTask<object?> EvaluateUserDefinedFunction(FunctionCallExpression f, List<object?> args, Row context)
            => _procedureExecutor.EvaluateUserDefinedFunction(f, args, context);

        public Task EvaluateProcedure(string name, List<(string? Name, object? Value)> args)
            => _procedureExecutor.EvaluateProcedure(name, args);

        public async ValueTask<bool> EvaluateCondition(Expression? expr, Row context)
        {
            if (expr == null) return true;
            var res = await EvaluateValue(expr, context);
            if (res == null || res == DBNull.Value) return false;
            if (res is bool b) return b;
            try { return Convert.ToBoolean(res); } catch { return false; }
        }

        public List<string> GetIndexedColumns(Expression? cond, string alias)
        {
            var cols = new List<string>();
            if (cond is BinaryExpression bin)
            {
                if (bin.Operator == TokenType.EQUALS)
                {
                    if (bin.Left is IdentifierExpression lid && IsFromAlias(lid.Name, alias)) cols.Add(GetColumnName(lid.Name));
                    if (bin.Right is IdentifierExpression rid && IsFromAlias(rid.Name, alias)) cols.Add(GetColumnName(rid.Name));
                }
                else if (bin.Operator == TokenType.AND)
                {
                    cols.AddRange(GetIndexedColumns(bin.Left, alias));
                    cols.AddRange(GetIndexedColumns(bin.Right, alias));
                }
            }
            return cols.Distinct().ToList();
        }

        private bool IsFromAlias(string identifier, string? alias)
        {
            if (string.IsNullOrEmpty(alias)) return true;
            if (identifier.Contains(".")) return identifier.StartsWith(alias + ".", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private string GetColumnName(string identifier)
        {
            int dot = identifier.IndexOf('.');
            return dot >= 0 ? identifier.Substring(dot + 1) : identifier;
        }

        public object? CastToType(object? value, string dataType) => _expressionEvaluator.CastToType(value, dataType);
        public bool IsSqlPushdown(string conn)
            => !string.Equals(conn, "DUAL", StringComparison.OrdinalIgnoreCase)
               && _connections.TryGetValue(conn, out var ds)
               && ds is IDatabaseSource db
               && db.SupportsSqlPushdown;
    }
}
