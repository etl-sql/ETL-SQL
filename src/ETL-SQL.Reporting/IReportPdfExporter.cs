using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Reporting
{
    public interface IReportPdfExporter
    {
        byte[] Export(ReportManifest manifest, PdfExportOptions? options = null);
        Task<byte[]> ExportAsync(ReportManifest manifest, PdfExportOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.Run(() => Export(manifest, options), cancellationToken);
    }
}
