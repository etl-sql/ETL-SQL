using System;
using System.Collections.Generic;

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
        public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }
        public Action<string>? Warn { get; init; }

        public static PdfExportOptions Static { get; } = new();
    }
}
