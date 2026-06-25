using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Reporting
{
    public sealed class StaticReportPdfExporter : IReportPdfExporter
    {
        public byte[] Export(ReportManifest manifest, PdfExportOptions? options = null) =>
            new PdfExporter().Export(manifest);

        public Task<byte[]> ExportAsync(ReportManifest manifest, PdfExportOptions? options = null, CancellationToken cancellationToken = default) =>
            new PdfExporter().ExportAsync(manifest, cancellationToken);
    }
}
