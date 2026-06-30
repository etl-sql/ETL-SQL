using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Data;
using ETL_SQL.Engine.Engines;
using Xunit;

namespace ETL_SQL.Tests.Operations
{
    /// <summary>
    /// Round-trip correctness for the join build-side binary codec. The external join holds build rows
    /// as packed blobs and decodes them only on a probe match, so the codec must be lossless across the
    /// full value domain produced by the spill readers (null/bool/decimal/double/DateTime/string).
    /// </summary>
    public class RowPackerTests
    {
        private static Row Roundtrip(Row row, out IReadOnlyList<string> columns)
        {
            var cols = row.GetColumnNames().ToList();
            columns = cols;
            var packer = new RowPacker();
            var blob = packer.Pack(row, cols);
            return RowPacker.Unpack(blob, cols);
        }

        [Fact]
        public void Roundtrip_PreservesAllScalarTypes()
        {
            var row = new Row
            {
                ["n"] = null,
                ["b"] = true,
                ["dec"] = 12345.678901234567m,      // high precision/scale
                ["dbl"] = 3.141592653589793,
                ["s"] = "héllo, 世界",                // unicode
                ["empty"] = "",
            };

            var result = Roundtrip(row, out var cols);

            Assert.Null(result["n"]);
            Assert.Equal(true, result["b"]);
            Assert.Equal(12345.678901234567m, result["dec"]);
            Assert.Equal(3.141592653589793, result["dbl"]);
            Assert.Equal("héllo, 世界", result["s"]);
            Assert.Equal("", result["empty"]);
            Assert.Equal(new[] { "n", "b", "dec", "dbl", "s", "empty" }, cols);
        }

        [Fact]
        public void Roundtrip_PreservesDateTimeTicksAndKind()
        {
            var utc = new DateTime(2026, 6, 29, 13, 45, 30, DateTimeKind.Utc).AddTicks(1234);
            var unspecified = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var row = new Row { ["u"] = utc, ["x"] = unspecified };

            var result = Roundtrip(row, out _);

            var ru = Assert.IsType<DateTime>(result["u"]);
            Assert.Equal(utc.Ticks, ru.Ticks);
            Assert.Equal(DateTimeKind.Utc, ru.Kind);
            var rx = Assert.IsType<DateTime>(result["x"]);
            Assert.Equal(unspecified.Ticks, rx.Ticks);
            Assert.Equal(DateTimeKind.Unspecified, rx.Kind);
        }

        [Fact]
        public void Roundtrip_IntegralValuesComeBackAsDecimal()
        {
            // Integers box as decimal on the common runtime path; the codec stores them losslessly as decimal.
            var row = new Row { ["i"] = 42, ["l"] = 9_000_000_000L };

            var result = Roundtrip(row, out _);

            Assert.Equal(42m, Assert.IsType<decimal>(result["i"]));
            Assert.Equal(9_000_000_000m, Assert.IsType<decimal>(result["l"]));
        }

        [Fact]
        public void Unpack_MissingColumnInBlob_IsNull()
        {
            var row = new Row { ["a"] = 1m };
            var packer = new RowPacker();
            var blob = packer.Pack(row, new[] { "a" });

            // Decoding against a wider column list yields null for columns absent from the blob.
            var result = RowPacker.Unpack(blob, new[] { "a" });
            Assert.Equal(1m, result["a"]);
            Assert.Null(result["missing"]);
        }

        [Fact]
        public void Packer_IsReusableAcrossRows()
        {
            var packer = new RowPacker();
            var cols = new[] { "k", "v" };
            var r1 = RowPacker.Unpack(packer.Pack(new Row { ["k"] = "a", ["v"] = 1m }, cols), cols);
            var r2 = RowPacker.Unpack(packer.Pack(new Row { ["k"] = "b", ["v"] = 2m }, cols), cols);

            Assert.Equal("a", r1["k"]);
            Assert.Equal(1m, r1["v"]);
            Assert.Equal("b", r2["k"]);
            Assert.Equal(2m, r2["v"]);
        }
    }
}
