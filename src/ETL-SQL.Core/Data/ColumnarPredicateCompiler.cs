using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core.Data;

/// <summary>Conservative AST binder for native selection kernels. Unsupported expressions fall back.</summary>
public static class ColumnarPredicateCompiler
{
    public static bool TrySelect(
        ColumnBatch batch,
        Expression expression,
        out SelectionVector? selection,
        SelectionVector? input = null,
        CancellationToken cancellationToken = default,
        bool? caseSensitiveComparison = null)
    {
        selection = null;
        if (expression is IsNullExpression { Expression: IdentifierExpression nullIdentifier } nullExpression)
        {
            var name = nullIdentifier.Name.Split('.').Last();
            try
            {
                selection = ColumnBatchKernels.SelectNull(
                    batch, name, isNull: !nullExpression.Not, input, cancellationToken);
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }
        if (expression is BinaryExpression { Operator: TokenType.AND } andExpression)
        {
            if (!TrySelect(batch, andExpression.Left, out var left, input, cancellationToken, caseSensitiveComparison) || left == null)
                return false;
            try
            {
                if (!TrySelect(batch, andExpression.Right, out selection, left, cancellationToken, caseSensitiveComparison))
                {
                    selection?.Dispose();
                    selection = null;
                    return false;
                }
                return true;
            }
            finally
            {
                left.Dispose();
            }
        }
        if (expression is BinaryExpression { Operator: TokenType.OR } orExpression)
        {
            SelectionVector? left = null;
            SelectionVector? right = null;
            try
            {
                if (!TrySelect(batch, orExpression.Left, out left, input, cancellationToken, caseSensitiveComparison) || left == null
                    || !TrySelect(batch, orExpression.Right, out right, input, cancellationToken, caseSensitiveComparison) || right == null)
                    return false;
                selection = ColumnBatchKernels.Union(batch.RowCount, left, right, input, cancellationToken);
                return true;
            }
            finally
            {
                left?.Dispose();
                right?.Dispose();
            }
        }

        if (expression is not BinaryExpression comparison || !TryMapComparison(comparison.Operator, out var comparisonKind))
            return false;

        if (TryBindValue(comparison.Left, out var columnName, out var arithmetic, out var operand)
            && comparison.Right is LiteralExpression rightLiteral)
            return TryDispatch(batch, columnName, arithmetic, operand, comparisonKind, rightLiteral.Value, input, cancellationToken, caseSensitiveComparison, out selection);

        if (comparison.Left is LiteralExpression leftLiteral
            && TryBindValue(comparison.Right, out columnName, out arithmetic, out operand))
            return TryDispatch(batch, columnName, arithmetic, operand, Reverse(comparisonKind), leftLiteral.Value, input, cancellationToken, caseSensitiveComparison, out selection);

        return false;
    }

    private static bool TryBindValue(
        Expression expression,
        out string columnName,
        out ColumnArithmetic? arithmetic,
        out object? operand)
    {
        if (expression is IdentifierExpression identifier)
        {
            columnName = identifier.Name.Split('.').Last();
            arithmetic = null;
            operand = null;
            return true;
        }
        if (expression is BinaryExpression binary
            && binary.Left is IdentifierExpression arithmeticIdentifier
            && binary.Right is LiteralExpression literal
            && TryMapArithmetic(binary.Operator, out var operation))
        {
            columnName = arithmeticIdentifier.Name.Split('.').Last();
            arithmetic = operation;
            operand = literal.Value;
            return true;
        }
        columnName = string.Empty;
        arithmetic = null;
        operand = null;
        return false;
    }

    private static bool TryDispatch(
        ColumnBatch batch,
        string columnName,
        ColumnArithmetic? arithmetic,
        object? operand,
        ColumnComparison comparison,
        object? constant,
        SelectionVector? input,
        CancellationToken cancellationToken,
        bool? caseSensitiveComparison,
        out SelectionVector? selection)
    {
        selection = null;
        if (constant == null || constant == DBNull.Value) return false;
        IColumnBuffer column;
        try { column = batch.GetColumn(columnName); }
        catch (KeyNotFoundException) { return false; }

        try
        {
            if (column.ElementType == typeof(byte)) selection = Apply(batch, columnName, arithmetic, operand, comparison, Convert.ToByte(constant), input, cancellationToken);
            else if (column.ElementType == typeof(short)) selection = Apply(batch, columnName, arithmetic, operand, comparison, Convert.ToInt16(constant), input, cancellationToken);
            else if (column.ElementType == typeof(int)) selection = Apply(batch, columnName, arithmetic, operand, comparison, Convert.ToInt32(constant), input, cancellationToken);
            else if (column.ElementType == typeof(long)) selection = Apply(batch, columnName, arithmetic, operand, comparison, Convert.ToInt64(constant), input, cancellationToken);
            else if (column.ElementType == typeof(float)) selection = Apply(batch, columnName, arithmetic, operand, comparison, Convert.ToSingle(constant), input, cancellationToken);
            else if (column.ElementType == typeof(double)) selection = Apply(batch, columnName, arithmetic, operand, comparison, Convert.ToDouble(constant), input, cancellationToken);
            else if (column.ElementType == typeof(decimal)) selection = Apply(batch, columnName, arithmetic, operand, comparison, Convert.ToDecimal(constant), input, cancellationToken);
            else if (column is Utf8ColumnBuffer && arithmetic == null && caseSensitiveComparison.HasValue)
                selection = ColumnBatchKernels.SelectUtf8Comparison(
                    batch, columnName, comparison, constant, caseSensitiveComparison.Value, input, cancellationToken);
            else return false;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            selection?.Dispose();
            selection = null;
            return false;
        }
    }

    private static SelectionVector Apply<T>(
        ColumnBatch batch,
        string columnName,
        ColumnArithmetic? arithmetic,
        object? operand,
        ColumnComparison comparison,
        T constant,
        SelectionVector? input,
        CancellationToken cancellationToken) where T : unmanaged, System.Numerics.INumber<T>
        => arithmetic == null
            ? ColumnBatchKernels.SelectComparison(batch, columnName, comparison, constant, input, cancellationToken)
            : ColumnBatchKernels.SelectArithmeticComparison(
                batch, columnName, arithmetic.Value, (T)Convert.ChangeType(operand!, typeof(T)), comparison, constant, input, cancellationToken);

    private static bool TryMapComparison(TokenType token, out ColumnComparison comparison)
    {
        comparison = token switch
        {
            TokenType.EQUALS => ColumnComparison.Equal,
            TokenType.NOT_EQUALS => ColumnComparison.NotEqual,
            TokenType.LESS_THAN => ColumnComparison.LessThan,
            TokenType.LESS_EQUALS => ColumnComparison.LessThanOrEqual,
            TokenType.GREATER_THAN => ColumnComparison.GreaterThan,
            TokenType.GREATER_EQUALS => ColumnComparison.GreaterThanOrEqual,
            _ => default
        };
        return token is TokenType.EQUALS or TokenType.NOT_EQUALS or TokenType.LESS_THAN
            or TokenType.LESS_EQUALS or TokenType.GREATER_THAN or TokenType.GREATER_EQUALS;
    }

    private static bool TryMapArithmetic(TokenType token, out ColumnArithmetic arithmetic)
    {
        arithmetic = token switch
        {
            TokenType.PLUS => ColumnArithmetic.Add,
            TokenType.MINUS => ColumnArithmetic.Subtract,
            TokenType.STAR => ColumnArithmetic.Multiply,
            TokenType.SLASH => ColumnArithmetic.Divide,
            _ => default
        };
        return token is TokenType.PLUS or TokenType.MINUS or TokenType.STAR or TokenType.SLASH;
    }

    private static ColumnComparison Reverse(ColumnComparison comparison) => comparison switch
    {
        ColumnComparison.LessThan => ColumnComparison.GreaterThan,
        ColumnComparison.LessThanOrEqual => ColumnComparison.GreaterThanOrEqual,
        ColumnComparison.GreaterThan => ColumnComparison.LessThan,
        ColumnComparison.GreaterThanOrEqual => ColumnComparison.LessThanOrEqual,
        _ => comparison
    };
}
