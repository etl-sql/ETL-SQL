using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;

namespace ETL_SQL.Engine.Spill;

/// <summary>
/// Internal extension of <see cref="ISpillReader"/> providing <see cref="ValueTask{TResult}"/>
/// row reads and direct unboxed sort-key extraction from buffered Arrow batches.
/// </summary>
internal interface ISortSpillReader : ISpillReader
{
    ValueTask<Row?> ReadRowValueTaskAsync();

    bool SupportsSortFastPath { get; }

    bool TryReadBufferedSortRow(
        string[] keyColumnNames,
        string[]? sentinelColumnNames,
        bool stripSentinelColumns,
        [NotNullWhen(true)] out Row? row,
        out SortKey key0,
        Span<SortKey> extraKeys);

    ValueTask<bool> MoveToNextBatchAsync();
}
