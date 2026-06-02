using System;

namespace ETL_SQL.Reporting
{
    public sealed class ReportPdfExporter(
        IReportPdfExporter? staticExporter = null,
        IReportPdfExporter? highFidelityExporter = null) : IReportPdfExporter
    {
        private readonly IReportPdfExporter _staticExporter = staticExporter ?? new StaticReportPdfExporter();
        private readonly IReportPdfExporter _highFidelityExporter = highFidelityExporter ?? new BrowserReportPdfExporter();

        public byte[] Export(ReportManifest manifest, PdfExportOptions? options = null)
        {
            options ??= PdfExportOptions.Static;

            return options.Mode switch
            {
                PdfExportMode.Static => _staticExporter.Export(manifest, options),
                PdfExportMode.Auto => ExportAuto(manifest, options),
                PdfExportMode.Hosted => _highFidelityExporter.Export(manifest, options),
                PdfExportMode.Browser => _highFidelityExporter.Export(manifest, options),
                _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unsupported PDF export mode '{options.Mode}'.")
            };
        }

        private byte[] ExportAuto(ReportManifest manifest, PdfExportOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.Host))
            {
                try
                {
                    return _highFidelityExporter.Export(manifest, options);
                }
                catch (Exception ex)
                {
                    options.Warn?.Invoke($"High-fidelity PDF export failed ({ex.Message}); falling back to STATIC PDF export.");
                }
            }
            else
            {
                options.Warn?.Invoke("High-fidelity PDF export is not configured; falling back to STATIC PDF export.");
            }

            return _staticExporter.Export(manifest, options);
        }
    }
}
