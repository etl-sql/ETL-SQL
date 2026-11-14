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

    // An operand that is not a number, or a result that does not fit, is an error. Both used to
    // give NULL, so `'abc' - 5` and an overflowing product read as missing data.
    private static ExecutionException NotNumeric(object a, object b, string op) =>
        new($"Cannot apply '{op}' to '{TypeConverter.ToInvariantText(a)}' and '{TypeConverter.ToInvariantText(b)}': both operands must be numbers.");

    private static ExecutionException Overflow(object a, object b, string op) =>
        new($"Arithmetic overflow in '{TypeConverter.ToInvariantText(a)} {op} {TypeConverter.ToInvariantText(b)}'.");

    private static object? MathOp(object? a, object? b, string op)
    {
        if (a is null or DBNull || b is null or DBNull) return null;

        // Handle date arithmetic (FW-6)
        if (a is DateTime dtA)
        {
            if (op == "-" && TryGetDateDiffDays(dtA, b, out var diffDays))
                return diffDays;
            if (op is "+" or "-")
                return AddDays(dtA, b, op, sign: op == "+" ? 1 : -1);
        }

        if (a is DateOnly dA)
        {
            if (op == "-" && TryGetDateDiffDays(dA.ToDateTime(TimeOnly.MinValue), b, out var diffDays))
                return diffDays;
            if (op is "+" or "-")
                return AddDays(dA, b, op, sign: op == "+" ? 1 : -1);
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
            // `+` on text is concatenation; no other operator has a meaning for text.
            if (op == "+") return TypeConverter.ToInvariantText(a) + TypeConverter.ToInvariantText(b);
            throw NotNumeric(a, b, op);
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
            throw Overflow(a, b, op);
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
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw NotNumeric(a, b, op);
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
        // NaN and infinity in give NaN and infinity out; only finite operands can overflow.
        if (!double.IsFinite(result) && double.IsFinite(da) && double.IsFinite(db)) throw Overflow(a, b, op);
        return result;
    }

    private static object? ShiftOp(object? a, object? b, string op)
    {
        if (a is null or DBNull || b is null or DBNull) return null;
        long valA, valB;
        try
        {
            valA = Convert.ToInt64(a, System.Globalization.CultureInfo.InvariantCulture);
            valB = Convert.ToInt64(b, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw NotNumeric(a, b, op);
        }

        // Shifting a long cannot overflow: bits fall off the end.
        return op == "<<" ? valA << (int)(valB & 63) : valA >> (int)(valB & 63);
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

    private static bool TryGetDateDiffDays(DateTime dtA, object b, out decimal diffDays)
    {
        if (b is DateTime dtB) { diffDays = (decimal)(dtA - dtB).TotalDays; return true; }
        if (b is DateOnly dB) { diffDays = (decimal)(dtA - dB.ToDateTime(TimeOnly.MinValue)).TotalDays; return true; }
        diffDays = 0;
        return false;
    }

    private static object AddDays(object a, object b, string op, int sign)
    {
        try
        {
            double days = sign * Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
            if (a is DateTime dt) return dt.AddDays(days);
            if (a is DateOnly dateOnly)
            {
                if (days == Math.Truncate(days) && days >= int.MinValue && days <= int.MaxValue)
                    return dateOnly.AddDays((int)days);
                return dateOnly.ToDateTime(TimeOnly.MinValue).AddDays(days);
            }
            throw NotNumeric(a, b, op);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentOutOfRangeException)
        {
            throw NotNumeric(a, b, op);
        }
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
