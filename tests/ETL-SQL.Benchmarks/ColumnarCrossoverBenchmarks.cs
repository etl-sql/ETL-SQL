using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Benchmarks;

/// <summary>
/// Row-reference versus native columnar crossover benchmarks for Phase 5 native-path admission.
/// Run explicitly with:
///   dotnet run --project tests/ETL-SQL.Benchmarks -c Release -- --filter *ColumnarCrossover*
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 5)]
public class ColumnarCrossoverBenchmarks
{
    private int[] _keys = Array.Empty<int>();
    private int[] _values = Array.Empty<int>();
    private bool[] _keyNulls = Array.Empty<bool>();
    private bool[] _valueNulls = Array.Empty<bool>();
    private int[] _rightKeys = Array.Empty<int>();
    private bool[] _rightKeyNulls = Array.Empty<bool>();
    private ColumnBatch _leftBatch = null!;
    private ColumnBatch _rightBatch = null!;

    [Params(1_000, 50_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(8675309);
        _keys = new int[RowCount];
        _values = new int[RowCount];
        _keyNulls = new bool[RowCount];
        _valueNulls = new bool[RowCount];

        for (var row = 0; row < RowCount; row++)
        {
            _keys[row] = random.Next(0, 257);
            _values[row] = (row * 17) % 10_000;
            _keyNulls[row] = row % 97 == 0;
            _valueNulls[row] = row % 89 == 0;
        }

        var rightCount = Math.Max(128, RowCount / 4);
        _rightKeys = new int[rightCount];
        _rightKeyNulls = new bool[rightCount];
        for (var row = 0; row < rightCount; row++)
        {
            _rightKeys[row] = random.Next(0, 257);
            _rightKeyNulls[row] = row % 83 == 0;
        }

        _leftBatch = CreateBatch(_keys, _values, _keyNulls, _valueNulls);
        _rightBatch = CreateBatch(_rightKeys, Enumerable.Range(0, rightCount).ToArray(), _rightKeyNulls, new bool[rightCount]);
        VerifyComparableChecksums();
    }

    [Benchmark(Baseline = true, Description = "RowReference_FilterProject")]
    public long RowReference_FilterProject()
    {
        long checksum = 0;
        for (var row = 0; row < _values.Length; row++)
        {
            if (_valueNulls[row] || _values[row] <= 5_000) continue;
            if (!_keyNulls[row]) checksum += _keys[row];
            checksum += _values[row];
        }

        return checksum;
    }

    [Benchmark(Description = "NativeColumnar_FilterProject")]
    public long NativeColumnar_FilterProject()
    {
        using var selected = ColumnBatchKernels.SelectComparison(_leftBatch, "Value", ColumnComparison.GreaterThan, 5_000);
        var keyColumn = _leftBatch.GetColumn<int>("Key");
        var valueColumn = _leftBatch.GetColumn<int>("Value");
        long checksum = 0;
        foreach (var row in selected.Indices.Span)
        {
            if (!keyColumn.IsNull(row)) checksum += keyColumn.Values.Span[row];
            checksum += valueColumn.Values.Span[row];
        }

        return checksum;
    }

    [Benchmark(Description = "RowReference_GroupAggregate")]
    public long RowReference_GroupAggregate()
    {
        var groups = new Dictionary<int, (long Rows, long NonNull, long Sum)>();
        for (var row = 0; row < _keys.Length; row++)
        {
            var key = _keyNulls[row] ? int.MinValue : _keys[row];
            groups.TryGetValue(key, out var state);
            state.Rows++;
            if (!_valueNulls[row])
            {
                state.NonNull++;
                state.Sum += _values[row];
            }

            groups[key] = state;
        }

        return groups.Values.Sum(state => state.Rows + state.NonNull + state.Sum);
    }

    [Benchmark(Description = "NativeColumnar_GroupAggregate")]
    public long NativeColumnar_GroupAggregate()
    {
        using var result = ColumnBatchGroupKernels.GroupAggregate<int, int>(_leftBatch, "Key", "Value");
        return (long)result.Groups.Values.Sum(state => state.RowCount + state.NonNullCount + state.Sum);
    }

    [Benchmark(Description = "RowReference_Sort")]
    public long RowReference_Sort()
    {
        return Enumerable.Range(0, _keys.Length)
            .OrderBy(row => _keyNulls[row])
            .ThenBy(row => _keys[row])
            .ThenBy(row => row)
            .Take(1_000)
            .Sum(row => (long)row);
    }

    [Benchmark(Description = "NativeColumnar_Sort")]
    public long NativeColumnar_Sort()
    {
        using var run = ColumnBatchSortKernels.CreateRun<int>(_leftBatch, "Key", nullsFirst: false);
        return run.Ordinals.Span[..Math.Min(1_000, run.Count)].ToArray().Sum(row => (long)row);
    }

    [Benchmark(Description = "RowReference_InnerJoin")]
    public long RowReference_InnerJoin()
    {
        var index = new Dictionary<int, int>();
        for (var row = 0; row < _rightKeys.Length; row++)
        {
            if (_rightKeyNulls[row]) continue;
            index.TryGetValue(_rightKeys[row], out var count);
            index[_rightKeys[row]] = count + 1;
        }

        long matches = 0;
        for (var row = 0; row < _keys.Length; row++)
        {
            if (!_keyNulls[row] && index.TryGetValue(_keys[row], out var count)) matches += count;
        }

        return matches;
    }

    [Benchmark(Description = "NativeColumnar_InnerJoin")]
    public long NativeColumnar_InnerJoin()
    {
        using var pairs = ColumnBatchJoinKernels.Join<int>(
            _leftBatch, "Key", _rightBatch, "Key", ColumnarJoinKind.Inner);
        return pairs.Count;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leftBatch.Dispose();
        _rightBatch.Dispose();
    }

    private static ColumnBatch CreateBatch(int[] keys, int[] values, bool[] keyNulls, bool[] valueNulls)
    {
        var schema = new ColumnBatchSchema(new[]
        {
            new ColumnBatchField("Key", typeof(int), "INT"),
            new ColumnBatchField("Value", typeof(int), "INT")
        });

        return new ColumnBatch(schema, new IColumnBuffer[]
        {
            new ColumnBuffer<int>(keys, keys.Length, ToBitmap(keyNulls)),
            new ColumnBuffer<int>(values, values.Length, ToBitmap(valueNulls))
        }, keys.Length);
    }

    private void VerifyComparableChecksums()
    {
        VerifyEqual(RowReference_FilterProject(), NativeColumnar_FilterProject(), nameof(NativeColumnar_FilterProject));
        VerifyEqual(RowReference_GroupAggregate(), NativeColumnar_GroupAggregate(), nameof(NativeColumnar_GroupAggregate));
        VerifyEqual(RowReference_Sort(), NativeColumnar_Sort(), nameof(NativeColumnar_Sort));
        VerifyEqual(RowReference_InnerJoin(), NativeColumnar_InnerJoin(), nameof(NativeColumnar_InnerJoin));

        static void VerifyEqual(long expected, long actual, string name)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    $"{name} checksum mismatch. Row reference={expected}; native={actual}.");
            }
        }
    }

    private static byte[] ToBitmap(bool[] nulls)
    {
        var bitmap = new byte[(nulls.Length + 7) / 8];
        for (var row = 0; row < nulls.Length; row++)
        {
            if (nulls[row]) bitmap[row >> 3] |= (byte)(1 << (row & 7));
        }

        return bitmap;
    }
}
