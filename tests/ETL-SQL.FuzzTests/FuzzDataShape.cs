using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ETL_SQL.FuzzTests
{
    /// <summary>
    /// How NULL is distributed through a fuzzed column.
    ///
    /// <para><see cref="WholeBatch"/> is the one that matters most and the one no lane produced: a
    /// column that is entirely NULL for a run of rows has <b>no type evidence</b> in that batch, so
    /// whatever infers a schema from values sees nothing and guesses. That is not a NULL-semantics
    /// bug — those are well covered — it is NULL as absence of evidence, where "we don't know" and
    /// "genuinely absent" take the same branch and it is always the benign one.</para>
    /// </summary>
    public enum NullDensity
    {
        None,
        Sparse,
        WholeBatch,
        All
    }

    /// <summary>
    /// A seeded data shape for the fuzz table <c>(ID INT, Price DECIMAL, Name VARCHAR(50),
    /// TotalAmount DECIMAL)</c>.
    ///
    /// <para>The grammar walk varies the query; this varies what the query runs against. Row counts
    /// are drawn to straddle batch and spill boundaries rather than to be large — the interesting
    /// cases are one row, exactly a batch, and one past it, not a million.</para>
    /// </summary>
    public sealed record FuzzDataShape(
        int RowCount,
        NullDensity PriceNulls,
        NullDensity NameNulls,
        NullDensity TotalNulls,
        bool WideStrings)
    {
        /// <summary>
        /// Row counts chosen around the boundaries that have actually produced defects: the default
        /// batch size, the spill-lane batch size of 7, and the ±1 either side of each. A count that
        /// divides evenly into batches hides exactly the bugs that batch boundaries cause, so the
        /// awkward numbers are deliberate.
        /// </summary>
        private static readonly int[] RowCounts = { 0, 1, 2, 6, 7, 8, 13, 49, 50, 51, 99, 101, 257 };

        public static FuzzDataShape FromSeed(int seed)
        {
            // A distinct stream from the grammar walk's, derived from the same seed so that
            // ETLSQL_FUZZ_SEED reproduces the data and the queries together.
            var rng = new Random(unchecked(seed * 2654435761u.GetHashCode() + 0x5EED));

            return new FuzzDataShape(
                RowCounts[rng.Next(RowCounts.Length)],
                PickDensity(rng),
                PickDensity(rng),
                PickDensity(rng),
                rng.Next(4) == 0);
        }

        /// <summary>
        /// Weighted so most runs still have ordinary data — an all-NULL column every run would make
        /// most generated queries trivially empty and waste the walk.
        /// </summary>
        private static NullDensity PickDensity(Random rng) => rng.Next(10) switch
        {
            0 or 1 or 2 or 3 or 4 => NullDensity.None,
            5 or 6 or 7 => NullDensity.Sparse,
            8 => NullDensity.WholeBatch,
            _ => NullDensity.All
        };

        /// <summary>
        /// INSERT statements in chunks, because one statement with hundreds of VALUES tuples is a
        /// parser stress test rather than a data shape, and this is meant to vary the data only.
        /// </summary>
        public IEnumerable<string> BuildInserts(string tableName)
        {
            const int chunk = 50;
            // A run long enough to fall wholly inside one batch at the spill lane's BatchSize of 7.
            int wholeBatchEnd = Math.Min(RowCount, 7);

            for (int start = 0; start < RowCount; start += chunk)
            {
                var tuples = new List<string>();
                for (int i = start; i < Math.Min(start + chunk, RowCount); i++)
                {
                    tuples.Add(string.Join(", ", new[]
                    {
                        (i + 1).ToString(CultureInfo.InvariantCulture),
                        Value(PriceNulls, i, wholeBatchEnd, () => Decimal(i)),
                        Value(NameNulls, i, wholeBatchEnd, () => Text(i)),
                        Value(TotalNulls, i, wholeBatchEnd, () => Decimal(i * 3 + 1))
                    }));
                }

                if (tuples.Count > 0)
                    yield return $"INSERT INTO {tableName} VALUES ({string.Join("), (", tuples)});";
            }
        }

        private static string Value(NullDensity density, int index, int wholeBatchEnd, Func<string> value) =>
            density switch
            {
                NullDensity.All => "null",
                NullDensity.WholeBatch => index < wholeBatchEnd ? "null" : value(),
                NullDensity.Sparse => index % 4 == 3 ? "null" : value(),
                _ => value()
            };

        /// <summary>Values that stress numeric handling: zero, negatives, and a long scale.</summary>
        private static string Decimal(int index) => (index % 7) switch
        {
            0 => "0",
            1 => "-1",
            2 => "0.000001",
            3 => "-99999.99",
            4 => "12345678.9",
            _ => ((index * 10.5) + 1).ToString("0.###", CultureInfo.InvariantCulture)
        };

        /// <summary>
        /// Strings that have historically confused typed sinks: numeric-looking text, a leading
        /// zero, an embedded quote, and — when the shape calls for it — something past the
        /// VARCHAR(50) the column declares.
        /// </summary>
        private string Text(int index)
        {
            string body = (index % 6) switch
            {
                0 => "Alice",
                1 => "00123",
                2 => "4.50",
                3 => "1e5",
                4 => "O''Brien",
                _ => "name" + index.ToString(CultureInfo.InvariantCulture)
            };

            if (WideStrings && index % 11 == 0)
                body = new string('x', 60);

            return "'" + body + "'";
        }

        public override string ToString() =>
            $"rows={RowCount} price={PriceNulls} name={NameNulls} total={TotalNulls} wide={WideStrings}";
    }
}
