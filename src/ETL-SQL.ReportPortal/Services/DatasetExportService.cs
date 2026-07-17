using System.IO.Pipelines;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;

namespace ETL_SQL.ReportPortal.Services;

public sealed class DatasetExportService(
    DatasetViewerService viewer,
    ILogger<DatasetExportService> logger)
{
    public async Task<DatasetExportResult> PrepareAsync(
        Dataset dataset,
        string? sort,
        string? dir,
        string? search,
        IEnumerable<DatasetColumnFilterDto> filters,
        string? format)
    {
        var safeName = string.Concat(dataset.Name.Where(c => char.IsLetterOrDigit(c) || c == '_'));
        var (filteredRows, columns) = await viewer.PrepareExportAsync(dataset.Id, sort, dir, search, filters);

        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            var pipe = new Pipe();
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var stream = pipe.Writer.AsStream();
                    await viewer.ExportXlsxAsync(columns, filteredRows, stream, dataset.Name);
                    await pipe.Writer.CompleteAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "XLSX export of dataset {DatasetId} failed mid-stream.", dataset.Id);
                    await pipe.Writer.CompleteAsync(ex);
                }
            });

            return new DatasetExportResult(
                pipe.Reader.AsStream(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{safeName}.xlsx");
        }

        var csvPipe = new Pipe();
        _ = Task.Run(async () =>
        {
            try
            {
                await using var stream = csvPipe.Writer.AsStream();
                await viewer.ExportCsvAsync(columns, filteredRows, stream);
                await csvPipe.Writer.CompleteAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CSV export of dataset {DatasetId} failed mid-stream.", dataset.Id);
                await csvPipe.Writer.CompleteAsync(ex);
            }
        });

        return new DatasetExportResult(
            csvPipe.Reader.AsStream(),
            "text/csv; charset=utf-8",
            $"{safeName}.csv");
    }
}

public sealed record DatasetExportResult(Stream Stream, string ContentType, string FileName);
