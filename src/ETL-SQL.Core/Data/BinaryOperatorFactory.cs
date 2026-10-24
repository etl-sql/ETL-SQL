using System;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Data;
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
        [TokenType.MODULO] = (l, r) => MathOp(l, r, "%"),
        [TokenType.LSHIFT] = (l, r) => ShiftOp(l, r, "<<"),
        [TokenType.RSHIFT] = (l, r) => ShiftOp(l, r, ">>")
    };

    private static object? MathOp(object? a, object? b, string op)
    {
        if (a == null || b == null) return null;

        // Handle date arithmetic (FW-6)
        if (a is DateTime dtA)
        {
            if (op == "+")
            {
                try { return dtA.AddDays(Convert.ToDouble(b)); } catch { return null; }
            }
            if (op == "-" && b is DateTime dtB)
            {
                return (decimal)(dtA - dtB).TotalDays;
            }
            if (op == "-")
            {
                try { return dtA.AddDays(-Convert.ToDouble(b)); } catch { return null; }
            }
        }

        decimal da, db;
        try
        {
            da = Convert.ToDecimal(a, System.Globalization.CultureInfo.InvariantCulture);
            db = Convert.ToDecimal(b, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            // COMPAT_BREAK: 0.22.0 — a double outside decimal's range (1e30, NaN, ±Infinity) is still a
            // number: compute it as a double instead of concatenating the two values as text for "+"
            // and returning NULL for the other operators.
            if (IsFloatingPoint(a) || IsFloatingPoint(b))
                return DoubleOp(a, b, op);
            return op == "+" ? a?.ToString() + b?.ToString() : null;
        }
        try
        {
            return op switch
            {
                "+" => da + db,
                "-" => da - db,
                "*" => da * db,
                "/" => db == 0 ? throw new ExecutionException("Divide by zero error encountered.")
                           : (IsIntegerType(a) && IsIntegerType(b) ? Math.Truncate(da / db) : da / db),
                "%" => db == 0 ? throw new ExecutionException("Divide by zero error encountered.") : da % db,
                _ => null
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool IsFloatingPoint(object? value) => value is double or float;

    private static object? DoubleOp(object a, object b, string op)
    {
        double da, db;
        try
        {
            da = Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture);
            db = Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
        if ((op == "/" || op == "%") && db == 0)
            throw new ExecutionException("Divide by zero error encountered.");
        var result = op switch
        {
            "+" => da + db,
            "-" => da - db,
            "*" => da * db,
            "/" => da / db,
            "%" => da % db,
            _ => double.NaN
        };
        // An out-of-range result is NULL, as decimal overflow is above.
        return double.IsFinite(result) ? result : null;
    }

    private static object? ShiftOp(object? a, object? b, string op)
    {
        if (a == null || b == null) return null;
        long valA, valB;
        try
        {
            valA = Convert.ToInt64(a, System.Globalization.CultureInfo.InvariantCulture);
            valB = Convert.ToInt64(b, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }

        try
        {
            if (op == "<<")
            {
                return valA << (int)(valB & 63);
            }
            if (op == ">>")
            {
                return valA >> (int)(valB & 63);
            }
        }
        catch (OverflowException)
        {
            return null;
        }
        return null;
    }

    private static bool IsIntegerType(object? val)
    {
        if (val == null) return false;
        if (val is int || val is long || val is short || val is byte || val is sbyte || val is uint || val is ulong || val is ushort)
            return true;
        if (val is decimal dec)
        {
            if (dec != Math.Truncate(dec)) return false;
            string s = dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return !s.Contains('.') && !s.Contains('e') && !s.Contains('E');
        }
        return false;
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
