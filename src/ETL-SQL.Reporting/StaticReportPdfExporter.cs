using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Reporting
{
    public sealed class StaticReportPdfExporter(ETL_SQL.Common.ILogger? logger = null) : IReportPdfExporter
    {
        public Task<byte[]> ExportAsync(ReportManifest manifest, PdfExportOptions? options = null, CancellationToken cancellationToken = default) =>
            new PdfExporter(logger).ExportAsync(manifest, cancellationToken);
    }
}
