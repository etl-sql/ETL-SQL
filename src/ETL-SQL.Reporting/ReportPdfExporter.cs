using System;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Reporting
{
    public sealed class ReportPdfExporter(
        IReportPdfExporter? staticExporter = null,
        IReportPdfExporter? highFidelityExporter = null,
        ETL_SQL.Common.ILogger? logger = null) : IReportPdfExporter
    {
        private readonly IReportPdfExporter _staticExporter = staticExporter ?? new StaticReportPdfExporter(logger);
        private readonly IReportPdfExporter _highFidelityExporter = highFidelityExporter ?? new BrowserReportPdfExporter();

        public async Task<byte[]> ExportAsync(ReportManifest manifest, PdfExportOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= PdfExportOptions.Static;

            return options.Mode switch
            {
                PdfExportMode.Static => await _staticExporter.ExportAsync(manifest, options, cancellationToken),
                PdfExportMode.Auto => await ExportAutoAsync(manifest, options, cancellationToken),
                PdfExportMode.Hosted => await _highFidelityExporter.ExportAsync(manifest, options, cancellationToken),
                PdfExportMode.Browser => await _highFidelityExporter.ExportAsync(manifest, options, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unsupported PDF export mode '{options.Mode}'.")
            };
        }

        private async Task<byte[]> ExportAutoAsync(ReportManifest manifest, PdfExportOptions options, CancellationToken cancellationToken)
        {
            if (HasPaginatedLayout(manifest))
                return await _staticExporter.ExportAsync(manifest, options, cancellationToken);

            if (!string.IsNullOrWhiteSpace(options.Host))
            {
                try
                {
                    return await _highFidelityExporter.ExportAsync(manifest, options, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    options.Warn?.Invoke($"High-fidelity PDF export failed ({ex.Message}); falling back to STATIC PDF export.");
                }
            }
            else
            {
                options.Warn?.Invoke("High-fidelity PDF export is not configured; falling back to STATIC PDF export.");
            }

            return await _staticExporter.ExportAsync(manifest, options, cancellationToken);
        }

        private static bool HasPaginatedLayout(ReportManifest manifest)
        {
            foreach (var page in manifest.Pages)
            {
                if (string.Equals(page.Mode, "PAGINATED", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
