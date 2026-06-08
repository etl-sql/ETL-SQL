using Xunit;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Pure gutter-marker derivation from the diagnostics list — no console required.</summary>
    public class DiagnosticGutterTests
    {
        private static EditorDiagnostic Diag(string severity, int line) =>
            new("PARSER", severity, "msg", line, 1);

        [Theory]
        [InlineData("Error", DiagnosticLevel.Error)]
        [InlineData("error", DiagnosticLevel.Error)]
        [InlineData("Warning", DiagnosticLevel.Warning)]
        [InlineData("Info", DiagnosticLevel.Info)]
        [InlineData("Suggestion", DiagnosticLevel.Info)]
        [InlineData("", DiagnosticLevel.Info)]
        public void Classify_MapsSeverityStrings(string severity, DiagnosticLevel expected)
        {
            Assert.Equal(expected, DiagnosticGutter.Classify(severity));
        }

        [Fact]
        public void BuildLineMap_GroupsByLine()
        {
            var map = DiagnosticGutter.BuildLineMap(new[] { Diag("Error", 3), Diag("Warning", 7) });

            Assert.Equal(DiagnosticLevel.Error, map[3]);
            Assert.Equal(DiagnosticLevel.Warning, map[7]);
            Assert.False(map.ContainsKey(1));
        }

        [Fact]
        public void BuildLineMap_WorstSeverityWinsPerLine()
        {
            // Same line carries both a warning and an error → error marker wins.
            var map = DiagnosticGutter.BuildLineMap(new[] { Diag("Warning", 5), Diag("Error", 5) });
            Assert.Equal(DiagnosticLevel.Error, map[5]);

            // Order-independent.
            var map2 = DiagnosticGutter.BuildLineMap(new[] { Diag("Error", 5), Diag("Warning", 5) });
            Assert.Equal(DiagnosticLevel.Error, map2[5]);
        }

        [Fact]
        public void BuildLineMap_Empty_ReturnsEmpty()
        {
            Assert.Empty(DiagnosticGutter.BuildLineMap(new EditorDiagnostic[0]));
        }

        [Theory]
        [InlineData(DiagnosticLevel.Error, "✗", "red")]
        [InlineData(DiagnosticLevel.Warning, "!", "yellow")]
        [InlineData(DiagnosticLevel.Info, "•", "blue")]
        public void GlyphAndColor_AreDistinctPerLevel(DiagnosticLevel level, string glyph, string color)
        {
            Assert.Equal(glyph, DiagnosticGutter.Glyph(level));
            Assert.Equal(color, DiagnosticGutter.Color(level));
        }
    }
}
