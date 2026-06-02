using System;

namespace ETL_SQL.Reporting
{
    public enum PdfExportMode
    {
        Static,
        Auto,
        Hosted,
        Browser
    }

    public sealed class PdfExportOptions
    {
        public PdfExportMode Mode { get; init; } = PdfExportMode.Static;
        public string? Host { get; init; }
        public string? BrowserPath { get; init; }
        public Action<string>? Warn { get; init; }

        public static PdfExportOptions Static { get; } = new();
    }
}
