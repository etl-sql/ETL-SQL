using System;
using System.Collections.Generic;

namespace ETL_SQL.TUI.UI
{
    /// <summary>Severity rank for an editor gutter marker — higher is more severe (worst wins per line).</summary>
    public enum DiagnosticLevel { Info = 0, Warning = 1, Error = 2 }

    /// <summary>
    /// Derives the editor gutter overlay from the diagnostics list: the worst severity per
    /// 1-based source line, plus the glyph/colour used to draw the marker in the line-number
    /// gutter. Pure logic — unit-testable without a console.
    /// </summary>
    public static class DiagnosticGutter
    {
        /// <summary>Maps a diagnostic's severity string (parser/lint) to a gutter level.</summary>
        public static DiagnosticLevel Classify(string? severity)
        {
            if (string.IsNullOrEmpty(severity)) return DiagnosticLevel.Info;
            if (severity.StartsWith("Error", StringComparison.OrdinalIgnoreCase)) return DiagnosticLevel.Error;
            if (severity.StartsWith("Warn", StringComparison.OrdinalIgnoreCase)) return DiagnosticLevel.Warning;
            return DiagnosticLevel.Info;
        }

        /// <summary>Worst diagnostic severity per 1-based source line.</summary>
        public static Dictionary<int, DiagnosticLevel> BuildLineMap(IEnumerable<EditorDiagnostic> diagnostics)
        {
            var map = new Dictionary<int, DiagnosticLevel>();
            foreach (var d in diagnostics)
            {
                var level = Classify(d.Severity);
                if (!map.TryGetValue(d.Line, out var existing) || level > existing)
                    map[d.Line] = level;
            }
            return map;
        }

        /// <summary>The single-cell marker glyph drawn in the gutter for a severity. Deliberately
        /// narrow (text-presentation) symbols so they occupy exactly one terminal cell.</summary>
        public static string Glyph(DiagnosticLevel level) => level switch
        {
            DiagnosticLevel.Error => "✗",
            DiagnosticLevel.Warning => "!",
            _ => "•"
        };

        /// <summary>The Spectre colour name for the marker glyph.</summary>
        public static string Color(DiagnosticLevel level) => level switch
        {
            DiagnosticLevel.Error => "red",
            DiagnosticLevel.Warning => "yellow",
            _ => "blue"
        };
    }
}
