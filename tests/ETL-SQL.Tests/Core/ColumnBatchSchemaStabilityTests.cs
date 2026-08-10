using System;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    /// <summary>
    /// The invariant every columnar spill write depends on: successive batches of one stream must
    /// agree on each column's physical type.
    ///
    /// <para><b>Why this exists as its own test.</b> The spill writer infers its Arrow schema from
    /// the first batch and then rejects any later batch that disagrees — so the requirement is real,
    /// but it was only ever enforced by an exception at write time, on data large enough to spill.
    /// Nothing stated the property where it is actually established, which is per-batch type
    /// inference. These tests do, against hand-built batches, in milliseconds.</para>
    ///
    /// <para>Two ordinary things break it, and neither is exotic. A nullable column can be entirely
    /// NULL in one batch, leaving no type evidence at all — and "no evidence" is not "string", though
    /// string is the easiest thing to return. More common still: engine rows are dynamically typed,
    /// so one batch can hold a <see cref="DateTime"/> in a column where the next holds the same
    /// instant as a formatted string. Either way the second batch infers a different physical type
    /// than the first, and the write fails.</para>
    /// </summary>
    public class ColumnBatchSchemaStabilityTests
    {
        [Theory]
        [InlineData("JoinDate")]   // DateTime  -> timestamp in the spill schema
        [InlineData("Price")]      // decimal   -> decimal128
        [InlineData("Visits")]     // int/long  -> int64
        public void AnAllNullBatch_KeepsTheColumnTypeEstablishedByTheFirstBatch(string nulledColumn)
        {
            var typed = BuildTable(withValues: true, nulledColumn: null);
            var allNull = BuildTable(withValues: true, nulledColumn: nulledColumn);

            using var first = ColumnBatchAdapter.FromDataTable(typed);
            using var second = ColumnBatchAdapter.FromDataTable(allNull, ColumnBatchAdapter.LogicalSchemaOf(first));

            for (var i = 0; i < first.Schema.Count; i++)
            {
                Assert.Equal(first.Schema.Fields[i].Name, second.Schema.Fields[i].Name);
                Assert.Equal(first.Schema.Fields[i].ElementType, second.Schema.Fields[i].ElementType);
            }
        }

        [Fact]
        public void InferringEachBatchIndependently_IsWhatBreaksTheInvariant()
        {
            // Pins the actual defect rather than the fix: with no schema carried forward, a batch
            // whose column is entirely NULL infers a different physical type from the batch before
            // it, and the spill writer has nothing to reconcile them with.
            var typed = BuildTable(withValues: true, nulledColumn: null);
            var allNull = BuildTable(withValues: true, nulledColumn: "JoinDate");

            using var first = ColumnBatchAdapter.FromDataTable(typed);
            using var independent = ColumnBatchAdapter.FromDataTable(allNull);

            var ordinal = first.Schema.GetOrdinal("JoinDate");
            Assert.Equal(typeof(DateTime), first.Schema.Fields[ordinal].ElementType);
            Assert.NotEqual(
                first.Schema.Fields[ordinal].ElementType,
                independent.Schema.Fields[ordinal].ElementType);
        }

        [Fact]
        public void ADateHeldAsTextInALaterBatch_KeepsTheEstablishedType()
        {
            // The case the failing samples actually hit: rows are dynamically typed, so the same
            // column arrives as DateTime in one batch and as a formatted string in the next.
            var typed = BuildTable(withValues: true, nulledColumn: null);
            var asText = BuildTable(withValues: true, nulledColumn: null);
            foreach (var row in asText.Rows)
                row["JoinDate"] = ((DateTime)row["JoinDate"]!).ToString("O");

            using var first = ColumnBatchAdapter.FromDataTable(typed);
            using var independent = ColumnBatchAdapter.FromDataTable(asText);
            using var carried = ColumnBatchAdapter.FromDataTable(asText, ColumnBatchAdapter.LogicalSchemaOf(first));

            var ordinal = first.Schema.GetOrdinal("JoinDate");
            Assert.NotEqual(
                first.Schema.Fields[ordinal].ElementType,
                independent.Schema.Fields[ordinal].ElementType);
            Assert.Equal(
                first.Schema.Fields[ordinal].ElementType,
                carried.Schema.Fields[ordinal].ElementType);
        }

        [Fact]
        public void AWhollyNullColumn_StillRoundTripsItsValues()
        {
            // Adopting the earlier type must not invent values: every cell stays NULL.
            var typed = BuildTable(withValues: true, nulledColumn: null);
            var allNull = BuildTable(withValues: true, nulledColumn: "JoinDate");

            using var first = ColumnBatchAdapter.FromDataTable(typed);
            using var second = ColumnBatchAdapter.FromDataTable(allNull, ColumnBatchAdapter.LogicalSchemaOf(first));

            var restored = ColumnBatchAdapter.ToDataTable(second);
            Assert.Equal(allNull.Rows.Count, restored.Rows.Count);
            foreach (var row in restored.Rows)
                Assert.True(row["JoinDate"] is null or DBNull);
        }

        private static DataTable BuildTable(bool withValues, string? nulledColumn)
        {
            var table = new DataTable();
            table.SetColumns(["Id", "JoinDate", "Price", "Visits", "Name"]);
            for (var i = 1; i <= 3; i++)
            {
                var row = table.NewRow();
                row["Id"] = (decimal)i;
                row["JoinDate"] = new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc);
                row["Price"] = 10.5m * i;
                row["Visits"] = (long)i;
                row["Name"] = $"row{i}";
                if (nulledColumn != null) row[nulledColumn] = null;
                table.Rows.Add(row);
            }
            return table;
        }
    }
}
