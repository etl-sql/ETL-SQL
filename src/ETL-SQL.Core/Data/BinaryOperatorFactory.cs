using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Data
{
    /// <summary>
    /// Registry for binary operator implementations (arithmetic and logical).
    /// Replaces large switch statements in expression evaluation.
    /// </summary>
    public static class BinaryOperatorFactory
    {
        private static readonly Dictionary<TokenType, Func<object?, object?, object?>> _operators = new()
        {
            [TokenType.PLUS] = (l, r) => MathOp(l, r, "+"),
            [TokenType.MINUS] = (l, r) => MathOp(l, r, "-"),
            [TokenType.STAR] = (l, r) => MathOp(l, r, "*"),
            [TokenType.SLASH] = (l, r) => MathOp(l, r, "/"),
            [TokenType.MODULO] = (l, r) => MathOp(l, r, "%")
        };

        private static object? MathOp(object? a, object? b, string op)
        {
            if (a == null || b == null) return null;
            decimal da, db;
            try {
                da = Convert.ToDecimal(a, System.Globalization.CultureInfo.InvariantCulture);
                db = Convert.ToDecimal(b, System.Globalization.CultureInfo.InvariantCulture);
            } catch {
                return op == "+" ? a?.ToString() + b?.ToString() : null;
            }
            return op switch {
                "+" => da + db,
                "-" => da - db,
                "*" => da * db,
                "/" => db == 0 ? throw new DivideByZeroException() : da / db,
                "%" => da % db,
                _ => null
            };
        }

        /// <summary>Executes a binary operation for the given token type.</summary>
        public static object? Execute(TokenType op, object? left, object? right)
        {
            if (_operators.TryGetValue(op, out var handler)) return handler(left, right);
            return null;
        }

        /// <summary>Registers a custom operator handler.</summary>
        public static void Register(TokenType op, Func<object?, object?, object?> handler) => _operators[op] = handler;
    }
}
