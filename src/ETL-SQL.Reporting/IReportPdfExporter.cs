using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Reporting
{
    public interface IReportPdfExporter
    {
        Task<byte[]> ExportAsync(ReportManifest manifest, PdfExportOptions? options = null, CancellationToken cancellationToken = default);
    }
}
