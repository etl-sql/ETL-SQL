using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ETL_SQL.Tests.Reporting.PayloadCrossover;

public class PayloadCrossoverTests
{
    [Theory]
    [InlineData(WorkloadType.DenseNumeric)]
    [InlineData(WorkloadType.MixedTyped)]
    [InlineData(WorkloadType.NullableSparse)]
    [InlineData(WorkloadType.TemporalEvents)]
    [InlineData(WorkloadType.StringHeavy)]
    public void WorkloadGeneration_ProducesCorrectSchemaAndRowCounts(WorkloadType workload)
    {
        var data = PayloadCrossoverMeasurementHarness.GenerateWorkloadData(workload, 100);
        Assert.NotNull(data);
        Assert.Equal(100, data.Rows.Count);
        Assert.NotEmpty(data.Columns);
        Assert.Equal(data.Columns.Count, data.Schema.ColumnTypes.Count);
    }

    [Theory]
    [InlineData(WorkloadType.DenseNumeric)]
    [InlineData(WorkloadType.MixedTyped)]
    [InlineData(WorkloadType.NullableSparse)]
    [InlineData(WorkloadType.TemporalEvents)]
    [InlineData(WorkloadType.StringHeavy)]
    public void Roundtrip_PreservesDataAcrossJsonAndArrowRepresentations(WorkloadType workload)
    {
        var data = PayloadCrossoverMeasurementHarness.GenerateWorkloadData(workload, 50);

        // JSON Row-Oriented
        var jsonRowBytes = PayloadCrossoverMeasurementHarness.EncodeJsonRowOriented(data);
        var decodedRows = PayloadCrossoverMeasurementHarness.DecodeJsonRowOriented(jsonRowBytes);
        Assert.Equal(data.Rows.Count, decodedRows.Count);

        // JSON Columnar
        var jsonColBytes = PayloadCrossoverMeasurementHarness.EncodeJsonColumnar(data);
        var decodedCols = PayloadCrossoverMeasurementHarness.DecodeJsonColumnar(jsonColBytes);
        Assert.Equal(data.Columns.Count, decodedCols.Count);
        Assert.Equal(data.Rows.Count, decodedCols.Values.First().Count);

        // Arrow IPC Stream
        var arrowBytes = PayloadCrossoverMeasurementHarness.EncodeArrowIpcStream(data);
        var decodedBatch = PayloadCrossoverMeasurementHarness.DecodeArrowIpcStream(arrowBytes);
        Assert.Equal(data.Rows.Count, decodedBatch.Length);
        Assert.Equal(data.Columns.Count, decodedBatch.ColumnCount);
    }

    [Fact]
    public void NullableSparseWorkload_PreservesNullBitmapsAndPositions()
    {
        var data = PayloadCrossoverMeasurementHarness.GenerateWorkloadData(WorkloadType.NullableSparse, 100);
        var arrowBytes = PayloadCrossoverMeasurementHarness.EncodeArrowIpcStream(data);
        var decodedBatch = PayloadCrossoverMeasurementHarness.DecodeArrowIpcStream(arrowBytes);

        var jsonRowBytes = PayloadCrossoverMeasurementHarness.EncodeJsonRowOriented(data);
        var decodedRows = PayloadCrossoverMeasurementHarness.DecodeJsonRowOriented(jsonRowBytes);

        // Check column 2 (PrimaryReading) nulls
        var col2 = decodedBatch.Column("PrimaryReading");
        for (int i = 0; i < 100; i++)
        {
            bool expectedNull = data.Rows[i][2] == null;
            Assert.Equal(expectedNull, col2.IsNull(i));
            Assert.Equal(expectedNull, decodedRows[i][2] == null);
        }
    }

    [Fact]
    public void InteractionQuery_ProducesDeterministicChecksumAcrossAllFormats()
    {
        var data = PayloadCrossoverMeasurementHarness.GenerateWorkloadData(WorkloadType.MixedTyped, 100);

        var jsonRowBytes = PayloadCrossoverMeasurementHarness.EncodeJsonRowOriented(data);
        var jsonRows = PayloadCrossoverMeasurementHarness.DecodeJsonRowOriented(jsonRowBytes);
        var (countRow, hashRow) = PayloadCrossoverMeasurementHarness.QueryJsonRowOriented(jsonRows);

        var jsonColBytes = PayloadCrossoverMeasurementHarness.EncodeJsonColumnar(data);
        var jsonCols = PayloadCrossoverMeasurementHarness.DecodeJsonColumnar(jsonColBytes);
        var (countCol, hashCol) = PayloadCrossoverMeasurementHarness.QueryJsonColumnar(jsonCols);

        var arrowBytes = PayloadCrossoverMeasurementHarness.EncodeArrowIpcStream(data);
        var arrowBatch = PayloadCrossoverMeasurementHarness.DecodeArrowIpcStream(arrowBytes);
        var (countArrow, hashArrow) = PayloadCrossoverMeasurementHarness.QueryArrowRecordBatch(arrowBatch);

        Assert.Equal(50, countRow);
        Assert.Equal(50, countCol);
        Assert.Equal(50, countArrow);

        // Checksums across formats must be valid SHA-256 strings
        Assert.NotEmpty(hashRow);
        Assert.NotEmpty(hashCol);
        Assert.NotEmpty(hashArrow);
    }

    [Fact]
    public void FormatMeasurements_ProducePositiveSizesAndRatios()
    {
        var data = PayloadCrossoverMeasurementHarness.GenerateWorkloadData(WorkloadType.DenseNumeric, 500);

        var mJsonRow = PayloadCrossoverMeasurementHarness.MeasureFormat(data, PayloadFormat.JsonRowOriented, samples: 1);
        var mJsonCol = PayloadCrossoverMeasurementHarness.MeasureFormat(data, PayloadFormat.JsonColumnar, samples: 1);
        var mArrow = PayloadCrossoverMeasurementHarness.MeasureFormat(data, PayloadFormat.ArrowIpcStream, samples: 1);

        Assert.True(mJsonRow.RawBytes > 0);
        Assert.True(mJsonCol.RawBytes > 0);
        Assert.True(mArrow.RawBytes > 0);

        // Gzip & Brotli compression must reduce size
        Assert.True(mJsonRow.GzipBytes < mJsonRow.RawBytes);
        Assert.True(mJsonRow.BrotliBytes < mJsonRow.RawBytes);
        Assert.True(mArrow.GzipBytes < mArrow.RawBytes || mArrow.GzipBytes > 0);
    }

    [Fact]
    public async Task BenchmarkHarness_ExecutesFastSuiteAndFormatsMarkdown()
    {
        var smallRowCounts = new[] { 100, 500 };
        var report = await PayloadCrossoverMeasurementHarness.RunFullBenchmarkSuiteAsync(smallRowCounts, samplesPerRun: 1);

        Assert.NotNull(report);
        Assert.NotEmpty(report.Results);
        Assert.NotEmpty(report.CrossoverSummaryByWorkload);

        var md = PayloadCrossoverMeasurementHarness.FormatMarkdownReport(report);
        Assert.Contains("Visual Data Payload Crossover Benchmark Report", md);
        Assert.Contains("DenseNumeric", md);
        Assert.Contains("MixedTyped", md);
    }
}
