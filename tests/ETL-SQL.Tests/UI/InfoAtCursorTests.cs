using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Word detection and content composition for Shift+F1 "info at cursor" (function/keyword
    /// help + lineage). Pure logic — no console required.
    /// </summary>
    public class InfoAtCursorTests
    {
        static InfoAtCursorTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        [Theory]
        [InlineData("UPPER(x)", 2, "UPPER")]   // inside the word
        [InlineData("UPPER(x)", 5, "UPPER")]   // cursor just after the word (on '(')
        [InlineData("UPPER(x)", 6, "x")]       // the argument
        [InlineData("a @var b", 4, "@var")]    // variable sigil included
        [InlineData("   ", 1, null)]           // whitespace -> none
        [InlineData("", 0, null)]              // empty line -> none
        public void WordAt_FindsIdentifierUnderOrBeforeCursor(string line, int col, string? expected)
        {
            Assert.Equal(expected, InfoAtCursor.WordAt(line, col));
        }

        [Fact]
        public void Build_ReturnsNull_WhenNothingMatches()
        {
            Assert.Null(InfoAtCursor.Build("   ", 0, 1, null, null, null, out _));
        }

        [Fact]
        public void Build_ReturnsKeywordHelp_FromRegistry()
        {
            var help = ETL_SQL.TUI.Program.ServiceProvider.GetService(typeof(ILanguageHelpRegistry)) as ILanguageHelpRegistry;
            Assert.NotNull(help);

            // The topic must be a single word by InfoAtCursor's own definition, not merely
            // space-free: help topics are derived from doc filenames, so hyphenated pages such as
            // 'data-prep' or 'asof-join' are legitimate topics that WordAt correctly splits at the
            // hyphen ('data'), leaving nothing for Build to resolve. Selecting on WordAt keeps this
            // test asserting the registry lookup rather than tripping over topic naming.
            string? topic = help!.GetTopics().FirstOrDefault(t =>
                help.GetHelp(t) != null
                && InfoAtCursor.WordAt(t, 0) == t);
            Assert.NotNull(topic); // the embedded help resources should provide at least one keyword

            var result = InfoAtCursor.Build(topic!, 0, 0, null, help, null, out var title);
            Assert.NotNull(result);
            Assert.Equal(topic, title);
            Assert.Equal(help.GetHelp(topic!)!.Trim(), result);
        }

        [Fact]
        public void Build_IncludesLineage_ForPositionInsideEntry()
        {
            var entries = new List<LineageEntry>
            {
                new LineageEntry("Orders", "SELECT")
                {
                    TargetColumn = "Total",
                    Line = 2, Column = 1, EndLine = 2, EndColumn = 20,
                    SourceTables = new List<string> { "raw_orders" },
                    DerivedFromDescriptions = "qty * price"
                }
            };

            // Cursor at line index 1 (1-based line 2), column index 5 -> inside [1,20].
            var result = InfoAtCursor.Build("SELECT Total", 1, 5, null, null, entries, out _);

            Assert.NotNull(result);
            Assert.Contains("Orders.Total", result);
            Assert.Contains("raw_orders", result);
            Assert.Contains("qty * price", result);
        }

        [Fact]
        public void BuildLineageFromEntries_FallsBackToWordMatch()
        {
            var entries = new List<LineageEntry>
            {
                new LineageEntry("Orders", "SELECT")
                {
                    TargetColumn = "Total",
                    Line = 9, Column = 1, EndLine = 9, EndColumn = 5, // span far from the cursor
                    DerivedFromDescriptions = "qty * price"
                }
            };

            // Cursor on the word "Total" (col 8) on line 0 — not in the span, matched by name.
            var result = InfoAtCursor.BuildLineageFromEntries(entries, "SELECT Total", 0, 8, out var title);

            Assert.NotNull(result);
            Assert.Equal("Total", title);
            Assert.Contains("Orders.Total", result);
        }

        [Fact]
        public void BuildLineageFromEntries_MatchesSourceColumn()
        {
            var entries = new List<LineageEntry>
            {
                new LineageEntry("Orders", "SELECT INTO")
                {
                    TargetColumn = "Total",
                    Line = 9, Column = 1, EndLine = 9, EndColumn = 5,
                    SourceColumns = new List<string> { "Qty", "Price" }
                }
            };

            // Cursor on the source column "Qty" -> should still resolve the lineage entry.
            var result = InfoAtCursor.BuildLineageFromEntries(entries, "SELECT Qty", 0, 8, out _);
            Assert.NotNull(result);
            Assert.Contains("Orders.Total", result);
        }

        [Fact]
        public void BuildAvailableList_ListsTargets()
        {
            var entries = new List<LineageEntry>
            {
                new LineageEntry("Orders", "SELECT INTO") { TargetColumn = "Total" },
                new LineageEntry("Orders", "SELECT INTO") { TargetColumn = "Qty" }
            };

            var list = InfoAtCursor.BuildAvailableList(entries, "nope");
            Assert.Contains("No lineage for **nope**", list);
            Assert.Contains("Orders.Total", list);
            Assert.Contains("Orders.Qty", list);
        }

        [Fact]
        public void Build_NoLineage_WhenCursorOutsideEntry()
        {
            var entries = new List<LineageEntry>
            {
                new LineageEntry("Orders", "SELECT") { TargetColumn = "Total", Line = 5, Column = 1, EndLine = 5, EndColumn = 10 }
            };

            // Cursor on line 1 — far from the entry on line 5, and no help registries.
            Assert.Null(InfoAtCursor.Build("SELECT Total", 0, 3, null, null, entries, out _));
        }
    }
}
