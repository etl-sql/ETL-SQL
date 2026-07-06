using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace ETL_SQL.Core.Data;

public enum ColumnComparison
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

public enum ColumnArithmetic
{
    Add,
    Subtract,
    Multiply,
    Divide
}

/// <summary>Pooled row ordinals selected from a native column batch.</summary>
public sealed class SelectionVector : IDisposable
{
    private int[]? _indices;

    internal SelectionVector(int capacity)
    {
        _indices = ArrayPool<int>.Shared.Rent(Math.Max(1, capacity));
    }

    public int Count { get; private set; }
    public ReadOnlyMemory<int> Indices
        => (_indices ?? throw new ObjectDisposedException(nameof(SelectionVector))).AsMemory(0, Count);

    internal void Add(int index)
        => (_indices ?? throw new ObjectDisposedException(nameof(SelectionVector)))[Count++] = index;

    public static SelectionVector FromIndices(IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        var values = indices as IReadOnlyCollection<int> ?? indices.ToArray();
        var result = new SelectionVector(values.Count);
        try
        {
            foreach (var index in values)
            {
                if (index < 0) throw new ArgumentOutOfRangeException(nameof(indices));
                result.Add(index);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public static SelectionVector FromIndices(ReadOnlySpan<int> indices)
    {
        var result = new SelectionVector(indices.Length);
        try
        {
            foreach (var index in indices)
            {
                if (index < 0) throw new ArgumentOutOfRangeException(nameof(indices));
                result.Add(index);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        var indices = Interlocked.Exchange(ref _indices, null);
        if (indices != null) ArrayPool<int>.Shared.Return(indices, clearArray: false);
    }
}

/// <summary>
/// Zero-copy projection over a retained native batch. Disposing the projection releases its source
/// reference; projected buffers remain owned by the source batch.
/// </summary>
public sealed class ColumnBatchProjection : IDisposable
{
    private ColumnBatch? _source;
    private readonly int[] _ordinals;

    internal ColumnBatchProjection(ColumnBatch source, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (columns.Count == 0) throw new ArgumentException("A projection requires at least one column.", nameof(columns));
        _ordinals = columns.Select(source.Schema.GetOrdinal).ToArray();
        if (_ordinals.Distinct().Count() != _ordinals.Length)
            throw new ArgumentException("A projection cannot contain duplicate columns.", nameof(columns));
        Schema = new ColumnBatchSchema(_ordinals.Select(ordinal => source.Schema.Fields[ordinal]));
        _source = source.Retain();
    }

    public ColumnBatchSchema Schema { get; }
    public int RowCount => GetSource().RowCount;
    public IColumnBuffer GetColumn(string name)
    {
        var projectedOrdinal = Schema.GetOrdinal(name);
        return GetSource().Columns[_ordinals[projectedOrdinal]];
    }

    public void Dispose() => Interlocked.Exchange(ref _source, null)?.Dispose();
    private ColumnBatch GetSource() => _source ?? throw new ObjectDisposedException(nameof(ColumnBatchProjection));
}

/// <summary>Scalar native-buffer kernels used before SIMD and expression-plan integration.</summary>
public static class ColumnBatchKernels
{
    public static ColumnBatchProjection Project(ColumnBatch batch, params string[] columns)
        => new(batch, columns);

    public static SelectionVector SelectNull(
        ColumnBatch batch,
        string columnName,
        bool isNull,
        SelectionVector? input = null,
        CancellationToken cancellationToken = default)
    {
        var column = batch.GetColumn(columnName);
        return Select(batch.RowCount, input, index => column.IsNull(index) == isNull, cancellationToken);
    }

    public static SelectionVector SelectBoolean(
        ColumnBatch batch,
        string columnName,
        bool expected,
        SelectionVector? input = null,
        CancellationToken cancellationToken = default)
        => SelectComparison(batch, columnName, ColumnComparison.Equal, expected ? (byte)1 : (byte)0, input, cancellationToken);

    public static SelectionVector SelectComparison<T>(
        ColumnBatch batch,
        string columnName,
        ColumnComparison comparison,
        T constant,
        SelectionVector? input = null,
        CancellationToken cancellationToken = default) where T : unmanaged, IComparable<T>
    {
        var column = batch.GetColumn<T>(columnName);
        var values = column.Values;
        return Select(batch.RowCount, input, index =>
        {
            if (column.IsNull(index)) return false; // SQL comparison with NULL is UNKNOWN.
            var order = values.Span[index].CompareTo(constant);
            return comparison switch
            {
                ColumnComparison.Equal => order == 0,
                ColumnComparison.NotEqual => order != 0,
                ColumnComparison.LessThan => order < 0,
                ColumnComparison.LessThanOrEqual => order <= 0,
                ColumnComparison.GreaterThan => order > 0,
                ColumnComparison.GreaterThanOrEqual => order >= 0,
                _ => throw new ArgumentOutOfRangeException(nameof(comparison))
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Applies the engine's coercing string comparison semantics directly over a UTF-8 column.
    /// Values are decoded individually, but no Row/DataTable object graph is materialized.
    /// </summary>
    public static SelectionVector SelectUtf8Comparison(
        ColumnBatch batch,
        string columnName,
        ColumnComparison comparison,
        object constant,
        bool caseSensitive,
        SelectionVector? input = null,
        CancellationToken cancellationToken = default)
    {
        var column = batch.GetUtf8Column(columnName);
        byte[]? constantUtf8 = null;
        var canCompareEncodedEquality = comparison is ColumnComparison.Equal or ColumnComparison.NotEqual
            && constant is string constantString
            && !decimal.TryParse(constantString, out _)
            && !EvaluationUtils.TryToDateTime(constantString, out _);
        if (canCompareEncodedEquality)
            constantUtf8 = Encoding.UTF8.GetBytes((string)constant);

        return Select(batch.RowCount, input, index =>
        {
            if (column.IsNull(index)) return false;
            var encodedValue = column.GetUtf8Bytes(index);
            if (constantUtf8 != null
                && (caseSensitive || IsAscii(encodedValue) && IsAscii(constantUtf8)))
            {
                var equal = caseSensitive
                    ? encodedValue.SequenceEqual(constantUtf8)
                    : EqualsAsciiIgnoreCase(encodedValue, constantUtf8);
                return comparison == ColumnComparison.Equal ? equal : !equal;
            }

            var value = Encoding.UTF8.GetString(encodedValue);
            if (comparison is ColumnComparison.Equal or ColumnComparison.NotEqual)
            {
                var equal = EvaluationUtils.IsSoftEqual(value, constant, caseSensitive: caseSensitive);
                return comparison == ColumnComparison.Equal ? equal : !equal;
            }

            var order = EvaluationUtils.CompareConstants(value, constant, caseSensitive);
            return comparison switch
            {
                ColumnComparison.LessThan => order < 0,
                ColumnComparison.LessThanOrEqual => order <= 0,
                ColumnComparison.GreaterThan => order > 0,
                ColumnComparison.GreaterThanOrEqual => order >= 0,
                _ => throw new ArgumentOutOfRangeException(nameof(comparison))
            };
        }, cancellationToken);
    }

    private static bool IsAscii(ReadOnlySpan<byte> value)
    {
        foreach (var item in value)
            if ((item & 0x80) != 0) return false;
        return true;
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            var l = left[i] is >= (byte)'a' and <= (byte)'z' ? (byte)(left[i] - 32) : left[i];
            var r = right[i] is >= (byte)'a' and <= (byte)'z' ? (byte)(right[i] - 32) : right[i];
            if (l != r) return false;
        }
        return true;
    }

    public static SelectionVector SelectArithmeticComparison<T>(
        ColumnBatch batch,
        string columnName,
        ColumnArithmetic arithmetic,
        T operand,
        ColumnComparison comparison,
        T constant,
        SelectionVector? input = null,
        CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        var column = batch.GetColumn<T>(columnName);
        var values = column.Values;
        return Select(batch.RowCount, input, index =>
        {
            if (column.IsNull(index)) return false;
            var value = arithmetic switch
            {
                ColumnArithmetic.Add => checked(values.Span[index] + operand),
                ColumnArithmetic.Subtract => checked(values.Span[index] - operand),
                ColumnArithmetic.Multiply => checked(values.Span[index] * operand),
                ColumnArithmetic.Divide => values.Span[index] / operand,
                _ => throw new ArgumentOutOfRangeException(nameof(arithmetic))
            };
            var order = value.CompareTo(constant);
            return comparison switch
            {
                ColumnComparison.Equal => order == 0,
                ColumnComparison.NotEqual => order != 0,
                ColumnComparison.LessThan => order < 0,
                ColumnComparison.LessThanOrEqual => order <= 0,
                ColumnComparison.GreaterThan => order > 0,
                ColumnComparison.GreaterThanOrEqual => order >= 0,
                _ => throw new ArgumentOutOfRangeException(nameof(comparison))
            };
        }, cancellationToken);
    }

    internal static SelectionVector Union(
        int rowCount,
        SelectionVector left,
        SelectionVector right,
        SelectionVector? input = null,
        CancellationToken cancellationToken = default)
    {
        var byteCount = checked((int)((rowCount + 7L) / 8));
        var bitmap = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteCount));
        bitmap.AsSpan(0, byteCount).Clear();
        var result = new SelectionVector(input?.Count ?? rowCount);
        try
        {
            void Mark(SelectionVector vector)
            {
                foreach (var row in vector.Indices.Span)
                {
                    if ((uint)row >= (uint)rowCount)
                        throw new ArgumentOutOfRangeException(nameof(vector), "Selection vector contains an invalid row ordinal.");
                    bitmap[row >> 3] |= (byte)(1 << (row & 7));
                }
            }
            Mark(left);
            Mark(right);

            if (input == null)
            {
                for (var row = 0; row < rowCount; row++)
                {
                    if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if ((bitmap[row >> 3] & (1 << (row & 7))) != 0) result.Add(row);
                }
            }
            else
            {
                var candidates = input.Indices.Span;
                for (var position = 0; position < candidates.Length; position++)
                {
                    if ((position & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var row = candidates[position];
                    if ((uint)row >= (uint)rowCount)
                        throw new ArgumentOutOfRangeException(nameof(input), "Selection vector contains an invalid row ordinal.");
                    if ((bitmap[row >> 3] & (1 << (row & 7))) != 0) result.Add(row);
                }
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bitmap, clearArray: true);
        }
    }

    public static long Count(
        ColumnBatch batch,
        string? columnName = null,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default)
    {
        var column = columnName == null ? null : batch.GetColumn(columnName);
        long count = 0;
        VisitOrdinals(batch.RowCount, selection, cancellationToken, index =>
        {
            if (column == null || !column.IsNull(index)) count++;
        });
        return count;
    }

    public static T? Sum<T>(
        ColumnBatch batch,
        string columnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        var column = batch.GetColumn<T>(columnName);
        var values = column.Values;
        var sum = T.Zero;
        var hasValue = false;
        VisitOrdinals(batch.RowCount, selection, cancellationToken, index =>
        {
            if (!column.IsNull(index))
            {
                sum = checked(sum + values.Span[index]);
                hasValue = true;
            }
        });
        return hasValue ? sum : null;
    }

    public static decimal? SumDecimal<T>(
        ColumnBatch batch,
        string columnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        var column = batch.GetColumn<T>(columnName);
        var values = column.Values;
        decimal sum = 0;
        var hasValue = false;
        VisitOrdinals(batch.RowCount, selection, cancellationToken, index =>
        {
            if (column.IsNull(index)) return;
            sum = checked(sum + decimal.CreateChecked(values.Span[index]));
            hasValue = true;
        });
        return hasValue ? sum : null;
    }

    public static NativeMinMax<T> MinMax<T>(
        ColumnBatch batch,
        string columnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default) where T : unmanaged, IComparable<T>
    {
        var column = batch.GetColumn<T>(columnName);
        var values = column.Values;
        var hasValue = false;
        var min = default(T);
        var max = default(T);
        VisitOrdinals(batch.RowCount, selection, cancellationToken, index =>
        {
            if (column.IsNull(index)) return;
            var value = values.Span[index];
            if (!hasValue)
            {
                min = max = value;
                hasValue = true;
            }
            else
            {
                if (value.CompareTo(min) < 0) min = value;
                if (value.CompareTo(max) > 0) max = value;
            }
        });
        return new NativeMinMax<T>(hasValue, min, max);
    }

    public static double? Average<T>(
        ColumnBatch batch,
        string columnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        var column = batch.GetColumn<T>(columnName);
        var values = column.Values;
        double sum = 0;
        long count = 0;
        VisitOrdinals(batch.RowCount, selection, cancellationToken, index =>
        {
            if (column.IsNull(index)) return;
            sum += double.CreateChecked(values.Span[index]);
            count++;
        });
        return count == 0 ? null : sum / count;
    }

    public static decimal? AverageDecimal<T>(
        ColumnBatch batch,
        string columnName,
        SelectionVector? selection = null,
        CancellationToken cancellationToken = default) where T : unmanaged, INumber<T>
    {
        var column = batch.GetColumn<T>(columnName);
        var values = column.Values;
        decimal sum = 0;
        long count = 0;
        VisitOrdinals(batch.RowCount, selection, cancellationToken, index =>
        {
            if (column.IsNull(index)) return;
            sum = checked(sum + decimal.CreateChecked(values.Span[index]));
            count++;
        });
        return count == 0 ? null : sum / count;
    }

    private static SelectionVector Select(
        int rowCount,
        SelectionVector? input,
        Func<int, bool> predicate,
        CancellationToken cancellationToken)
    {
        var candidateCount = input?.Count ?? rowCount;
        var result = new SelectionVector(candidateCount);
        try
        {
            if (input == null)
            {
                for (var row = 0; row < rowCount; row++)
                {
                    if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (predicate(row)) result.Add(row);
                }
            }
            else
            {
                var candidates = input.Indices.Span;
                for (var position = 0; position < candidates.Length; position++)
                {
                    if ((position & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var row = candidates[position];
                    if ((uint)row >= (uint)rowCount)
                        throw new ArgumentOutOfRangeException(nameof(input), "Selection vector contains an invalid row ordinal.");
                    if (predicate(row)) result.Add(row);
                }
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void VisitOrdinals(
        int rowCount,
        SelectionVector? selection,
        CancellationToken cancellationToken,
        Action<int> visit)
    {
        if (selection == null)
        {
            for (var row = 0; row < rowCount; row++)
            {
                if ((row & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                visit(row);
            }
            return;
        }

        var ordinals = selection.Indices.Span;
        for (var position = 0; position < ordinals.Length; position++)
        {
            if ((position & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var row = ordinals[position];
            if ((uint)row >= (uint)rowCount)
                throw new ArgumentOutOfRangeException(nameof(selection), "Selection vector contains an invalid row ordinal.");
            visit(row);
        }
    }
}

public readonly record struct NativeMinMax<T>(bool HasValue, T Min, T Max);
