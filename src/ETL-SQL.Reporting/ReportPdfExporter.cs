using System;

namespace ETL_SQL.Reporting
{
    public sealed class ReportPdfExporter(IReportPdfExporter? staticExporter = null) : IReportPdfExporter
    {
        private readonly IReportPdfExporter _staticExporter = staticExporter ?? new StaticReportPdfExporter();

        public byte[] Export(ReportManifest manifest, PdfExportOptions? options = null)
        {
            options ??= PdfExportOptions.Static;

            return options.Mode switch
            {
                PdfExportMode.Static => _staticExporter.Export(manifest, options),
                PdfExportMode.Auto => ExportAuto(manifest, options),
                PdfExportMode.Hosted => throw new InvalidOperationException(
                    "HOSTED PDF export is not implemented yet. Use PDF_MODE = STATIC, or PDF_MODE = AUTO to allow fallback."),
                PdfExportMode.Browser => throw new InvalidOperationException(
                    "BROWSER PDF export is not implemented yet. Use PDF_MODE = STATIC, or PDF_MODE = AUTO to allow fallback."),
                _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unsupported PDF export mode '{options.Mode}'.")
            };
        }

        private byte[] ExportAuto(ReportManifest manifest, PdfExportOptions options)
        {
            options.Warn?.Invoke("High-fidelity PDF export is not available yet; falling back to STATIC PDF export.");
            return _staticExporter.Export(manifest, options);
        }
    }
}
