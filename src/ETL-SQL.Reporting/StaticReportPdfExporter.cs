namespace ETL_SQL.Reporting
{
    public sealed class StaticReportPdfExporter : IReportPdfExporter
    {
        public byte[] Export(ReportManifest manifest, PdfExportOptions? options = null) =>
            new PdfExporter().Export(manifest);
    }
}
