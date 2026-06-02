namespace ETL_SQL.Reporting
{
    public interface IReportPdfExporter
    {
        byte[] Export(ReportManifest manifest, PdfExportOptions? options = null);
    }
}
